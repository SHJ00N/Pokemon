using System.Text.Json;
using Pokemon.Protocol;

// 상태이상 처리는 RoomManager.Status.cs에 나누어 둔다.
partial class RoomManager
{
    private const int TeamSize = 3;
    private const int MaxNicknameLength = 8;

    private readonly GameData data;
    private readonly string catalogMessage;     // POKEMON_CATALOG <Brotli로 압축한 JSON의 Base64>
    public int CatalogJsonBytes { get; }        // 압축 전 JSON 크기
    public int CatalogMessageBytes => catalogMessage.Length + 1;   // 실제로 보내는 크기 (ASCII, 줄바꿈 포함)
    private readonly object roomsLock = new();
    private readonly Dictionary<int, GameRoom> rooms = new();
    private readonly HashSet<string> nicknames = new();     // 접속 중인 플레이어의 닉네임 (중복 금지)
    private int nextRoomId = 1000;

    public RoomManager(GameData data)
    {
        this.data = data;
        // 팀에 넣을 수 있는 폼만 도감번호 순으로 보낸다. 같은 종에 고를 수 있는 폼이 여럿이면 폼 이름을 붙인다.
        var selectable = data.PokemonById.Values.Where(p => p.IsSelectable)
            .OrderBy(p => p.SpeciesId).ThenBy(p => p.Id).ToArray();
        var formCount = selectable.CountBy(p => p.SpeciesId).ToDictionary();
        var pokemon = selectable
            .Select(p => new PokemonCatalogEntry(p.Id, p.SpeciesId, p.Name,
                formCount[p.SpeciesId] > 1 || !p.IsDefault ? p.FormName : null, GetTypes(p),
                data.LearnableMovesByPokemonId[p.Id].Order().ToArray())).ToArray();
        // 기술은 고를 수 있는 폼이 배우는 것만 보낸다 (Z기술·다이맥스기 등은 빠진다).
        var learnable = pokemon.SelectMany(p => p.LearnableMoveIds).ToHashSet();
        var moves = data.MovesById.Values.Where(m => learnable.Contains(m.Id)).OrderBy(m => m.Id).Select(GetMove).ToArray();
        // 게임마다 같은 목록을 보내므로 서버 시작 시 한 번만 압축해 둔다.
        catalogMessage = "POKEMON_CATALOG " + CatalogCodec.Encode(new PokemonCatalogResponse(pokemon, moves), out int jsonBytes);
        CatalogJsonBytes = jsonBytes;
    }

    // 접속 직후 닉네임을 정하고 로비로 보낸다. 닉네임은 띄어쓰기 없이 1~8글자이며 접속 중인 다른 플레이어와 겹치면 안 된다.
    // 메시지를 띄어쓰기로 나누므로 닉네임에 띄어쓰기를 허용하지 않는다.
    public void SetNickname(ClientSession session, string nickname)
    {
        lock (roomsLock)
        {
            if (session.State is not NicknameState)
            {
                session.QueueMessage("ERROR 이미 닉네임을 정했습니다.");
                return;
            }
            if (nickname.Length is 0 or > MaxNicknameLength ||
                nickname.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                session.QueueMessage($"ERROR 닉네임은 띄어쓰기 없이 1~{MaxNicknameLength}글자로 입력하세요.");
                return;
            }
            if (!nicknames.Add(nickname))
            {
                session.QueueMessage("ERROR 이미 사용 중인 닉네임입니다.");
                return;
            }

            session.Nickname = nickname;
            session.State = new LobbyState();
            session.QueueMessage($"NAME_ACCEPTED {nickname}");
            Console.WriteLine($"{session.Address}: 닉네임 {nickname}");
        }
    }

    // 연결이 끊기면 방에서 내보내고 닉네임을 다른 플레이어가 쓸 수 있게 돌려준다.
    public void Disconnect(ClientSession session)
    {
        lock (roomsLock)
        {
            LeaveRoom(session);
            if (session.Nickname.Length > 0)
                nicknames.Remove(session.Nickname);
        }
    }

    // 로비의 방 목록. 게임 중이거나 가득 찬 방도 상태와 함께 보낸다.
    public void SendRoomList(ClientSession session)
    {
        lock (roomsLock)
        {
            RoomSummary[] list = rooms.Values.OrderBy(room => room.Id)
                .Select(room => new RoomSummary(room.Id, room.Host.Nickname, room.Guest == null ? 1 : 2,
                    room.Host.State is not InRoomState))
                .ToArray();
            session.QueueMessage("ROOM_LIST " + JsonSerializer.Serialize(list));
        }
    }

