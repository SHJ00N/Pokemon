class RoomManager
{
    private readonly object roomsLock = new();
    private readonly Dictionary<int, GameRoom> rooms = new();
    private int nextRoomId = 1000;

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
            rooms.Add(roomId, new GameRoom(roomId, session));
            session.RoomId = roomId;
            session.QueueMessage($"CREATED {roomId}");
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

            room.Guest = session;
            session.RoomId = roomId;
            session.QueueMessage($"JOINED {roomId}");
            room.Host.QueueMessage("PLAYER_JOINED");
            Console.WriteLine($"방 {roomId} 입장: {session.Address}");
        }
    }

    public void LeaveRoom(ClientSession session)
    {
        lock (roomsLock)
        {
            if (session.RoomId is not int roomId)
                return;

            session.RoomId = null;
            if (!rooms.TryGetValue(roomId, out GameRoom? room))
                return;

            if (room.Host.Id == session.Id)
            {
                rooms.Remove(roomId);
                if (room.Guest != null)
                {
                    room.Guest.RoomId = null;
                    room.Guest.QueueMessage("ROOM_CLOSED");
                }
                Console.WriteLine($"방 {roomId} 종료");
            }
            else if (room.Guest?.Id == session.Id)
            {
                room.Guest = null;
                room.Host.QueueMessage("PLAYER_LEFT");
                Console.WriteLine($"방 {roomId} 참여자 퇴장");
            }
        }
    }
}
