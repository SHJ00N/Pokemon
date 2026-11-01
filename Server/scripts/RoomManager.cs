using System.Text.Json;
using Pokemon.Protocol;

class RoomManager
{
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

    public void SelectPokemon(ClientSession session, PokemonSelection? selection)
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
            if ((isHost ? room.HostSelection : room.GuestSelection) != null)
            {
                session.QueueMessage("ERROR 이미 선택을 완료했습니다.");
                return;
            }
            if (selection == null || !data.PokemonById.ContainsKey(selection.PokemonId) ||
                selection.MoveIds is not { Length: 4 } ids || ids.Distinct().Count() != 4 ||
                ids.Any(id => !data.LearnableMovesByPokemonId[selection.PokemonId].Contains(id)))
            {
                session.QueueMessage("ERROR 포켓몬과 배울 수 있는 서로 다른 기술 4개를 선택하세요.");
                return;
            }

            if (isHost) room.HostSelection = selection;
            else room.GuestSelection = selection;
            session.QueueMessage("SELECTION_ACCEPTED");

            if (room.HostSelection is PokemonSelection host && room.GuestSelection is PokemonSelection guest)
            {
                BattlePokemon hostPokemon = room.HostPokemon = CreateBattlePokemon(host);
                BattlePokemon guestPokemon = room.GuestPokemon = CreateBattlePokemon(guest);
                PokemonView hostView = GetPokemon(hostPokemon);
                PokemonView guestView = GetPokemon(guestPokemon);
                // BATTLE_READY 이후 클라이언트는 가위바위보 선택 단계로 들어간다.
                room.Host.QueueMessage("BATTLE_READY " + JsonSerializer.Serialize(
                    new BattleView(hostView, guestView, hostPokemon.Moves.Select(GetMove).ToArray())));
                room.Guest.QueueMessage("BATTLE_READY " + JsonSerializer.Serialize(
                    new BattleView(guestView, hostView, guestPokemon.Moves.Select(GetMove).ToArray())));
            }
        }
    }

    // choice: 1 가위, 2 바위, 3 보. 비기면 양쪽 모두 다시 선택한다.
    public void SubmitRps(ClientSession session, int choice)
    {
        lock (roomsLock)
        {
            if (!TryGetBattleRoom(session, out GameRoom room) || room.Turn != null || room.BattleOver)
            {
                session.QueueMessage("ERROR 지금은 가위바위보를 할 수 없습니다.");
                return;
            }

            bool isHost = room.Host.Id == session.Id;
            if ((isHost ? room.HostRps : room.GuestRps) != null)
            {
                session.QueueMessage("ERROR 이미 가위바위보를 선택했습니다.");
                return;
            }
            if (isHost) room.HostRps = choice;
            else room.GuestRps = choice;

            if (room.HostRps is not int hostChoice || room.GuestRps is not int guestChoice)
                return;

            ClientSession guest = room.Guest!;
            if (hostChoice == guestChoice)
            {
                room.HostRps = room.GuestRps = null;
                room.Host.QueueMessage($"RPS_RESULT {hostChoice} {guestChoice} DRAW");
                guest.QueueMessage($"RPS_RESULT {guestChoice} {hostChoice} DRAW");
                return;
            }

            // 바위(2)는 가위(1)를, 보(3)는 바위(2)를, 가위(1)는 보(3)를 이긴다.
            bool hostWins = (hostChoice - guestChoice + 3) % 3 == 1;
            room.Host.QueueMessage($"RPS_RESULT {hostChoice} {guestChoice} {(hostWins ? "WIN" : "LOSE")}");
            guest.QueueMessage($"RPS_RESULT {guestChoice} {hostChoice} {(hostWins ? "LOSE" : "WIN")}");
            SetTurn(room, hostWins ? room.Host : guest);
        }
    }

    // slot: 선택한 기술 4개 중 몇 번째인지 (1~4)
    public void UseMove(ClientSession session, int slot)
    {
        lock (roomsLock)
        {
            if (!TryGetBattleRoom(session, out GameRoom room) || room.Turn == null || room.BattleOver)
            {
                session.QueueMessage("ERROR 지금은 기술을 사용할 수 없습니다.");
                return;
            }
            if (room.Turn.Id != session.Id)
            {
                session.QueueMessage("ERROR 상대방의 차례입니다.");
                return;
            }

            bool isHost = room.Host.Id == session.Id;
            BattlePokemon attacker = isHost ? room.HostPokemon! : room.GuestPokemon!;
            BattlePokemon defender = isHost ? room.GuestPokemon! : room.HostPokemon!;
            ClientSession opponent = isHost ? room.Guest! : room.Host;
            PokemonMoveInfo move = attacker.Moves[slot - 1];

            var (supported, hit, damage, effectiveness) = ApplyMove(attacker, defender, move);
            session.QueueMessage("MOVE_RESULT " + JsonSerializer.Serialize(new MoveResult(
                true, attacker.Info.Name, move.Name, supported, hit, damage, effectiveness, attacker.Hp, defender.Hp)));
            opponent.QueueMessage("MOVE_RESULT " + JsonSerializer.Serialize(new MoveResult(
                false, attacker.Info.Name, move.Name, supported, hit, damage, effectiveness, defender.Hp, attacker.Hp)));

            if (defender.Hp == 0)
            {
                room.BattleOver = true;
                room.Turn = null;
                session.QueueMessage("BATTLE_END WIN");
                opponent.QueueMessage("BATTLE_END LOSE");
                Console.WriteLine($"방 {room.Id} 전투 종료: {session.Address} 승리");
                return;
            }
            SetTurn(room, opponent);
        }
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

    private static void SetTurn(GameRoom room, ClientSession player)
    {
        room.Turn = player;
        ClientSession other = player.Id == room.Host.Id ? room.Guest! : room.Host;
        player.QueueMessage("TURN MY");
        other.QueueMessage("TURN OPPONENT");
    }

    // roomsLock 안에서 호출한다. 양쪽 포켓몬이 준비된 방만 찾는다.
    private bool TryGetBattleRoom(ClientSession session, out GameRoom room)
    {
        room = null!;
        if (session.State is not InGameState || session.RoomId is not int roomId ||
            !rooms.TryGetValue(roomId, out GameRoom? found) || found.Guest == null ||
            found.HostPokemon == null || found.GuestPokemon == null)
            return false;
        room = found;
        return true;
    }

    private BattlePokemon CreateBattlePokemon(PokemonSelection selection) => new(
        data.PokemonById[selection.PokemonId], selection.MoveIds.Select(id => data.MovesById[id]).ToArray());

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