    public void CreateRoom(ClientSession session)
    {
        lock (roomsLock)
        {
            if (session.RoomId != null)
            {
                session.QueueMessage("ERROR 이미 다른 방에 참여 중입니다.");
                return;
            }

            int roomId = nextRoomId++;
            var room = new GameRoom(roomId, session);
            rooms.Add(roomId, room);
            session.RoomId = roomId;
            session.State = new InRoomState();
            session.QueueMessage($"CREATED {roomId} {session.Nickname}");
            BroadcastPlayers(room);
            Console.WriteLine($"방 {roomId} 생성: {session.Nickname} ({session.Address})");
        }
    }

    public void JoinRoom(ClientSession session, int roomId)
    {
        lock (roomsLock)
        {
            if (session.RoomId != null)
            {
                session.QueueMessage("ERROR 이미 다른 방에 참여 중입니다.");
                return;
            }
            if (!rooms.TryGetValue(roomId, out GameRoom? room))
            {
                session.QueueMessage("ERROR 존재하지 않는 방입니다.");
                return;
            }
            if (room.Guest != null)
            {
                session.QueueMessage("ERROR 방이 가득 찼습니다.");
                return;
            }
            if (room.Host.State is not InRoomState)
            {
                session.QueueMessage("ERROR 이미 게임이 시작된 방입니다.");
                return;
            }

            room.Guest = session;
            session.RoomId = roomId;
            session.State = new InRoomState();
            session.QueueMessage($"JOINED {roomId} {room.Host.Nickname}");
            room.Host.QueueMessage("PLAYER_JOINED");
            BroadcastPlayers(room);
            Console.WriteLine($"방 {roomId} 입장: {session.Nickname} ({session.Address})");
        }
    }

    public void SetReady(ClientSession session)
    {
        lock (roomsLock)
        {
            if (session.RoomId is not int roomId || !rooms.TryGetValue(roomId, out GameRoom? room))
            {
                session.QueueMessage("ERROR 방에 참여 중이 아닙니다.");
                return;
            }

            if (session.State is not InRoomState)
            {
                session.QueueMessage("ERROR 현재 상태에서는 준비할 수 없습니다.");
                return;
            }

            bool wasReady = session.IsReady;
            session.IsReady = true;
            session.QueueMessage("READY");
            if (!wasReady)
            {
                ClientSession? opponent = room.Host.Id == session.Id ? room.Guest : room.Host;
                opponent?.QueueMessage("PLAYER_READY");
            }
            BroadcastPlayers(room);

            if (room.Guest is { IsReady: true } guest && room.Host.IsReady &&
                room.Host.State is InRoomState && guest.State is InRoomState)
            {
                room.Host.State = new InGameState();
                guest.State = new InGameState();
                room.Host.QueueMessage("GAME_STARTED");
                guest.QueueMessage("GAME_STARTED");
                room.Host.QueueMessage(catalogMessage);
                guest.QueueMessage(catalogMessage);
                room.TeamDeadline = Deadline(TeamTimeLimitSeconds);
            }
        }
    }

    // 서로 다른 포켓몬 3마리와 각각의 기술 4개. 첫 번째 포켓몬이 먼저 출전한다.
    public void SelectTeam(ClientSession session, PokemonSelection[]? team)
    {
        lock (roomsLock)
        {
            if (session.State is not InGameState || session.RoomId is not int roomId ||
                !rooms.TryGetValue(roomId, out GameRoom? room) || room.Guest == null || room.BattleOver)
            {
                session.QueueMessage("ERROR 포켓몬을 선택할 수 있는 게임이 없습니다.");
                return;
            }

            bool isHost = room.Host.Id == session.Id;
            if ((isHost ? room.HostTeam : room.GuestTeam) != null)
            {
                session.QueueMessage("ERROR 이미 선택을 완료했습니다.");
                return;
            }
            // 같은 종은 폼이 달라도 한 마리만 넣을 수 있다 (예: 라이츄와 알로라 라이츄).
            if (team is not { Length: TeamSize } || team.Any(p => p == null || !IsValidSelection(p)) ||
                team.Select(p => data.PokemonById[p.PokemonId].SpeciesId).Distinct().Count() != TeamSize)
            {
                session.QueueMessage("ERROR 서로 다른 포켓몬 3마리와 각각 배울 수 있는 서로 다른 기술 4개를 선택하세요.");
                return;
            }

            if (isHost) room.HostTeam = team;
            else room.GuestTeam = team;
            session.QueueMessage("SELECTION_ACCEPTED");

            if (room.HostTeam is { } hostTeam && room.GuestTeam is { } guestTeam)
            {
                room.TeamDeadline = null;
                room.HostSide = CreateSide(room.Host, hostTeam);
                room.GuestSide = CreateSide(room.Guest, guestTeam);
                SendBattleState(room);
                StartTurn(room);
            }
        }
    }

