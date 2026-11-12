namespace Client;

// 상태이상·랭크 변화·버프 이미지를 덧그린 뒤 원래 그림을 복원할 때까지 다음 결과를 보류한다.
sealed class StatusAnimation
{
    public const int DisplayMilliseconds = 480;
    public const int ClearMilliseconds = 560;
    public const int DurationMilliseconds = DisplayMilliseconds + ClearMilliseconds;
    public readonly record struct Effect(int TypeId, string? SoundId, bool OnPlayer);

    private static readonly Dictionary<string, (int TypeId, string? SoundId)> effects = new()
    {
        ["burn"] = (10, SoundManager.StatusBurned),
        ["poison"] = (4, SoundManager.StatusPoisoned),
        ["bad-poison"] = (4, SoundManager.StatusPoisoned),
        ["paralysis"] = (13, SoundManager.StatusParalyzed),
        ["freeze"] = (15, SoundManager.StatusFrozen),
        ["sleep"] = (14, SoundManager.StatusSleep),
        ["confusion"] = (14, SoundManager.StatusConfused),
        ["infatuation"] = (18, null)
    };
    private readonly Queue<Effect> queue = new();
    private Effect? current;
    private long startedAt;
    private long elapsed;

    public bool IsBusy => current != null || queue.Count > 0;
    public Effect? Overlay => elapsed < DisplayMilliseconds ? current : null;

    public bool Enqueue(string code, bool onPlayer)
    {
        if (!effects.TryGetValue(code, out var effect)) return false;
        queue.Enqueue(new Effect(effect.TypeId, effect.SoundId, onPlayer));
        return true;
    }

    public void EnqueueStat(int typeId, bool onPlayer, bool raised) =>
        queue.Enqueue(new Effect(typeId, raised ? SoundManager.StatUp : SoundManager.StatFall, onPlayer));

    // 연출이 시작될 때 한 번만 효과음 ID를 반환한다.
    public string? Update(long now)
    {
        string? soundId = null;
        if (current == null && queue.TryDequeue(out var effect))
        {
            current = effect;
            startedAt = now;
            soundId = effect.SoundId;
        }
        elapsed = Math.Max(0, now - startedAt);
        if (current != null && elapsed >= DurationMilliseconds)
            current = null;
        return soundId;
    }
}
