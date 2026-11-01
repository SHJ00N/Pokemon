using System.Text.Json;
using Pokemon.Protocol;

class RoomManager
{
    private const int TeamSize = 3;

    private readonly GameData data;
    private readonly string catalogMessage;
    private readonly object roomsLock = new();
    private readonly Dictionary<int, GameRoom> rooms = new();
    private int nextRoomId = 1000;

    public RoomManager(GameData data)
    {
        this.data = data;
        var pokemon = data.PokemonById.Values.OrderBy(p => p.Id)
            .Select(p => new PokemonCatalogEntry(p.Id, p.Name, GetTypes(p),
                data.LearnableMovesByPokemonId[p.Id].Order().ToArray())).ToArray();
        var moves = data.MovesById.Values.OrderBy(m => m.Id).Select(GetMove).ToArray();
        catalogMessage = "POKEMON_CATALOG " + JsonSerializer.Serialize(new PokemonCatalogResponse(pokemon, moves));
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
            session.QueueMessage($"CREATED {roomId} {session.Address}");
            BroadcastPlayers(room);
            Console.WriteLine($"방 {roomId} 생성: {session.Address}");
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
            session.QueueMessage($"JOINED {roomId} {room.Host.Address}");
            room.Host.QueueMessage("PLAYER_JOINED");
            BroadcastPlayers(room);
            Console.WriteLine($"방 {roomId} 입장: {session.Address}");
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
            }
        }
    }

    // 서로 다른 포켓몬 3마리와 각각의 기술 4개. 첫 번째 포켓몬이 먼저 출전한다.
    public void SelectTeam(ClientSession session, PokemonSelection[]? team)
    {
        lock (roomsLock)
        {
            if (session.State is not InGameState || session.RoomId is not int roomId ||
                !rooms.TryGetValue(roomId, out GameRoom? room) || room.Guest == null)
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
            if (team is not { Length: TeamSize } || team.Any(p => p == null || !IsValidSelection(p)) ||
                team.Select(p => p.PokemonId).Distinct().Count() != TeamSize)
            {
                session.QueueMessage("ERROR 서로 다른 포켓몬 3마리와 각각 배울 수 있는 서로 다른 기술 4개를 선택하세요.");
                return;
            }

            if (isHost) room.HostTeam = team;
            else room.GuestTeam = team;
            session.QueueMessage("SELECTION_ACCEPTED");

            if (room.HostTeam is { } hostTeam && room.GuestTeam is { } guestTeam)
            {
                room.HostSide = CreateSide(room.Host, hostTeam);
                room.GuestSide = CreateSide(room.Guest, guestTeam);
                SendBattleState(room);
                SendPrompts(room);
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
                me.Active = index;
                me.MustSwitch = false;
                Log(room, me, $"가라, {me.Current.Info.Name}!", $"상대가 {WithJosa(me.Current.Info.Name, "을", "를")} 내보냈다!");
                SendBattleState(room);
                SendPrompts(room);
                return;
            }

            if (other.MustSwitch || me.HasChosen)
            {
                session.QueueMessage("ERROR 지금은 교체할 수 없습니다.");
                return;
            }

            me.ChosenSwitch = index;
            TryResolveTurn(room);
        }
    }

    // 양쪽이 모두 행동을 고르면 한 턴을 처리한다. 교체 → 공격(우선도, 스피드 순) 순서다.
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
            side.Active = index;
            string next = side.Current.Info.Name;
            Log(room, side, $"돌아와, {previous}! 가라, {next}!",
                $"상대가 {WithJosa(previous, "을", "를")} 거두고 {WithJosa(next, "을", "를")} 내보냈다!");
        }

        // 우선도가 높은 기술 → 스피드가 빠른 포켓몬 → 같으면 무작위
        List<BattleSide> attackers = new[] { host, guest }
            .Where(side => side.ChosenMove != null)
            .OrderByDescending(side => side.Current.Moves[side.ChosenMove!.Value - 1].Priority)
            .ThenByDescending(side => side.Current.Speed)
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        foreach (BattleSide attacker in attackers)
        {
            // 이번 턴에 먼저 쓰러진 포켓몬은 공격하지 못한다.
            if (attacker.Current.Hp == 0)
                continue;

            BattleSide defender = attacker == host ? guest : host;
            PokemonMoveInfo move = attacker.Current.Moves[attacker.ChosenMove!.Value - 1];
            string result = DescribeMove(attacker.Current, move, ApplyMove(attacker.Current, defender.Current, move));
            Log(room, attacker, result, "상대 " + result);

            if (defender.Current.Hp == 0)
            {
                string name = WithJosa(defender.Current.Info.Name, "이", "가");
                Log(room, defender, $"{name} 쓰러졌다!", $"상대 {name} 쓰러졌다!");
                if (defender.Team.All(pokemon => pokemon.Hp == 0))
                    room.BattleOver = true;
                else
                    defender.MustSwitch = true;
            }
        }

        host.ChosenMove = guest.ChosenMove = null;
        host.ChosenSwitch = guest.ChosenSwitch = null;
        SendBattleState(room);

        if (room.BattleOver)
        {
            bool hostWins = host.Team.Any(pokemon => pokemon.Hp > 0);
            host.Player.QueueMessage(hostWins ? "BATTLE_END WIN" : "BATTLE_END LOSE");
            guest.Player.QueueMessage(hostWins ? "BATTLE_END LOSE" : "BATTLE_END WIN");
            Console.WriteLine($"방 {room.Id} 전투 종료: {(hostWins ? host : guest).Player.Address} 승리");
            return;
        }
        SendPrompts(room);
    }

    // 5세대 데미지 공식 (급소, 날씨, 특성, 아이템, 랭크 변화는 아직 반영하지 않음)
    private (bool Supported, bool Hit, int Damage, double Effectiveness) ApplyMove(
        BattlePokemon attacker, BattlePokemon defender, PokemonMoveInfo move)
    {
        // 변화 기술과 위력 값이 없는 기술은 효과가 아직 구현되지 않았다.
        if (move.Category == "변화" || move.Power is not int power)
            return (false, true, 0, 1);

        // 명중률이 없는 기술은 반드시 맞는다.
        if (move.Accuracy is int accuracy && Random.Shared.Next(100) >= accuracy)
            return (true, false, 0, 1);

        double effectiveness = data.GetMultiplier(move.TypeId, defender.Info.Type1Id);
        if (defender.Info.Type2Id is int type2)
            effectiveness *= data.GetMultiplier(move.TypeId, type2);
        if (effectiveness == 0)
            return (true, true, 0, 0);

        bool physical = move.Category == "물리";
        int attack = physical ? attacker.Attack : attacker.SpAttack;
        int defense = physical ? defender.Defense : defender.SpDefense;

        int damage = (2 * BattlePokemon.Level / 5 + 2) * power * attack / defense / 50 + 2;
        damage = damage * Random.Shared.Next(85, 101) / 100;  // 난수 0.85~1.00
        if (move.TypeId == attacker.Info.Type1Id || move.TypeId == attacker.Info.Type2Id)
            damage = damage * 3 / 2;                          // 자속 보정 1.5배
        damage = Math.Max(1, (int)(damage * effectiveness));  // 타입 상성

        defender.Hp = Math.Max(0, defender.Hp - damage);
        return (true, true, damage, effectiveness);
    }

    private static string DescribeMove(BattlePokemon attacker, PokemonMoveInfo move,
        (bool Supported, bool Hit, int Damage, double Effectiveness) result)
    {
        string text = $"{attacker.Info.Name}의 {move.Name}!";
        if (!result.Supported) return text + " (아직 구현되지 않은 기술이라 효과가 없었다)";
        if (!result.Hit) return text + " 공격이 빗나갔다!";
        if (result.Effectiveness == 0) return text + " 효과가 없는 것 같다...";
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
    // WAIT_SWITCH: 상대가 다음 포켓몬을 고르는 중
    private static void SendPrompts(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            BattleSide other = GetOther(room, side);
            side.Player.QueueMessage(side.MustSwitch ? "CHOOSE_SWITCH"
                : other.MustSwitch ? "WAIT_SWITCH" : "CHOOSE_ACTION");
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
        data.PokemonById.ContainsKey(selection.PokemonId) &&
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
        new(pokemon.Info.Id, pokemon.Info.Name, pokemon.Hp, pokemon.MaxHp, GetTypes(pokemon.Info));

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

            session.RoomId = null;
            session.IsReady = false;
            session.State = new LobbyState();
            if (!rooms.TryGetValue(roomId, out GameRoom? room))
                return;

            if (room.Host.Id == session.Id)
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
                    room.Host.QueueMessage($"RETURN_TO_ROOM {room.Id} {room.Host.Address}");
                }
                BroadcastPlayers(room);
                Console.WriteLine($"방 {roomId} 참여자 퇴장");
            }

            if (notify)
                session.QueueMessage("LEFT");
        }
    }

    // roomsLock 안에서 호출해 플레이어 목록과 준비 상태를 함께 전달한다.
    private static void BroadcastPlayers(GameRoom room)
    {
        string message = $"ROOM_PLAYERS {room.Id} {room.Host.Address} {(room.Host.IsReady ? 1 : 0)}";
        if (room.Guest is ClientSession guest)
            message += $" {guest.Address} {(guest.IsReady ? 1 : 0)}";

        room.Host.QueueMessage(message);
        room.Guest?.QueueMessage(message);
    }
}
