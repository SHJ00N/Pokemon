namespace Client;

// 활성화 소리를 기다리고, 실제 회복음 재생 중에 이미지를 표시한 뒤 원래 그림을 복원한다.
sealed class HealingAnimation
{
    public const int MinimumDisplayMilliseconds = 640;
    public const int ClearMilliseconds = 560;
    private enum Phase { Idle, Waiting, Showing, Clearing }
    private Phase phase;
    private long startedAt;

    public bool IsBusy => phase != Phase.Idle;
    public bool ShowImage => phase == Phase.Showing;
    public bool OnPlayer { get; private set; }

    public void Start(bool onPlayer)
    {
        OnPlayer = onPlayer;
        phase = Phase.Waiting;
    }

    public void Update(long now, SoundPlaybackState playback)
    {
        if (phase == Phase.Waiting)
        {
            if (playback == SoundPlaybackState.Pending) return;
            phase = Phase.Showing;
            startedAt = now;
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
