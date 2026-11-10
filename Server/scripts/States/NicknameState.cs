// 접속 직후 상태. NAME <닉네임>으로 닉네임을 정해야 로비로 간다.
sealed class NicknameState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string command)
    {
        if (command.StartsWith("NAME "))
            rooms.SetNickname(session, command[5..]);
        else
            session.QueueMessage("ERROR 닉네임을 먼저 정하세요.");
        return true;
    }
}
