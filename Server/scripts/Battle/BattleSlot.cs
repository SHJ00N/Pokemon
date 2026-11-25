// 한 편의 출전 자리 하나: 지금 나와 있는 포켓몬과 이번 턴 선택, 그 자리에 걸린 효과. 1:1은 편마다 한 자리, 2:2는 두 자리.
sealed class BattleSlot
{
    public BattleSide Side { get; }
    public int Index { get; }                       // 편 안의 자리 번호. 1:1은 0
    public BattlePlayer Owner { get; }              // 이 자리의 포켓몬을 고르는 플레이어
    public int Active { get; set; }                 // 출전 중인 Side.Team 번호 (0부터)
    public BattlePokemon Current => Side.Team[Active];

    // 이번 턴에 고른 행동. 기술 또는 교체 중 하나만 값이 있다.
    public int? ChosenMove { get; set; }            // 기술 슬롯 (1~4)
    public PokemonInfo? ChosenMegaForm { get; set; } // 이번 턴에 기술을 쓰기 전(교체 다음)에 이 폼으로 메가진화
    public int? ChosenSwitch { get; set; }          // 교체할 팀 번호 (0부터)
    public bool HasChosen => ChosenMove != null || ChosenSwitch != null;
    public bool Struggling { get; set; }            // 쓸 수 있는 기술이 없어 이번 턴에 발버둥을 쓴다.
    public BattlePokemon? TurnActor { get; set; }   // 이번 턴에 행동을 고른 포켓몬. 강제 교체로 나온 포켓몬은 행동하지 않는다.

    // 턴 도중 교체: 유턴·볼트체인지·막말내뱉기·배턴터치를 쓴 뒤 다음 포켓몬을 고르는 중 (CHOOSE_SWITCH_MIDTURN)
    public bool SwitchingMidTurn { get; set; }
    public bool BatonPassing { get; set; }          // 배턴터치로 교체 중: 랭크 등을 넘겨준다.

    // 출전 포켓몬이 쓰러져 다음 포켓몬을 골라야 하는 상태
    public bool MustSwitch { get; set; }

    // 이 자리에 걸리는 효과 (본가에서 "그 자리"의 포켓몬에게 적용)
    public int WishTurns { get; set; }              // 희망사항: 0이 되는 턴 끝에 이 자리의 포켓몬이 WishHeal만큼 회복
    public int WishHeal { get; set; }
    public PokemonMoveInfo? HealingWish { get; set; } // 치유소원·초승달춤: 다음에 나오는 포켓몬을 회복시킨다.
    // 미래예지·파멸의소원: 0이 되는 턴 끝에 이 자리의 포켓몬이 FutureAttackUser의 FutureAttackMove를 맞는다.
    public int FutureAttackTurns { get; set; }
    public BattlePokemon? FutureAttackUser { get; set; }
    public PokemonMoveInfo? FutureAttackMove { get; set; }

    public BattleSlot(BattleSide side, int index, BattlePlayer owner, int active)
    {
        Side = side;
        Index = index;
        Owner = owner;
        Active = active;
    }
}
