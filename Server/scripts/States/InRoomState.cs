sealed class InRoomState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string text)
    {
        if (!int.TryParse(text, out int command))
        {
            session.QueueMessage("ERROR 숫자 명령을 입력하세요.");
            return true;
        }

        switch (command)
        {
            case 1:
                rooms.SetReady(session);
                break;
            case 2:
                rooms.LeaveRoom(session, notify: true);
                break;
            default:
                session.QueueMessage("ERROR 1: 준비, 2: 나가기 중에서 선택하세요.");
                break;
        }
        return true;
    }
}
