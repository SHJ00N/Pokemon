using Pokemon.Protocol;

class GameRoom
{
    public int Id { get; }
    public ClientSession Host { get; }
    public ClientSession? Guest { get; set; }
    public PokemonSelection[]? HostTeam { get; set; }
    public PokemonSelection[]? GuestTeam { get; set; }

    // 양쪽 팀 선택이 끝나면 생성되는 전투 정보
    public BattleSide? HostSide { get; set; }
    public BattleSide? GuestSide { get; set; }
    public bool BattleOver { get; set; }

    public GameRoom(int id, ClientSession host)
    {
        Id = id;
        Host = host;
    }

    public void ResetBattle()
    {
        HostTeam = GuestTeam = null;
        HostSide = GuestSide = null;
        BattleOver = false;
    }
}
