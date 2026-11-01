// 전투에 참여한 플레이어 한 명의 팀과 이번 턴 선택
sealed class BattleSide
{
    public ClientSession Player { get; }
    public BattlePokemon[] Team { get; }
    public int Active { get; set; }                 // 현재 출전 중인 팀 번호 (0부터)
    public BattlePokemon Current => Team[Active];

    // 이번 턴에 고른 행동. 둘 중 하나만 값이 있다.
    public int? ChosenMove { get; set; }            // 기술 슬롯 (1~4)
    public int? ChosenSwitch { get; set; }          // 교체할 팀 번호 (0부터)
    public bool HasChosen => ChosenMove != null || ChosenSwitch != null;

    // 출전 포켓몬이 쓰러져 다음 포켓몬을 골라야 하는 상태
    public bool MustSwitch { get; set; }

    public BattleSide(ClientSession player, BattlePokemon[] team)
    {
        Player = player;
        Team = team;
    }
}
