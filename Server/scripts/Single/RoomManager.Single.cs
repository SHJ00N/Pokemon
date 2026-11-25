using Pokemon.Protocol;

// 1:1 대전 진행: 행동 받기, 안내(CHOOSE_ACTION 등), 결과·재도전. 규칙 계산은 Battle이 한다.
partial class RoomManager
{
    // 양쪽 팀 선택이 끝나면 전투를 만들고 첫 턴을 안내한다.
    private void StartBattle(GameRoom room, PokemonSelection[] hostTeam, PokemonSelection[] guestTeam)
    {
        room.TeamDeadline = null;
        Battle battle = room.Battle = new Battle(data, room.Random,
            CreateSide(room.Host, hostTeam), CreateSide(room.Guest!, guestTeam));
        battle.SendBattleState();
        Proceed(room, TurnProgress.NextTurn);
    }

    // slot: 출전 중인 포켓몬이 현재 사용할 수 있는 기술 목록에서 몇 번째인지
    public void UseMove(ClientSession session, int slot, bool megaEvolve = false, int? megaFormId = null)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out Battle battle, out BattleSide me, out BattleSide other) ||
                room.BattleOver || me.MustSwitch || other.MustSwitch || me.HasChosen ||
                me.SwitchingMidTurn || other.SwitchingMidTurn)
            {
                session.QueueMessage("ERROR 지금은 행동을 선택할 수 없습니다.");
                return;
            }

            PokemonInfo? megaForm = null;
            if (megaEvolve)
            {
                var options = battle.GetMegaForms(me);
                if (options.Count == 0)
                {
                    session.QueueMessage("ERROR 지금은 메가진화할 수 없습니다.");
                    return;
                }
                megaForm = options.FirstOrDefault(form => megaFormId == null || form.Id == megaFormId);
                if (megaForm == null)
                {
                    session.QueueMessage("ERROR 현재 포켓몬이 메가진화할 수 있는 폼을 선택하세요.");
                    return;
                }
            }

            if (slot < 1 || slot > me.Current.Moves.Length)
            {
                session.QueueMessage("ERROR 현재 포켓몬의 기술 목록에 있는 슬롯을 선택하세요.");
                return;
            }

            // PP가 부족하거나 효과로 막힌 기술은 고를 수 없다.
            // 쓸 수 있는 기술이 하나도 없으면 어느 기술을 골라도 발버둥을 쓴다.
            me.Struggling = false;
            if (battle.MoveBlockedBy(me, other, slot) is string cause)
            {
                if (!battle.MustStruggle(me, other))
                {
                    session.QueueMessage($"ERROR {cause} 때문에 {Battle.WithJosa(me.Current.Moves[slot - 1].Move.Name, "을", "를")} 쓸 수 없습니다.");
                    return;
                }
                me.Struggling = true;
            }

            me.ChosenMove = slot;
            me.ChosenMegaForm = megaForm;
            if (battle.AllChosen)
                Proceed(room, battle.ResolveTurn());
        }
    }

    // teamSlot: 교체할 팀 번호 (1~3)
    public void SwitchPokemon(ClientSession session, int teamSlot)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out Battle battle, out BattleSide me, out BattleSide other) ||
                room.BattleOver)
            {
                session.QueueMessage("ERROR 지금은 교체할 수 없습니다.");
                return;
            }

            int index = teamSlot - 1;
            if (index == me.Active || me.Team[index].Hp == 0)
            {
                session.QueueMessage("ERROR 교체할 수 없는 포켓몬입니다.");
                return;
            }

            // 유턴·배턴터치 등으로 턴 도중에 교체하는 중이면 교체하고 남은 턴을 이어서 처리한다.
            if (me.SwitchingMidTurn)
            {
                Proceed(room, battle.CompleteMidTurnSwitch(me, index));
                return;
            }
            if (other.SwitchingMidTurn)
            {
                session.QueueMessage("ERROR 지금은 교체할 수 없습니다.");
                return;
            }

            // 쓰러진 포켓몬 대신 내보내는 교체는 턴을 쓰지 않는다. 상대도 추가로 공격하지 못한다.
            // 나오자마자 설치기로 쓰러지면 다시 고르고, 남은 포켓몬이 없으면 전투가 끝난다.
            if (me.MustSwitch)
            {
                battle.SendInReplacement(me, index);
                Proceed(room, battle.CheckEnd());
                return;
            }

            if (other.MustSwitch || me.HasChosen)
            {
                session.QueueMessage("ERROR 지금은 교체할 수 없습니다.");
                return;
            }
            if (me.Current.Ingrained)
            {
                session.QueueMessage("ERROR 뿌리를 박고 있어서 교체할 수 없습니다.");
                return;
            }
            if (me.Current.SkyDropped)
            {
                session.QueueMessage("ERROR 상대의 프리폴에 붙잡혀 있어서 교체할 수 없습니다.");
                return;
            }
            if (battle.FairyLockTurns > 0)
            {
                session.QueueMessage("ERROR 페어리록 때문에 이번 턴에는 교체할 수 없습니다.");
                return;
            }
            if (battle.IsTrapped(me, other))
            {
                session.QueueMessage("ERROR 상대에게 붙잡혀 있어서 교체할 수 없습니다.");
                return;
            }

            me.ChosenSwitch = index;
            if (battle.AllChosen)
                Proceed(room, battle.ResolveTurn());
        }
    }

    // 전투 중 항복. 나가기와 같은 결과로 상대가 이긴다. 양쪽 모두 결과 화면에서 재도전을 신청할 수 있다.
    public void Surrender(ClientSession session)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out Battle battle, out BattleSide me, out BattleSide other) || room.BattleOver)
            {
                session.QueueMessage("ERROR 지금은 항복할 수 없습니다.");
                return;
            }

            room.BattleOver = true;
            battle.Send(me, "BATTLE_END LOSE SURRENDER");
            battle.Send(other, "BATTLE_END WIN SURRENDER");
            Console.WriteLine($"방 {room.Id} 전투 종료 (항복: {session.Nickname})");
        }
    }

    // 결과 화면에서 재도전 신청. 양쪽 모두 신청하면 포켓몬 선택부터 다시 시작한다.
    public void RequestRematch(ClientSession session)
    {
        lock (roomsLock)
        {
            // 포켓몬 선택 시간 초과로 끝났으면 전투(Battle)가 없으므로 방 단위로 확인한다.
            if (session.State is not InGameState || session.RoomId is not int roomId ||
                !rooms.TryGetValue(roomId, out GameRoom? room) || !room.BattleOver)
            {
                session.QueueMessage("ERROR 재도전은 전투가 끝난 뒤에 신청할 수 있습니다.");
                return;
            }
            if (room.Guest == null)
            {
                session.QueueMessage("ERROR 상대방이 나가서 재도전할 수 없습니다.");
                return;
            }
            bool isHost = room.Host.Id == session.Id;
            if (isHost ? room.HostRematch : room.GuestRematch)
            {
                session.QueueMessage("ERROR 이미 재도전을 신청했습니다.");
                return;
            }

            if (isHost) room.HostRematch = true;
            else room.GuestRematch = true;
            if (!room.HostRematch || !room.GuestRematch)
            {
                session.QueueMessage("REMATCH_REQUESTED");
                (isHost ? room.Guest : room.Host).QueueMessage("PLAYER_REMATCH");
                return;
            }

            // 포켓몬 목록은 클라이언트가 이미 가지고 있으므로 다시 보내지 않는다.
            room.ResetBattle();
            room.TeamDeadline = Deadline(TeamTimeLimitSeconds);
            room.Host.QueueMessage("REMATCH_STARTED");
            room.Guest.QueueMessage("REMATCH_STARTED");
            Console.WriteLine($"방 {room.Id} 재도전 시작");
        }
    }

    // 엔진이 입력을 기다릴 때까지 진행시키고, 기다리는 입력을 안내한다.
    private void Proceed(GameRoom room, TurnProgress progress)
    {
        Battle battle = room.Battle!;
        while (true)
        {
            switch (progress)
            {
                case TurnProgress.Over:
                    room.BattleOver = true;
                    BattleOutcome outcome = battle.Outcome!.Value;
                    battle.Send(battle.HostSide, outcome switch
                    {
                        BattleOutcome.HostWins => "BATTLE_END WIN", BattleOutcome.GuestWins => "BATTLE_END LOSE", _ => "BATTLE_END DRAW"
                    });
                    battle.Send(battle.GuestSide, outcome switch
                    {
                        BattleOutcome.GuestWins => "BATTLE_END WIN", BattleOutcome.HostWins => "BATTLE_END LOSE", _ => "BATTLE_END DRAW"
                    });
                    Console.WriteLine($"방 {room.Id} 전투 종료");
                    return;
                case TurnProgress.AwaitingMidTurnSwitch:
                    SendPrompts(battle);
                    room.ActionDeadline = Deadline(actionTimeLimitSeconds);
                    return;
                default:
                    // 새 턴. 난동·2턴 기술 등으로 양쪽 행동이 모두 정해져 있으면 바로 진행한다.
                    battle.BeginTurn();
                    SendPrompts(battle);
                    room.ActionDeadline = Deadline(actionTimeLimitSeconds);   // 클라이언트도 안내를 받은 순간부터 센다.
                    if (!battle.AllChosen)
                        return;
                    progress = battle.ResolveTurn();
                    break;
            }
        }
    }

    // CHOOSE_ACTION: 기술/교체 선택, CHOOSE_SWITCH: 쓰러진 포켓몬 대신 내보낼 포켓몬 선택,
    // CHOOSE_SWITCH_MIDTURN: 유턴·배턴터치 등으로 턴 도중에 교체할 포켓몬 선택,
    // WAIT_SWITCH: 상대가 다음 포켓몬을 고르는 중,
    // LOCKED: 난동 기술·2턴 기술의 공격 턴·반동으로 쉬는 턴이라 행동이 정해져 있어 선택 없이 대기
    private static void SendPrompts(Battle battle)
    {
        foreach (BattleSide side in new[] { battle.HostSide, battle.GuestSide })
        {
            BattleSide other = battle.GetOther(side);
            battle.Send(side, side.SwitchingMidTurn ? "CHOOSE_SWITCH_MIDTURN"
                : other.SwitchingMidTurn ? "WAIT_SWITCH"
                : side.MustSwitch ? "CHOOSE_SWITCH"
                : other.MustSwitch ? "WAIT_SWITCH"
                : side.HasChosen ? "LOCKED" : "CHOOSE_ACTION");
        }
    }

    // roomsLock 안에서 호출한다. 전투가 시작된 방만 찾는다.
    private bool TryGetBattle(ClientSession session, out GameRoom room, out Battle battle, out BattleSide me, out BattleSide other)
    {
        room = null!;
        battle = null!;
        me = other = null!;
        if (session.State is not InGameState || session.RoomId is not int roomId ||
            !rooms.TryGetValue(roomId, out GameRoom? found) || found.Battle is not { } current)
            return false;

        room = found;
        battle = current;
        (me, other) = current.HostSide.Players[0].Session == session
            ? (current.HostSide, current.GuestSide) : (current.GuestSide, current.HostSide);
        return true;
    }
}
