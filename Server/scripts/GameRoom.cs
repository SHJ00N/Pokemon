using Pokemon.Protocol;

class GameRoom
{
    public int Id { get; }
    public ClientSession Host { get; }
    public ClientSession? Guest { get; set; }
    public PokemonSelection? HostSelection { get; set; }
    public PokemonSelection? GuestSelection { get; set; }

    // 양쪽 선택이 끝나면 생성되는 전투용 포켓몬
    public BattlePokemon? HostPokemon { get; set; }
    public BattlePokemon? GuestPokemon { get; set; }
    // 가위바위보 선택 (1: 가위, 2: 바위, 3: 보)
    public int? HostRps { get; set; }
    public int? GuestRps { get; set; }
    // 공격할 차례인 플레이어. 가위바위보가 끝나기 전과 전투가 끝난 뒤에는 null
    public ClientSession? Turn { get; set; }
    public bool BattleOver { get; set; }

    public GameRoom(int id, ClientSession host)
    {
        Id = id;
        Host = host;
    }

    public void ResetBattle()
    {
        HostSelection = GuestSelection = null;
        HostPokemon = GuestPokemon = null;
        HostRps = GuestRps = null;
        Turn = null;
        BattleOver = false;
    }
}
