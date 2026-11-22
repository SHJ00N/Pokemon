namespace Client;

// 활성화 소리가 끝나면 실제 회복음과 함께 ANSI 프레임을 재생한다.
sealed class HealingAnimation
{
    public const int MinimumDisplayMilliseconds = 640;
    public const int ClearMilliseconds = 560;
    private const int PlaybackDisplayMilliseconds = 1500;
    private const int FrameCount = 24;

    public readonly record struct Frame(bool OnPlayer, int ImageIndex);

    private enum Phase { Idle, Waiting, Showing, Clearing }
    private Phase phase;
    private long startedAt;
    private int displayMilliseconds;
    private bool onPlayer;

    public bool IsBusy => phase != Phase.Idle;

    public void Start(bool onPlayer)
    {
        this.onPlayer = onPlayer;
        phase = Phase.Waiting;
    }

    public Frame? CaptureFrame(long now)
    {
        if (phase != Phase.Showing) return null;
        long elapsed = Math.Clamp(now - startedAt, 0, displayMilliseconds - 1);
        return new Frame(onPlayer, (int)(elapsed * FrameCount / displayMilliseconds));
    }

    public void Update(long now, SoundPlaybackState playback)
    {
        if (phase == Phase.Waiting)
        {
            if (playback == SoundPlaybackState.Pending) return;
            phase = Phase.Showing;
            startedAt = now;
            displayMilliseconds = playback == SoundPlaybackState.Playing
                ? PlaybackDisplayMilliseconds : MinimumDisplayMilliseconds;
        }
        if (phase == Phase.Showing && playback != SoundPlaybackState.Playing &&
            now - startedAt >= MinimumDisplayMilliseconds)
        {
            phase = Phase.Clearing;
            startedAt = now;
        }
        if (phase == Phase.Clearing && now - startedAt >= ClearMilliseconds)
            phase = Phase.Idle;
    }
}
