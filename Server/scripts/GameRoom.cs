class GameRoom
{
    public int Id { get; }
    public ClientSession Host { get; }
    public ClientSession? Guest { get; set; }

    public GameRoom(int id, ClientSession host)
    {
        Id = id;
        Host = host;
    }
}
