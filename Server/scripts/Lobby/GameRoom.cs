using Pokemon.Protocol;

class GameRoom
{
    public int Id { get; }
    public Random Random { get; }   // 이 방의 모든 무작위 (시드가 있으면 방마다 고정). 전투(Battle)도 이것을 쓴다.
    // 전투가 시작된 뒤 방장이 나가면 남은 참여자가 방장이 된다.
    public ClientSession Host { get; set; }
    public ClientSession? Guest { get; set; }
    public PokemonSelection[]? HostTeam { get; set; }
    public PokemonSelection[]? GuestTeam { get; set; }

    // 양쪽 팀 선택이 끝나면 생성되는 전투 (상태와 규칙은 Battle/)
    public Battle? Battle { get; set; }
    public bool BattleOver { get; set; }

    // 선택 마감 시각 (Environment.TickCount64 기준 ms). null이면 기다리는 선택이 없다. (RoomManager.Timer.cs)
    public long? TeamDeadline { get; set; }     // 포켓몬·기술 선택 마감
    public long? ActionDeadline { get; set; }   // 이번 턴 행동 선택(기절 후 교체 포함) 마감

    // 결과 화면에서 재도전을 신청했는지. 양쪽 모두 신청하면 포켓몬 선택부터 다시 시작한다.
    public bool HostRematch { get; set; }
    public bool GuestRematch { get; set; }

    public GameRoom(int id, ClientSession host, Random random)
    {
        Id = id;
        Host = host;
        Random = random;
    }

    public void ResetBattle()
    {
        HostTeam = GuestTeam = null;
        Battle = null;
        BattleOver = false;
        TeamDeadline = ActionDeadline = null;
        HostRematch = GuestRematch = false;
    }
}
