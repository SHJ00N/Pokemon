// 선택 제한시간. 남은 시간은 클라이언트가 안내(GAME_STARTED, CHOOSE_ACTION 등)를 받은 순간부터 스스로 세어 표시한다.
// 서버는 매초 시간을 보내지 않고, 마감 시각까지 답이 왔는지만 확인한다.
partial class RoomManager
{
    public const int TeamTimeLimitSeconds = 300;    // 포켓몬·기술 선택 5분. 넘기면 바로 패배
    public const int ActionTimeLimitSeconds = 60;   // 턴마다 행동 선택(기절 후 교체 포함)

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

    // 시간이 지나면 사용할 수 있는 기술을 무작위로 고른다. 교체를 기다리는 중이면 남은 첫 포켓몬을 내보낸다.
    private void ActionTimeout(GameRoom room)
    {
        Battle battle = room.Battle!;
        BattleSlot host = battle.HostSide.Slots[0], guest = battle.GuestSide.Slots[0];   // 1:1은 편마다 자리 하나
        room.ActionDeadline = null;

        // 턴 도중 교체나 기절 후 교체를 기다리는 중이면 교체할 쪽만, 아니면 아직 고르지 않은 쪽이 늦은 것이다.
        bool midTurn = host.SwitchingMidTurn || guest.SwitchingMidTurn;
        bool forcedSwitch = !midTurn && (host.MustSwitch || guest.MustSwitch);
        BattleSlot[] late = new[] { host, guest }
            .Where(side => midTurn ? side.SwitchingMidTurn : forcedSwitch ? side.MustSwitch : !side.HasChosen).ToArray();
        if (late.Length == 0)
            return;
        foreach (BattleSlot side in late)
        {
            if (midTurn || forcedSwitch)
            {
                battle.Log(side, "시간 초과로 교체할 포켓몬을 자동 선택했다!", "상대가 시간 초과로 교체할 포켓몬을 자동 선택했다!");
                int replacement = Enumerable.Range(0, side.Side.Team.Length)
                    .First(index => index != side.Active && side.Side.Team[index].Hp > 0);
                if (midTurn)
                {
                    Proceed(room, battle.CompleteMidTurnSwitch(side, replacement));
                    return;
                }
                battle.SendInReplacement(side, replacement);
            }
            else
            {
                int[] available = Enumerable.Range(1, side.Current.Moves.Length)
                    .Where(slot => battle.MoveBlockedBy(side, slot) == null).ToArray();
                side.Struggling = available.Length == 0;
                int slot = side.Struggling ? 1 : available[battle.Random.Next(available.Length)];
                side.ChosenMove = slot;
                string name = side.Struggling ? data.MovesById[Battle.StruggleMoveId].Name : side.Current.Moves[slot - 1].Move.Name;
                string selected = Battle.WithJosa(name, "을", "를");
                battle.Log(side, $"시간 초과로 {selected} 자동 선택했다!", $"상대가 시간 초과로 {selected} 자동 선택했다!");
            }
        }

        if (forcedSwitch)
            Proceed(room, battle.CheckEnd());
        else if (battle.AllChosen)
            Proceed(room, battle.ResolveTurn());
    }

    // BATTLE_END <WIN|LOSE|DRAW> TIMEOUT
    private static string TimeoutResult(bool meLate, bool otherLate) =>
        "BATTLE_END " + (meLate && otherLate ? "DRAW" : meLate ? "LOSE" : "WIN") + " TIMEOUT";
}
