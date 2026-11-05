using Pokemon.Protocol;

class GameRoom
{
    public int Id { get; }
    // 전투가 시작된 뒤 방장이 나가면 남은 참여자가 방장이 된다.
    public ClientSession Host { get; set; }
    public ClientSession? Guest { get; set; }
    public PokemonSelection[]? HostTeam { get; set; }
    public PokemonSelection[]? GuestTeam { get; set; }

    // 양쪽 팀 선택이 끝나면 생성되는 전투 정보
    public BattleSide? HostSide { get; set; }
    public BattleSide? GuestSide { get; set; }
    public bool BattleOver { get; set; }
    public int TrickRoomTurns { get; set; }  // 트릭룸 남은 턴: 스피드가 느린 쪽이 먼저 행동

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
        TrickRoomTurns = 0;
    }
}
