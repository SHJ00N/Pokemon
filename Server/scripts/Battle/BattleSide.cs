// 전투의 한 편: 그 편의 플레이어들, 팀, 출전 자리들, 편 전체에 걸린 효과
sealed class BattleSide
{
    public BattlePlayer[] Players { get; }          // 1:1은 한 명
    public BattlePokemon[] Team { get; }
    public BattleSlot[] Slots { get; }              // 플레이어마다 한 자리. 처음에는 그 플레이어가 맡은 첫 포켓몬이 나온다.

    public bool FaintedThisTurn { get; set; }       // 이번 턴에 우리 편 포켓몬이 쓰러졌는지
    public bool FaintedLastTurn { get; set; }       // 지난 턴에 쓰러졌는지 (원수갚기)

    // 우리 편 전체에 걸리는 효과의 남은 턴. 0이면 없음. 교체해도 유지된다.
    public int ReflectTurns { get; set; }           // 리플렉터: 물리 데미지 절반
    public int LightScreenTurns { get; set; }       // 빛의장막: 특수 데미지 절반
    public int TailwindTurns { get; set; }          // 순풍: 스피드 2배
    public int MistTurns { get; set; }              // 흰안개: 상대가 능력치를 내리지 못함
    public int LuckyChantTurns { get; set; }        // 주술: 급소에 맞지 않음
    public int SafeguardTurns { get; set; }         // 신비의부적: 상대 기술로 상태이상·혼란에 걸리지 않음
    public bool QuickGuard { get; set; }            // 패스트가드: 이번 턴에 우선도가 높은 기술을 막음
    public bool WideGuard { get; set; }             // 와이드가드: 이번 턴에 여러 대상을 노리는 공격을 막음
    public bool MatBlock { get; set; }              // 마룻바닥세워막기: 이번 턴에 공격 기술을 막음
    public bool CraftyShield { get; set; }          // 트릭가드: 이번 턴에 상대의 변화 기술을 막음
    // 설치기: 이 편에 교체해서 나오는 포켓몬에게 효과가 있다. 고속스핀·안개제거로 없앤다.
    public int Spikes { get; set; }                 // 압정뿌리기 (최대 3층)
    public int ToxicSpikes { get; set; }            // 독압정 (최대 2층)
    public bool StealthRock { get; set; }           // 스텔스록
    public bool StickyWeb { get; set; }             // 끈적끈적네트

    public BattleSide(BattlePlayer[] players, BattlePokemon[] team)
    {
        Players = players;
        Team = team;
        Slots = players.Select((player, index) => new BattleSlot(this, index, player, player.TeamIndices[0])).ToArray();
    }
}
