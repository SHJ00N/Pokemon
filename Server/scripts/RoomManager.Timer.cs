// 선택 제한시간. 남은 시간은 클라이언트가 안내(GAME_STARTED, CHOOSE_ACTION 등)를 받은 순간부터 스스로 세어 표시한다.
// 서버는 매초 시간을 보내지 않고, 마감 시각까지 답이 왔는지만 확인한다.
partial class RoomManager
{
    public const int TeamTimeLimitSeconds = 60;     // 포켓몬·기술 선택. 넘기면 바로 패배
    public const int ActionTimeLimitSeconds = 60;   // 턴마다 행동 선택(기절 후 교체 포함)
    private const int MaxTimeouts = 2;              // 한 전투에서 행동 선택 시간을 이만큼 넘기면 패배

    // 클라이언트는 안내를 받은 뒤부터 세므로, 전송 지연과 확인 주기만큼 서버가 조금 더 기다린다.
    private const int GraceMilliseconds = 1000;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    // 시스템 시계가 바뀌어도 영향을 받지 않도록 부팅 후 경과 시간(ms)으로 잰다.
    private static long Deadline(int seconds) => Environment.TickCount64 + seconds * 1000L + GraceMilliseconds;

    // 서버가 켜져 있는 동안 계속 돈다. Program.Main에서 한 번 시작한다.
    public async Task RunTimersAsync()
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                lock (roomsLock)
                    CheckTimeouts(Environment.TickCount64);
            }
            catch (Exception ex)
            {
                // 한 방의 오류로 다른 방의 제한시간 확인이 멈추지 않게 한다.
                Console.WriteLine($"제한시간 확인 오류: {ex}");
            }
        }
    }

    // roomsLock 안에서 호출한다.
    private void CheckTimeouts(long now)
    {
        foreach (GameRoom room in rooms.Values)
        {
            if (room.BattleOver)
                continue;
            if (room.TeamDeadline <= now)
                TeamTimeout(room);
            else if (room.ActionDeadline <= now)
                ActionTimeout(room);
        }
    }

    // 포켓몬·기술 선택 시간이 지나면 고르지 못한 쪽이 바로 진다. 둘 다 못 골랐으면 무승부다.
    private static void TeamTimeout(GameRoom room)
    {
        bool hostLate = room.HostTeam == null, guestLate = room.GuestTeam == null;
        room.TeamDeadline = null;
        room.BattleOver = true;
        room.Host.QueueMessage(TimeoutResult(hostLate, guestLate));
        room.Guest?.QueueMessage(TimeoutResult(guestLate, hostLate));
        Console.WriteLine($"방 {room.Id} 포켓몬 선택 시간 초과");
    }

    // 행동 선택 시간이 지나면 고르지 않은 쪽은 이번 턴을 쉰다. 기절 후 교체였다면 남은 첫 포켓몬이 나간다.
    // 한 전투에서 MaxTimeouts번 넘기면 진다.
    private void ActionTimeout(GameRoom room)
    {
        BattleSide host = room.HostSide!, guest = room.GuestSide!;
        room.ActionDeadline = null;

        // 기절 후 교체를 기다리는 중이면 교체할 쪽만, 아니면 아직 고르지 않은 쪽이 늦은 것이다.
        bool forcedSwitch = host.MustSwitch || guest.MustSwitch;
        BattleSide[] late = new[] { host, guest }
            .Where(side => forcedSwitch ? side.MustSwitch : !side.HasChosen).ToArray();
        if (late.Length == 0)
            return;
        foreach (BattleSide side in late)
            side.Timeouts++;

        bool hostLost = host.Timeouts >= MaxTimeouts, guestLost = guest.Timeouts >= MaxTimeouts;
        if (hostLost || guestLost)
        {
            room.BattleOver = true;
            host.Player.QueueMessage(TimeoutResult(hostLost, guestLost));
            guest.Player.QueueMessage(TimeoutResult(guestLost, hostLost));
            Console.WriteLine($"방 {room.Id} 전투 종료 (행동 선택 시간 초과 {MaxTimeouts}번)");
            return;
        }

        foreach (BattleSide side in late)
        {
            Log(room, side, $"시간 안에 행동을 고르지 않았다! ({MaxTimeouts - side.Timeouts}번 더 넘기면 패배)",
                "상대가 시간 안에 행동을 고르지 않았다!");
            if (forcedSwitch)
            {
                // 쓰러진 출전 포켓몬의 HP는 0이므로 HP가 남은 첫 포켓몬은 항상 교체 대상이다.
                ChangeActive(room, side, Array.FindIndex(side.Team, pokemon => pokemon.Hp > 0));
                side.MustSwitch = false;
                Log(room, side, $"가라, {side.Current.Info.Name}!",
                    $"상대가 {WithJosa(side.Current.Info.Name, "을", "를")} 내보냈다!");
            }
            else
                side.SkipTurn = true;
        }

        if (forcedSwitch)
        {
            SendBattleState(room);
            StartTurn(room);
        }
        else
            TryResolveTurn(room);   // 늦은 쪽은 쉬고 상대의 행동과 턴 끝 처리만 진행한다.
    }

    // BATTLE_END <WIN|LOSE|DRAW> TIMEOUT
    private static string TimeoutResult(bool meLate, bool otherLate) =>
        "BATTLE_END " + (meLate && otherLate ? "DRAW" : meLate ? "LOSE" : "WIN") + " TIMEOUT";
}
