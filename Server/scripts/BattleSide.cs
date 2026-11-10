// 전투에 참여한 플레이어 한 명의 팀과 이번 턴 선택
sealed class BattleSide
{
    public ClientSession Player { get; }
    public BattlePokemon[] Team { get; }
    public int Active { get; set; }                 // 현재 출전 중인 팀 번호 (0부터)
    public BattlePokemon Current => Team[Active];

    // 이번 턴에 고른 행동. 셋 중 하나만 값이 있다.
    public int? ChosenMove { get; set; }            // 기술 슬롯 (1~4)
    public int? ChosenSwitch { get; set; }          // 교체할 팀 번호 (0부터)
    public bool SkipTurn { get; set; }              // 제한시간 안에 고르지 않아 이번 턴을 쉰다.
    public int? BatonTarget { get; set; }           // 배턴터치로 내보낼 팀 번호 (0부터)
    public bool HasChosen => ChosenMove != null || ChosenSwitch != null || SkipTurn;

    // 행동 선택 제한시간을 넘긴 횟수 (이 전투 전체). RoomManager.MaxTimeouts번이 되면 진다.
    public int Timeouts { get; set; }

    // 출전 포켓몬이 쓰러져 다음 포켓몬을 골라야 하는 상태
    public bool MustSwitch { get; set; }

    // 우리 편 전체에 걸리는 효과의 남은 턴. 0이면 없음. 교체해도 유지된다.
    public int ReflectTurns { get; set; }           // 리플렉터: 물리 데미지 절반
    public int LightScreenTurns { get; set; }       // 빛의장막: 특수 데미지 절반
    public int TailwindTurns { get; set; }          // 순풍: 스피드 2배
    public int MistTurns { get; set; }              // 흰안개: 상대가 능력치를 내리지 못함
    public int LuckyChantTurns { get; set; }        // 주술: 급소에 맞지 않음
    public int SafeguardTurns { get; set; }         // 신비의부적: 상대 기술로 상태이상·혼란에 걸리지 않음
    public bool QuickGuard { get; set; }            // 패스트가드: 이번 턴에 우선도가 높은 기술을 막음
    public int WishTurns { get; set; }              // 희망사항: 0이 되는 턴 끝에 이 자리의 포켓몬이 WishHeal만큼 회복
    public int WishHeal { get; set; }

    public BattleSide(ClientSession player, BattlePokemon[] team)
    {
        Player = player;
        Team = team;
    }
}
