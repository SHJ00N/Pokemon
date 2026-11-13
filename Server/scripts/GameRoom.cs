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
    // 날씨·필드 (RoomManager.Field.cs). 남은 턴이 0이면 없다.
    public string? Weather { get; set; }        // rain, sun, sand, hail
    public int WeatherTurns { get; set; }
    public string? Terrain { get; set; }        // grassy, misty, electric
    public int TerrainTurns { get; set; }
    public int GravityTurns { get; set; }       // 중력
    public int WonderRoomTurns { get; set; }    // 원더룸: 방어와 특수방어가 바뀐다.
    public int MudSportTurns { get; set; }      // 흙놀이: 전기 기술 위력 1/3
    public int WaterSportTurns { get; set; }    // 물놀이: 불꽃 기술 위력 1/3
    public int FairyLockTurns { get; set; }     // 페어리록: 교체할 수 없다.
    public bool IonDeluge { get; set; }         // 플라스마샤워: 이번 턴에 노말 기술이 전기 타입이 된다.
    // 이번 턴에 아직 기술을 쓰지 않은 쪽 (행동 순서). 턴 도중 교체를 기다리는 동안 남아 있다가 이어서 처리한다.
    public List<BattleSide> TurnQueue { get; } = new();
    public int TurnNumber { get; set; }         // 전투 시작부터 센 턴 번호
    public int LastMoveIdThisTurn { get; set; } // 이번 턴에 마지막으로 성공한 기술 (크로스플레임·크로스썬더)
    public PokemonMoveInfo? LastExecutedMove { get; set; }  // 전투에서 마지막으로 나온 기술 (흉내쟁이)
    public int EchoedVoiceTurn { get; set; }    // 에코보이스를 마지막으로 쓴 턴과 이어서 쓴 횟수 (1~5)
    public int EchoedVoiceCount { get; set; }

    // 선택 마감 시각 (Environment.TickCount64 기준 ms). null이면 기다리는 선택이 없다. (RoomManager.Timer.cs)
    public long? TeamDeadline { get; set; }     // 포켓몬·기술 선택 마감
    public long? ActionDeadline { get; set; }   // 이번 턴 행동 선택(기절 후 교체 포함) 마감

    // 결과 화면에서 재도전을 신청했는지. 양쪽 모두 신청하면 포켓몬 선택부터 다시 시작한다.
    public bool HostRematch { get; set; }
    public bool GuestRematch { get; set; }

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
        Weather = Terrain = null;
        WeatherTurns = TerrainTurns = GravityTurns = WonderRoomTurns = MudSportTurns = WaterSportTurns = FairyLockTurns = 0;
        IonDeluge = false;
        TurnQueue.Clear();
        TurnNumber = LastMoveIdThisTurn = EchoedVoiceTurn = EchoedVoiceCount = 0;
        LastExecutedMove = null;
        TeamDeadline = ActionDeadline = null;
        HostRematch = GuestRematch = false;
    }
}
