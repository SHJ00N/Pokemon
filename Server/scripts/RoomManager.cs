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
                PokemonView hostView = GetPokemon(host.PokemonId);
                PokemonView guestView = GetPokemon(guest.PokemonId);
                room.Host.QueueMessage("BATTLE_READY " + JsonSerializer.Serialize(
                    new BattleView(hostView, guestView, host.MoveIds.Select(id => GetMove(data.MovesById[id])).ToArray())));
                room.Guest.QueueMessage("BATTLE_READY " + JsonSerializer.Serialize(
                    new BattleView(guestView, hostView, guest.MoveIds.Select(id => GetMove(data.MovesById[id])).ToArray())));
            }
        }
    }

    private TypeView GetType(int id)
    {
        PokemonTypeInfo type = data.TypesById[id];
        return new TypeView(type.Id, type.Name, type.ColorRgb);
    }

    private TypeView[] GetTypes(PokemonInfo pokemon) => pokemon.Type2Id is int second
        ? [GetType(pokemon.Type1Id), GetType(second)] : [GetType(pokemon.Type1Id)];

    private MoveView GetMove(PokemonMoveInfo move) =>
        new(move.Id, move.Name, GetType(move.TypeId), move.Power, move.Accuracy);

    private PokemonView GetPokemon(int id)
    {
        PokemonInfo pokemon = data.PokemonById[id];
        return new PokemonView(id, pokemon.Name, pokemon.BaseHp, pokemon.BaseHp, GetTypes(pokemon));
    }

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
                    room.HostSelection = room.GuestSelection = null;
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