    // slot: 출전 중인 포켓몬의 기술 4개 중 몇 번째인지 (1~4)
    public void UseMove(ClientSession session, int slot)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out BattleSide me, out BattleSide other) ||
                room.BattleOver || me.MustSwitch || other.MustSwitch || me.HasChosen)
            {
                session.QueueMessage("ERROR 지금은 행동을 선택할 수 없습니다.");
                return;
            }

            // 사슬묶기·앙코르·트집·도발·회복봉인·봉인으로 쓸 수 없는 기술은 고를 수 없다.
            // 쓸 수 있는 기술이 하나도 없으면 어느 기술을 골라도 발버둥을 쓴다.
            me.Struggling = false;
            if (MoveBlockedBy(me, other, slot) is string cause)
            {
                if (!MustStruggle(me, other))
                {
                    session.QueueMessage($"ERROR {cause} 때문에 {WithJosa(me.Current.Moves[slot - 1].Name, "을", "를")} 쓸 수 없습니다.");
                    return;
                }
                me.Struggling = true;
            }

            me.ChosenMove = slot;
            TryResolveTurn(room);
        }
    }

    // teamSlot: 교체할 팀 번호 (1~3)
    public void SwitchPokemon(ClientSession session, int teamSlot)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out BattleSide me, out BattleSide other) || room.BattleOver)
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
                CompleteMidTurnSwitch(room, me, index);
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
                SendInReplacement(room, me, index);
                if (!EndBattleIfOver(room))
                    StartTurn(room);
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
            if (room.FairyLockTurns > 0)
            {
                session.QueueMessage("ERROR 페어리록 때문에 이번 턴에는 교체할 수 없습니다.");
                return;
            }
            if (IsTrapped(me, other))
            {
                session.QueueMessage("ERROR 상대에게 붙잡혀 있어서 교체할 수 없습니다.");
                return;
            }

            me.ChosenSwitch = index;
            TryResolveTurn(room);
        }
    }

    // 전투 중 항복. 나가기와 같은 결과로 상대가 이긴다. 양쪽 모두 결과 화면에서 재도전을 신청할 수 있다.
    public void Surrender(ClientSession session)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out BattleSide me, out BattleSide other) || room.BattleOver)
            {
                session.QueueMessage("ERROR 지금은 항복할 수 없습니다.");
                return;
            }

            room.BattleOver = true;
            me.Player.QueueMessage("BATTLE_END LOSE SURRENDER");
            other.Player.QueueMessage("BATTLE_END WIN SURRENDER");
            Console.WriteLine($"방 {room.Id} 전투 종료 (항복: {session.Nickname})");
        }
    }

    // 결과 화면에서 재도전 신청. 양쪽 모두 신청하면 포켓몬 선택부터 다시 시작한다.
    public void RequestRematch(ClientSession session)
    {
        lock (roomsLock)
        {
            // 포켓몬 선택 시간 초과로 끝났으면 전투 정보(BattleSide)가 없으므로 방 단위로 확인한다.
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

    // 새 턴을 시작한다. 난동 기술·2턴 기술의 공격 턴·반동으로 쉬는 턴인 포켓몬은 행동을 자동으로 정한다(LOCKED).
    private void StartTurn(GameRoom room)
    {
        BattleSide host = room.HostSide!, guest = room.GuestSide!;
        if (!host.MustSwitch && !guest.MustSwitch)
        {
            foreach (BattleSide side in new[] { host, guest })
            {
                BattlePokemon pokemon = side.Current;
                if ((pokemon.LockedSlot ?? pokemon.ChargingSlot ?? pokemon.RechargeSlot) is int slot)
                    side.ChosenMove = slot;
            }
        }
        SendPrompts(room);
        room.ActionDeadline = Deadline(ActionTimeLimitSeconds);   // 클라이언트도 안내를 받은 순간부터 센다.
        TryResolveTurn(room);   // 양쪽 모두 고정되어 있으면 바로 진행한다.
    }

    // 양쪽이 모두 행동을 고르면 한 턴을 처리한다. 교체 → 공격(우선도, 스피드 순) → 턴 끝 처리 순서다.
    private void TryResolveTurn(GameRoom room)
    {
        BattleSide host = room.HostSide!, guest = room.GuestSide!;
        if (!host.HasChosen || !guest.HasChosen)
            return;

        room.TurnNumber++;
        room.LastMoveIdThisTurn = 0;
        foreach (BattleSide side in new[] { host, guest })
        {
            side.FaintedLastTurn = side.FaintedThisTurn;
            side.FaintedThisTurn = false;
            side.TurnActor = side.Current;
            side.Current.ClearTurnRecord();
        }

        // 따라가때리기: 상대가 교체하려고 하면 교체하기 전에 위력 2배로 공격한다. 쓰러지면 교체하지 않고 턴 끝에 다시 고른다.
        var pursued = new HashSet<BattleSide>();
        foreach (BattleSide side in new[] { host, guest })
        {
            BattleSide pursuer = GetOther(room, side);
            if (side.ChosenSwitch == null || pursuer.ChosenMove is not int slot ||
                pursuer.Current.Moves[slot - 1].Id != PursuitMoveId || pursuer.Current.RechargeSlot != null)
                continue;
            side.Current.SwitchingOut = true;
            ExecuteMove(room, pursuer, side, movesLast: false);
            side.Current.SwitchingOut = false;
            pursued.Add(pursuer);
            SendBattleState(room);
            if (side.Current.Hp == 0)
                side.ChosenSwitch = null;
        }

        foreach (BattleSide side in new[] { host, guest })
        {
            if (side.ChosenSwitch is not int index)
                continue;
            string previous = side.Current.Info.Name;
            ChangeActive(room, side, index);
            string next = side.Current.Info.Name;
            Log(room, side, $"돌아와, {previous}! 가라, {next}!",
                $"상대가 {WithJosa(previous, "을", "를")} 거두고 {WithJosa(next, "을", "를")} 내보냈다!");
            ApplyEntryHazards(room, side);
            SendBattleState(room);
        }

        // 우선도가 높은 기술 → 스피드가 빠른 포켓몬(트릭룸이면 느린 포켓몬) → 같으면 무작위
        List<BattleSide> attackers = new[] { host, guest }
            .Where(side => side.ChosenMove != null && !pursued.Contains(side))
            .OrderByDescending(side => side.Current.Moves[side.ChosenMove!.Value - 1].Priority)
            .ThenByDescending(side => room.TrickRoomTurns > 0 ? -GetSpeed(side) : GetSpeed(side))
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        foreach (BattleSide side in new[] { host, guest })
            side.TurnActor = side.Current;     // 교체해서 나온 포켓몬 (이번 턴의 기록은 교체할 때 지워졌다)
        // 힘껏펀치: 턴이 시작될 때 기합을 모은다. 기술을 쓰기 전에 공격을 맞으면 실패한다.
        foreach (BattleSide side in attackers)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Moves[side.ChosenMove!.Value - 1].Id == FocusPunchMoveId && pokemon.RechargeSlot == null &&
                !pokemon.SkyDropped)
                LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "은", "는")} 기합을 모으기 시작했다!");
        }

        room.TurnQueue.Clear();
        room.TurnQueue.AddRange(attackers);
        ContinueTurn(room);
    }

    // 남은 행동을 순서대로 처리하고 턴을 끝낸다. 유턴·배턴터치 등으로 턴 도중에 교체할 포켓몬을 골라야 하면
    // 그 자리에서 멈추고(CHOOSE_SWITCH_MIDTURN), 고르면 CompleteMidTurnSwitch가 다시 부른다.
    private void ContinueTurn(GameRoom room)
    {
        BattleSide host = room.HostSide!, guest = room.GuestSide!;
        while (room.TurnQueue.Count > 0)
        {
            BattleSide side = room.TurnQueue[0];
            room.TurnQueue.RemoveAt(0);
            // 드래곤테일 등으로 강제 교체되어 나온 포켓몬은 이번 턴에 행동하지 않는다.
            if (!ReferenceEquals(side.Current, side.TurnActor))
                continue;
            ExecuteMove(room, side, GetOther(room, side), movesLast: room.TurnQueue.Count == 0);
            // 각 공격의 HP·상태·능력치 결과를 다음 공격 효과보다 먼저 보낸다.
            SendBattleState(room);
            if (side.SwitchingMidTurn)
            {
                SendPrompts(room);
                room.ActionDeadline = Deadline(ActionTimeLimitSeconds);
                return;
            }
        }

        EndOfTurn(room);

        host.ChosenMove = guest.ChosenMove = null;
        host.ChosenSwitch = guest.ChosenSwitch = null;
        host.SkipTurn = guest.SkipTurn = false;
        host.Struggling = guest.Struggling = false;
        SendBattleState(room);
        if (!EndBattleIfOver(room))
            StartTurn(room);
    }

    // 한쪽(또는 양쪽)의 포켓몬이 모두 쓰러졌으면 전투를 끝낸다. 끝났으면 true
    // 턴 끝 데미지 등으로 양쪽 마지막 포켓몬이 함께 쓰러지면 무승부다.
    private static bool EndBattleIfOver(GameRoom room)
    {
        BattleSide host = room.HostSide!, guest = room.GuestSide!;
        bool hostAlive = host.Team.Any(pokemon => pokemon.Hp > 0);
        bool guestAlive = guest.Team.Any(pokemon => pokemon.Hp > 0);
        if (hostAlive && guestAlive)
            return false;
        room.BattleOver = true;
        host.Player.QueueMessage(hostAlive ? "BATTLE_END WIN" : guestAlive ? "BATTLE_END LOSE" : "BATTLE_END DRAW");
        guest.Player.QueueMessage(guestAlive ? "BATTLE_END WIN" : hostAlive ? "BATTLE_END LOSE" : "BATTLE_END DRAW");
        Console.WriteLine($"방 {room.Id} 전투 종료");
        return true;
    }

    // 출전 포켓몬을 바꾼다. 나가는 포켓몬의 일시 상태와, 그 포켓몬 때문에 걸린 상대의 헤롱헤롱이 풀린다.
    private static void ChangeActive(GameRoom room, BattleSide side, int index)
    {
        side.Current.OnSwitchOut();
        GetOther(room, side).Current.Infatuated = false;
        side.Active = index;
        side.Current.OnSwitchIn();
    }

    // 6세대 데미지 공식 (특성, 아이템은 반영하지 않음). HP는 바꾸지 않는다.
    // hit: 연속기의 몇 번째 타격인지. 명중 판정은 첫 타에서만 하고, 트리플킥은 매번 판정하며 위력이 10·20·30으로 오른다.
    // user: 공격하는 포켓몬 (미래예지처럼 지금 나와 있지 않은 포켓몬의 공격일 때만 준다)
    private (bool Supported, bool Hit, int Damage, double Effectiveness, bool Critical) CalculateDamage(GameRoom room,
        BattleSide attackerSide, BattleSide defenderSide, PokemonMoveInfo move, int hit = 1, BattlePokemon? user = null)
    {
        BattlePokemon attacker = user ?? attackerSide.Current, defender = defenderSide.Current;
        // 변화 기술은 데미지가 없다.
        if (move.Category == "변화")
            return (false, true, 0, 1, false);

        // 정해진 데미지를 주는 기술 (일격필살·지구던지기·카운터 등): 효과가 없는지만 본다.
        // 일격필살은 명중률 30으로 랭크에 영향을 받지 않는다(같은 레벨).
        if (FixedDamage(attacker, defender, move) is int fixedDamage)
        {
            bool ohko = OhkoMoveIds.Contains(move.Id);
            bool lockedOn = ReferenceEquals(attacker.LockOnTarget, defender);
            if (ohko ? !lockedOn && Random.Shared.Next(100) >= 30 : !AccuracyCheck(room, attacker, defender, move))
                return (true, false, 0, 1, false);
            return TypeMultiplier(move, defender, room) == 0
                ? (true, true, 0, 0, false)
                : (true, true, Math.Max(1, fixedDamage), 1, false);
        }

        if (BasePower(room, attackerSide, defenderSide, attacker, move, hit) is not int power)
            return (false, true, 0, 1, false);

        bool tripleKick = move.Id == TripleKickMoveId;
        if ((hit == 1 || tripleKick) &&
            !AccuracyCheck(room, attacker, defender, move, ignoreEvasion: IgnoreStageMoveIds.Contains(move.Id)))
            return (true, false, 0, 1, false);
        if (tripleKick)
            power *= hit;

        // 발버둥은 타입이 없어 상성 1배, 자속 보정도 없다.
        double effectiveness = move.Id == StruggleMoveId ? 1 : TypeMultiplier(move, defender, room);
        // 플라잉프레스: 격투와 비행 상성을 곱한다. 싱크로노이즈: 같은 타입이 없으면 효과가 없다.
        if (move.Id == FlyingPressMoveId)
            effectiveness *= TypeMultiplier(move with { TypeId = TypeIdOf("비행") }, defender, room);
        if (move.Id == SynchronoiseMoveId && !SharesType(attacker, defender))
            effectiveness = 0;
        // 프리폴은 비행 타입에게 데미지를 주지 않는다(6세대).
        if (effectiveness == 0 || (move.Id == SkyDropMoveId && HasType(defender, "비행")))
            return (true, true, 0, 0, false);
        // 공중·땅속·물속에 숨은 상대를 맞히면 위력 2배 (바람일으키기·회오리, 지진·매그니튜드, 파도타기·바다회오리)
        if (DoubleOnHiddenMoveIds.Contains(move.Id) && defender.Hidden is Air or Underground or Underwater)
            power *= 2;

        // 충전: 전기 기술 위력 2배, 한 번 쓰면 끝난다.
        if (attacker.ChargeTurns > 0 && HasTypeName(move.TypeId, "전기"))
        {
            power *= 2;
            attacker.ChargeTurns = 0;
        }
        bool physical = move.Category == "물리";
        bool critical = RollCritical(attacker, defenderSide, data.MoveMetaById.GetValueOrDefault(move.Id)?.CritRate ?? 0);
        // 속임수는 상대의 공격과 공격 랭크로, 사이코쇼크 등은 상대의 방어로 계산한다. 야금야금·성스러운칼은 상대 랭크를 무시한다.
        BattlePokemon attackSource = move.Id == FoulPlayMoveId ? defender : attacker;
        bool targetsDefense = physical || DefenseTargetMoveIds.Contains(move.Id);
        int attackStage = attackSource.GetStage(physical ? Stat.Attack : Stat.SpAttack);
        int defenseStage = IgnoreStageMoveIds.Contains(move.Id) ? 0 : defender.GetStage(targetsDefense ? Stat.Defense : Stat.SpDefense);
        // 급소에 맞으면 공격 쪽의 불리한 랭크와 방어 쪽의 유리한 랭크를 무시한다.
        if (critical)
        {
            attackStage = Math.Max(0, attackStage);
            defenseStage = Math.Min(0, defenseStage);
        }
        int attack = (int)((physical ? attackSource.Attack : attackSource.SpAttack) * StageMultiplier(attackStage));
        // 원더룸: 방어와 특수방어 실제 능력치가 바뀐다 (랭크는 그대로). 모래바람: 바위 타입 특수방어 1.5배
        bool useDefense = targetsDefense != room.WonderRoomTurns > 0;
        double defenseStat = useDefense ? defender.Defense : defender.SpDefense;
        if (!useDefense && room.Weather == Sand && HasType(defender, "바위"))
            defenseStat *= 1.5;
        int defense = Math.Max(1, (int)(defenseStat * StageMultiplier(defenseStage)));

        int damage = (2 * BattlePokemon.Level / 5 + 2) * power * attack / defense / 50 + 2;
        damage = (int)(damage * FieldPowerModifier(room, attacker, defender, move));   // 날씨·필드·흙놀이·물놀이
        if (physical && attacker.Status == Burn && move.Id != FacadeMoveId)
            damage /= 2;                                      // 화상이면 물리 기술 데미지 절반 (객기는 그대로, 6세대)
        if (!critical && (physical ? defenderSide.ReflectTurns : defenderSide.LightScreenTurns) > 0)
            damage /= 2;                                      // 리플렉터·빛의장막 (급소면 무시)
        if (critical)
            damage = damage * 3 / 2;                          // 급소 1.5배 (6세대)
        damage = damage * Random.Shared.Next(85, 101) / 100;  // 난수 0.85~1.00
        if (attacker.Types.Contains(move.TypeId) && move.Id != StruggleMoveId)
            damage = damage * 3 / 2;                          // 자속 보정 1.5배
        damage = Math.Max(1, (int)(damage * effectiveness));  // 타입 상성

        return (true, true, damage, effectiveness, critical);
    }

    // multiHit: 연속기는 급소와 데미지를 타격마다 따로 알리므로 여기서는 상성만 적는다.
    private static string DescribeMove(BattlePokemon attacker, PokemonMoveInfo move,
        (bool Supported, bool Hit, int Damage, double Effectiveness, bool Critical) result, bool multiHit = false)
    {
        string text = $"{attacker.Info.Name}의 {move.Name}!";
        if (!result.Supported) return text + " (아직 구현되지 않은 기술이라 효과가 없었다)";
        if (move.Id == MagnitudeMoveId) text += $" 매그니튜드 {attacker.MagnitudeLevel}!";
        if (!result.Hit) return text + " 공격이 빗나갔다!";
        if (result.Effectiveness == 0) return text + " 효과가 없는 것 같다...";
        if (OhkoMoveIds.Contains(move.Id)) text += " 일격필살!";
        if (result.Critical && !multiHit) text += " 급소에 맞았다!";
        if (result.Effectiveness > 1) text += " 효과가 굉장했다!";
        else if (result.Effectiveness < 1) text += " 효과가 별로인 듯하다...";
        return multiHit ? text : text + $" ({result.Damage} 데미지)";
    }

    // 마지막 글자의 받침 유무에 따라 조사를 붙인다. 예: 마기라스를, 이상해씨를, 파이리가
    private static string WithJosa(string word, string afterFinal, string afterVowel)
    {
        char last = word[^1];
        bool hasFinal = last is >= '가' and <= '힣' && (last - '가') % 28 != 0;
        return word + (hasFinal ? afterFinal : afterVowel);
    }

    // 같은 사건을 행동한 플레이어와 상대에게 각자의 시점으로 보낸다.
    private static void Log(GameRoom room, BattleSide actor, string mine, string theirs)
    {
        actor.Player.QueueMessage("BATTLE_LOG " + mine);
        GetOther(room, actor).Player.QueueMessage("BATTLE_LOG " + theirs);
    }

    private void SendBattleState(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            var team = side.Team.Select(p => new TeamMemberView(GetPokemon(p), p.Moves.Select(GetMove).ToArray()));
            side.Player.QueueMessage("BATTLE_STATE " + JsonSerializer.Serialize(
                new BattleView(team.ToArray(), side.Active, GetPokemon(GetOther(room, side).Current),
                    GetFieldEffects(room))));
        }
    }

    // CHOOSE_ACTION: 기술/교체 선택, CHOOSE_SWITCH: 쓰러진 포켓몬 대신 내보낼 포켓몬 선택,
    // CHOOSE_SWITCH_MIDTURN: 유턴·배턴터치 등으로 턴 도중에 교체할 포켓몬 선택,
    // WAIT_SWITCH: 상대가 다음 포켓몬을 고르는 중,
    // LOCKED: 난동 기술·2턴 기술의 공격 턴·반동으로 쉬는 턴이라 행동이 정해져 있어 선택 없이 대기
    private static void SendPrompts(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            BattleSide other = GetOther(room, side);
            side.Player.QueueMessage(side.SwitchingMidTurn ? "CHOOSE_SWITCH_MIDTURN"
                : other.SwitchingMidTurn ? "WAIT_SWITCH"
                : side.MustSwitch ? "CHOOSE_SWITCH"
                : other.MustSwitch ? "WAIT_SWITCH"
                : side.HasChosen ? "LOCKED" : "CHOOSE_ACTION");
        }
    }

    private static BattleSide GetOther(GameRoom room, BattleSide side) =>
        side == room.HostSide ? room.GuestSide! : room.HostSide!;

    // roomsLock 안에서 호출한다. 양쪽 팀이 준비된 방만 찾는다.
    private bool TryGetBattle(ClientSession session, out GameRoom room, out BattleSide me, out BattleSide other)
    {
        room = null!;
        me = other = null!;
        if (session.State is not InGameState || session.RoomId is not int roomId ||
            !rooms.TryGetValue(roomId, out GameRoom? found) ||
            found.HostSide is not { } host || found.GuestSide is not { } guest)
            return false;

        room = found;
        (me, other) = host.Player.Id == session.Id ? (host, guest) : (guest, host);
        return true;
    }

    private bool IsValidSelection(PokemonSelection selection) =>
        data.PokemonById.TryGetValue(selection.PokemonId, out PokemonInfo? info) && info.IsSelectable &&
        selection.MoveIds is { Length: 4 } ids && ids.Distinct().Count() == 4 &&
        ids.All(id => data.LearnableMovesByPokemonId[selection.PokemonId].Contains(id));

    private BattleSide CreateSide(ClientSession player, PokemonSelection[] team) => new(player, team
        .Select(p => new BattlePokemon(data.PokemonById[p.PokemonId], p.MoveIds.Select(id => data.MovesById[id]).ToArray()))
        .ToArray());

    private TypeView GetType(int id)
    {
        PokemonTypeInfo type = data.TypesById[id];
        return new TypeView(type.Id, type.Name, type.ColorRgb);
    }

    private TypeView[] GetTypes(PokemonInfo pokemon) => pokemon.Type2Id is int second
        ? [GetType(pokemon.Type1Id), GetType(second)] : [GetType(pokemon.Type1Id)];

    private MoveView GetMove(PokemonMoveInfo move) =>
        new(move.Id, move.Name, GetType(move.TypeId), move.Power, move.Accuracy);

    private PokemonView GetPokemon(BattlePokemon pokemon) =>
        new(pokemon.Info.Id, pokemon.Info.SpeciesId, pokemon.Info.Name, pokemon.Hp, pokemon.MaxHp,
            pokemon.Types.Select(GetType).ToArray(),    // 물붓기 등으로 바뀐 지금 타입
            pokemon.Status == null ? null : StatusName(pokemon.Status), DescribeStages(pokemon));

    public void LeaveRoom(ClientSession session, bool notify = false)
    {
        lock (roomsLock)
        {
            if (session.RoomId is not int roomId)
            {
                if (notify)
                    session.QueueMessage("ERROR 방에 참여 중이 아닙니다.");
                return;
            }

            bool wasInGame = session.State is InGameState;
            session.RoomId = null;
            session.IsReady = false;
            session.State = new LobbyState();
            if (!rooms.TryGetValue(roomId, out GameRoom? room))
                return;

            ClientSession? opponent = room.Host.Id == session.Id ? room.Guest : room.Host;
            if (wasInGame && (room.HostSide != null || room.BattleOver) && opponent != null)
                LeaveBattle(room, opponent);
            else if (room.Host.Id == session.Id)
            {
                rooms.Remove(roomId);
                if (room.Guest != null)
                {
                    room.Guest.RoomId = null;
                    room.Guest.IsReady = false;
                    room.Guest.State = new LobbyState();
                    room.Guest.QueueMessage("ROOM_CLOSED");
                }
                Console.WriteLine($"방 {roomId} 종료");
            }
            else if (room.Guest?.Id == session.Id)
            {
                room.Guest = null;
                room.Host.QueueMessage("PLAYER_LEFT");
                if (room.Host.State is InGameState)
                {
                    room.Host.State = new InRoomState();
                    room.Host.IsReady = false;
                    room.ResetBattle();
                    room.Host.QueueMessage($"RETURN_TO_ROOM {room.Id} {room.Host.Nickname}");
                }
                BroadcastPlayers(room);
                Console.WriteLine($"방 {roomId} 참여자 퇴장");
            }

            if (notify)
                session.QueueMessage("LEFT");
        }
    }

    // roomsLock 안에서 호출한다. 전투가 시작됐거나 결과가 난 뒤 한 명이 나가면(연결 종료 포함) 처리한다.
    // 전투 중이었다면 항복으로 처리한다.
    // 남은 플레이어는 방장이 되어 결과 화면에 남고, 나가기를 고르면 방이 닫힌다.
    private static void LeaveBattle(GameRoom room, ClientSession remaining)
    {
        room.Host = remaining;
        room.Guest = null;
        if (!room.BattleOver)
        {
            room.BattleOver = true;
            remaining.QueueMessage("BATTLE_END WIN LEFT");
            Console.WriteLine($"방 {room.Id} 전투 종료 (상대 퇴장)");
        }
        else
            remaining.QueueMessage("PLAYER_LEFT");   // 결과 화면에서 나감. 재도전은 할 수 없다.
        Console.WriteLine($"방 {room.Id} 퇴장, 남은 플레이어 {remaining.Nickname}");
    }

    // roomsLock 안에서 호출해 플레이어 목록과 준비 상태를 함께 전달한다.
    private static void BroadcastPlayers(GameRoom room)
    {
        string message = $"ROOM_PLAYERS {room.Id} {room.Host.Nickname} {(room.Host.IsReady ? 1 : 0)}";
        if (room.Guest is ClientSession guest)
            message += $" {guest.Nickname} {(guest.IsReady ? 1 : 0)}";

        room.Host.QueueMessage(message);
        room.Guest?.QueueMessage(message);
    }
}
