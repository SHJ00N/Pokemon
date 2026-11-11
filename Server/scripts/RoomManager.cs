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
    // batonTeamSlot: 배턴터치일 때 내보낼 팀 번호 (1~3). 교체할 포켓몬이 없으면 생략한다.
    public void UseMove(ClientSession session, int slot, int? batonTeamSlot = null)
    {
        lock (roomsLock)
        {
            if (!TryGetBattle(session, out GameRoom room, out BattleSide me, out BattleSide other) ||
                room.BattleOver || me.MustSwitch || other.MustSwitch || me.HasChosen)
            {
                session.QueueMessage("ERROR 지금은 행동을 선택할 수 없습니다.");
                return;
            }

            if (me.Current.Moves[slot - 1].Id == BatonPassMoveId)
            {
                bool hasBench = me.Team.Where((pokemon, index) => index != me.Active && pokemon.Hp > 0).Any();
                if (batonTeamSlot is int teamSlot)
                {
                    int index = teamSlot - 1;
                    if (index == me.Active || me.Team[index].Hp == 0)
                    {
                        session.QueueMessage("ERROR 배턴터치로 내보낼 수 없는 포켓몬입니다.");
                        return;
                    }
                    me.BatonTarget = index;
                }
                else if (hasBench)
                {
                    session.QueueMessage("ERROR 배턴터치로 내보낼 포켓몬을 고르세요.");
                    return;
                }
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

            // 쓰러진 포켓몬 대신 내보내는 교체는 턴을 쓰지 않는다. 상대도 추가로 공격하지 못한다.
            if (me.MustSwitch)
            {
                ChangeActive(room, me, index);
                me.MustSwitch = false;
                Log(room, me, $"가라, {me.Current.Info.Name}!", $"상대가 {WithJosa(me.Current.Info.Name, "을", "를")} 내보냈다!");
                SendBattleState(room);
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

    // 새 턴을 시작한다. 난동 기술로 고정된 포켓몬은 행동을 자동으로 정한다.
    private void StartTurn(GameRoom room)
    {
        BattleSide host = room.HostSide!, guest = room.GuestSide!;
        if (!host.MustSwitch && !guest.MustSwitch)
        {
            foreach (BattleSide side in new[] { host, guest })
                if (side.Current.LockedSlot is int slot)
                    side.ChosenMove = slot;
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

        foreach (BattleSide side in new[] { host, guest })
        {
            if (side.ChosenSwitch is not int index)
                continue;
            string previous = side.Current.Info.Name;
            ChangeActive(room, side, index);
            string next = side.Current.Info.Name;
            Log(room, side, $"돌아와, {previous}! 가라, {next}!",
                $"상대가 {WithJosa(previous, "을", "를")} 거두고 {WithJosa(next, "을", "를")} 내보냈다!");
            SendBattleState(room);
        }

        // 우선도가 높은 기술 → 스피드가 빠른 포켓몬(트릭룸이면 느린 포켓몬) → 같으면 무작위
        List<BattleSide> attackers = new[] { host, guest }
            .Where(side => side.ChosenMove != null)
            .OrderByDescending(side => side.Current.Moves[side.ChosenMove!.Value - 1].Priority)
            .ThenByDescending(side => room.TrickRoomTurns > 0 ? -GetSpeed(side) : GetSpeed(side))
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        for (int index = 0; index < attackers.Count; index++)
        {
            ExecuteMove(room, attackers[index], GetOther(room, attackers[index]), movesLast: index == attackers.Count - 1);
            // 각 공격의 HP·상태·능력치 결과를 다음 공격 효과보다 먼저 보낸다.
            SendBattleState(room);
        }

        EndOfTurn(room);

        host.ChosenMove = guest.ChosenMove = null;
        host.ChosenSwitch = guest.ChosenSwitch = null;
        host.SkipTurn = guest.SkipTurn = false;
        host.BatonTarget = guest.BatonTarget = null;
        SendBattleState(room);

        bool hostAlive = host.Team.Any(pokemon => pokemon.Hp > 0);
        bool guestAlive = guest.Team.Any(pokemon => pokemon.Hp > 0);
        if (!hostAlive || !guestAlive)
        {
            // 턴 끝 데미지 등으로 양쪽 마지막 포켓몬이 함께 쓰러지면 무승부다.
            room.BattleOver = true;
            host.Player.QueueMessage(hostAlive ? "BATTLE_END WIN" : guestAlive ? "BATTLE_END LOSE" : "BATTLE_END DRAW");
            guest.Player.QueueMessage(guestAlive ? "BATTLE_END WIN" : hostAlive ? "BATTLE_END LOSE" : "BATTLE_END DRAW");
            Console.WriteLine($"방 {room.Id} 전투 종료");
            return;
        }
        StartTurn(room);
    }

    // 출전 포켓몬을 바꾼다. 나가는 포켓몬의 일시 상태와, 그 포켓몬 때문에 걸린 상대의 헤롱헤롱이 풀린다.
    private static void ChangeActive(GameRoom room, BattleSide side, int index)
    {
        side.Current.OnSwitchOut();
        GetOther(room, side).Current.Infatuated = false;
        side.Active = index;
        side.Current.OnSwitchIn();
    }

    // 5세대 데미지 공식 (날씨, 특성, 아이템은 아직 반영하지 않음). HP는 바꾸지 않는다.
    private (bool Supported, bool Hit, int Damage, double Effectiveness, bool Critical) CalculateDamage(
        BattleSide attackerSide, BattleSide defenderSide, PokemonMoveInfo move)
    {
        BattlePokemon attacker = attackerSide.Current, defender = defenderSide.Current;
        // 변화 기술과 위력 값이 없는 기술은 효과가 아직 구현되지 않았다.
        if (move.Category == "변화" || move.Power is not int power)
            return (false, true, 0, 1, false);

        if (!AccuracyCheck(attacker, defender, move))
            return (true, false, 0, 1, false);

        double effectiveness = TypeMultiplier(move, defender);
        if (effectiveness == 0)
            return (true, true, 0, 0, false);

        // 충전: 전기 기술 위력 2배, 한 번 쓰면 끝난다.
        if (attacker.ChargeTurns > 0 && HasTypeName(move.TypeId, "전기"))
        {
            power *= 2;
            attacker.ChargeTurns = 0;
        }

        bool physical = move.Category == "물리";
        bool critical = RollCritical(attacker, defenderSide);
        int attackStage = attacker.GetStage(physical ? Stat.Attack : Stat.SpAttack);
        int defenseStage = defender.GetStage(physical ? Stat.Defense : Stat.SpDefense);
        // 급소에 맞으면 공격 쪽의 불리한 랭크와 방어 쪽의 유리한 랭크를 무시한다.
        if (critical)
        {
            attackStage = Math.Max(0, attackStage);
            defenseStage = Math.Min(0, defenseStage);
        }
        int attack = (int)((physical ? attacker.Attack : attacker.SpAttack) * StageMultiplier(attackStage));
        int defense = Math.Max(1, (int)((physical ? defender.Defense : defender.SpDefense) * StageMultiplier(defenseStage)));

        int damage = (2 * BattlePokemon.Level / 5 + 2) * power * attack / defense / 50 + 2;
        if (physical && attacker.Status == Burn)
            damage /= 2;                                      // 화상이면 물리 기술 데미지 절반
        if (!critical && (physical ? defenderSide.ReflectTurns : defenderSide.LightScreenTurns) > 0)
            damage /= 2;                                      // 리플렉터·빛의장막 (급소면 무시)
        if (critical)
            damage *= 2;                                      // 급소 2배
        damage = damage * Random.Shared.Next(85, 101) / 100;  // 난수 0.85~1.00
        if (move.TypeId == attacker.Info.Type1Id || move.TypeId == attacker.Info.Type2Id)
            damage = damage * 3 / 2;                          // 자속 보정 1.5배
        damage = Math.Max(1, (int)(damage * effectiveness));  // 타입 상성

        return (true, true, damage, effectiveness, critical);
    }

    private static string DescribeMove(BattlePokemon attacker, PokemonMoveInfo move,
        (bool Supported, bool Hit, int Damage, double Effectiveness, bool Critical) result)
    {
        string text = $"{attacker.Info.Name}의 {move.Name}!";
        if (!result.Supported) return text + " (아직 구현되지 않은 기술이라 효과가 없었다)";
        if (!result.Hit) return text + " 공격이 빗나갔다!";
        if (result.Effectiveness == 0) return text + " 효과가 없는 것 같다...";
        if (result.Critical) text += " 급소에 맞았다!";
        if (result.Effectiveness > 1) text += " 효과가 굉장했다!";
        else if (result.Effectiveness < 1) text += " 효과가 별로인 듯하다...";
        return text + $" ({result.Damage} 데미지)";
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
                new BattleView(team.ToArray(), side.Active, GetPokemon(GetOther(room, side).Current))));
        }
    }

    // CHOOSE_ACTION: 기술/교체 선택, CHOOSE_SWITCH: 쓰러진 포켓몬 대신 내보낼 포켓몬 선택,
    // WAIT_SWITCH: 상대가 다음 포켓몬을 고르는 중, LOCKED: 난동 기술로 행동이 정해져 있어 선택 없이 대기
    private static void SendPrompts(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            BattleSide other = GetOther(room, side);
            side.Player.QueueMessage(side.MustSwitch ? "CHOOSE_SWITCH"
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
        new(pokemon.Info.Id, pokemon.Info.SpeciesId, pokemon.Info.Name, pokemon.Hp, pokemon.MaxHp, GetTypes(pokemon.Info),
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
