using Pokemon.Protocol;

class GameRoom
{
    public int Id { get; }
    public ClientSession Host { get; }
    public ClientSession? Guest { get; set; }
    public PokemonSelection? HostSelection { get; set; }
    public PokemonSelection? GuestSelection { get; set; }

    public GameRoom(int id, ClientSession host)
    {
        Id = id;
        Host = host;
    }
}
