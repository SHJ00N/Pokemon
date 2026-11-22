namespace Client;

// 렌더러에 넘길 시점의 값만 담는다. 다음 Update가 실행돼도 이미 예약한 출력은 바뀌지 않는다.
readonly record struct BattleAnimationFrame(
    int PlayerOffset, int OpponentOffset, bool FlashPlayer, bool FlashOpponent,
    BattleAnimation.Frame? Attack,
    double PlayerFaintProgress, double OpponentFaintProgress,
    StatusAnimation.Effect? StatusEffect, HealingAnimation.Frame? Healing, SummonAnimation.Frame? Summon,
    WithdrawAnimation.Frame? Withdraw = null, MegaEvolutionAnimation.Frame? MegaEvolution = null);
