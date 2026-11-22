namespace Client;

// 타격 프레임에 명중음을 재생하고 피격 깜빡임을 함께 시작한다.
sealed class BattleAnimation
{
    public const int ImpactMilliseconds = 500;
    public const int EffectMilliseconds = 1500;
    public const int FlashIntervalMilliseconds = 160;
    public const int FlashCount = 3;
    public const int HitMilliseconds = FlashIntervalMilliseconds * 2 * FlashCount;
    private const int FrameCount = 30;
    // 타입 ID 순서대로 각 원본의 타격 프레임을 가리킨다(0부터 시작).
    private static readonly int[] ImpactFrames = [13, 12, 18, 13, 14, 13, 12, 17, 12, 13, 16, 16, 10, 15, 13, 16, 15, 14];

    public readonly record struct Frame(int TypeId, bool FromPlayer, int ImageIndex);

    private readonly Queue<(int TypeId, bool FromPlayer, double Effectiveness)> queue = new();
    private (int TypeId, bool FromPlayer, double Effectiveness)? current;
    private long startedAt;
    private long elapsed;
    private bool hitStarted;

    public bool IsBusy => current != null || queue.Count > 0;
    public Frame? CurrentFrame => current is { } attack && elapsed < EffectMilliseconds
        ? new Frame(attack.TypeId, attack.FromPlayer, GetImageIndex(attack.TypeId)) : null;
    private bool FlashOn => current != null && elapsed >= ImpactMilliseconds &&
        elapsed < ImpactMilliseconds + HitMilliseconds &&
        (elapsed - ImpactMilliseconds) / FlashIntervalMilliseconds % 2 == 0;
    public bool FlashPlayer => current is { FromPlayer: false } && FlashOn;
    public bool FlashOpponent => current is { FromPlayer: true } && FlashOn;

    private int GetImageIndex(int typeId)
    {
        int impact = typeId > 0 && typeId <= ImpactFrames.Length ? ImpactFrames[typeId - 1] : FrameCount / 2;
        return elapsed < ImpactMilliseconds
            ? (int)(elapsed * impact / ImpactMilliseconds)
            : impact + (int)((elapsed - ImpactMilliseconds) * (FrameCount - impact) /
                (EffectMilliseconds - ImpactMilliseconds));
    }

    public void Enqueue(int typeId, bool fromPlayer, double effectiveness = 1) =>
        queue.Enqueue((typeId, fromPlayer, effectiveness));

    // 한 번의 타격당 한 번만 명중음에 사용할 상성 배율을 반환한다.
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
        if (current is { } hit && !hitStarted && elapsed >= ImpactMilliseconds)
        {
            // 프레임 갱신이 늦어져도 타격 프레임과 첫 깜빡임을 함께 보여 준다.
            startedAt = now - ImpactMilliseconds;
            elapsed = ImpactMilliseconds;
            hitStarted = true;
            if (hit.Effectiveness > 0)
                hitEffectiveness = hit.Effectiveness;
        }
        if (current != null && elapsed >= EffectMilliseconds && elapsed >= ImpactMilliseconds + HitMilliseconds)
            current = null;
        return hitEffectiveness;
    }
}
