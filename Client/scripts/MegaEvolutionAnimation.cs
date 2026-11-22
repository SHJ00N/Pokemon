namespace Client;

sealed class MegaEvolutionAnimation
{
    public const int DurationMilliseconds = 5000;
    private const int RevealMilliseconds = 3750;
    private const int ClearMilliseconds = 4550;

    private static readonly int[] BeforeRevealFrames =
    [
        1, 16, 17, 18, 19, 6, 20, 21, 22, 23, 7, 24, 25, 26, 2,
        27, 28, 29, 8, 30, 31, 32, 9, 33, 34, 35, 10, 36, 37, 38, 3,
        39, 40, 41, 11, 42, 43, 44, 12, 45, 46, 13, 47, 48, 49, 4, 50, 51, 52, 53
    ];
    private static readonly int[] AfterRevealFrames = [5, 54, 55, 56, 14, 57, 58, 15, 59, 60];

    public readonly record struct Frame(bool OnPlayer, int ImageIndex, bool HidePokemon);

    private bool onPlayer;
    private Action? reveal;
    private long? startedAt;
    private long elapsed;

    public bool IsBusy => reveal != null || startedAt != null;
    public Frame? CurrentFrame => startedAt != null ? new Frame(onPlayer, elapsed switch
    {
        < RevealMilliseconds => BeforeRevealFrames[(int)(elapsed * BeforeRevealFrames.Length / RevealMilliseconds)],
        < ClearMilliseconds => AfterRevealFrames[(int)((elapsed - RevealMilliseconds) * AfterRevealFrames.Length /
            (ClearMilliseconds - RevealMilliseconds))],
        _ => 0
    }, HidePokemon: elapsed < RevealMilliseconds) : null;

    public void Start(bool onPlayer, Action revealForm)
    {
        this.onPlayer = onPlayer;
        reveal = revealForm;
        startedAt = null;
        elapsed = 0;
    }

    public bool Update(long now, ISoundManager sound)
    {
        if (!IsBusy) return false;
        if (startedAt == null)
        {
            startedAt = now;
            sound.PlaySound(SoundManager.MegaEvolution);
        }

        elapsed = Math.Max(0, now - startedAt.Value);
        bool changed = false;
        if (elapsed >= RevealMilliseconds && reveal is { } apply)
        {
            reveal = null;
            apply();
            changed = true;
        }
        // 음소거 상태에서도 같은 길이로 보여 주고, 재생 중이면 효과음이 끝날 때까지 기다린다.
        if (elapsed >= DurationMilliseconds && sound.GetSoundState(SoundManager.MegaEvolution) != SoundPlaybackState.Playing)
        {
            startedAt = null;
            return true;
        }
        return changed;
    }
}
