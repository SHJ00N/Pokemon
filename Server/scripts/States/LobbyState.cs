sealed class LobbyState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string text)
    {
        if (!int.TryParse(text, out int command))
        {
            session.QueueMessage("ERROR 숫자 명령을 입력하세요.");
            return true;
        }

        if (command == 1)
            rooms.CreateRoom(session);
        else if (command == 3)
            return false;
        else if (command >= 1000)
            rooms.JoinRoom(session, command);
        else
            session.QueueMessage("ERROR 잘못된 명령입니다.");

        return true;
    }
}
