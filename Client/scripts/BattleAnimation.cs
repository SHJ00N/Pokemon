using Pokemon.Protocol;

namespace Client;

// 서버가 계산한 공격 순서대로 이동 → 피격 3회 깜빡임을 끝낸 뒤 다음 공격을 시작한다.
sealed class BattleAnimation
{
    public const int TravelMilliseconds = 500;
    public const int FlashIntervalMilliseconds = 160;
    public const int FlashCount = 3;
    public const int HitMilliseconds = FlashIntervalMilliseconds * 2 * FlashCount;
    private readonly Queue<(TypeView Type, bool FromPlayer, double Effectiveness)> queue = new();
    private (TypeView Type, bool FromPlayer, double Effectiveness)? current;
    private long startedAt;
    private long elapsed;
    private bool hitStarted;

    public bool IsBusy => current != null || queue.Count > 0;
    public TypeView? EffectType => current is { } attack && elapsed < TravelMilliseconds ? attack.Type : null;
    public bool FromPlayer => current?.FromPlayer ?? false;
    public double Progress => Math.Clamp((double)elapsed / TravelMilliseconds, 0, 1);
    private bool FlashOn => current != null && elapsed >= TravelMilliseconds &&
        (elapsed - TravelMilliseconds) / FlashIntervalMilliseconds % 2 == 0;
    public bool FlashPlayer => FlashOn && !FromPlayer;
    public bool FlashOpponent => FlashOn && FromPlayer;

    public void Enqueue(TypeView type, bool fromPlayer, double effectiveness = 1) =>
        queue.Enqueue((type, fromPlayer, effectiveness));

    // 양쪽 포켓몬 모두 피격 깜빡임이 시작될 때만 상성 배율을 반환한다.
    public double? Update(long now)
    {
        if (current == null && queue.TryDequeue(out var attack))
        {
            current = attack;
            startedAt = now;
            hitStarted = false;
        }
        elapsed = Math.Max(0, now - startedAt);
        double? hitEffectiveness = null;
        if (current is { } hit && !hitStarted && elapsed >= TravelMilliseconds)
        {
            hitStarted = true;
            if (hit.Effectiveness > 0)
                hitEffectiveness = hit.Effectiveness;
        }
        if (current != null && elapsed >= TravelMilliseconds + HitMilliseconds)
            current = null;
        return hitEffectiveness;
    }
}
