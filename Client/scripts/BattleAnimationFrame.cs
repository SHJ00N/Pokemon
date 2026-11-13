using Pokemon.Protocol;

namespace Client;

// 렌더러에 넘길 시점의 값만 담는다. 다음 Update가 실행돼도 이미 예약한 출력은 바뀌지 않는다.
readonly record struct BattleAnimationFrame(
    int PlayerOffset, int OpponentOffset, bool FlashPlayer, bool FlashOpponent,
    TypeView? AttackType, bool AttackFromPlayer, double AttackProgress,
    double PlayerFaintProgress, double OpponentFaintProgress,
    StatusAnimation.Effect? StatusEffect, bool? HealingOnPlayer, SummonAnimation.Frame? Summon,
    WithdrawAnimation.Frame? Withdraw = null);
