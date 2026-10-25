abstract class ClientState
{
    public abstract bool HandleCommand(ClientSession session, RoomManager rooms, string command);
}
