namespace Client;

// 살아 있는 포켓몬을 자기 진영의 화면 바깥으로 이동시킨다. 마지막 프레임은 다음 소환 전에 유지한다.
sealed class WithdrawAnimation
{
    public const int DurationMilliseconds = 400;
    internal readonly record struct Frame(bool OnPlayer, bool OnOpponent, double Progress);

    private bool pending;
    private long? startedAt;
    private bool onPlayer, onOpponent;
    private double progress;

    public bool IsBusy => pending || startedAt != null;
    public Frame CurrentFrame => new(onPlayer, onOpponent, progress);

    public void Start(bool withdrawPlayer, bool withdrawOpponent)
    {
        onPlayer = withdrawPlayer;
        onOpponent = withdrawOpponent;
        progress = 0;
        pending = onPlayer || onOpponent;
        startedAt = null;
    }

    public void Update(long now)
    {
        if (pending)
        {
            pending = false;
            startedAt = now;
        }
        if (startedAt is not long start) return;
        progress = Math.Clamp((double)(now - start) / DurationMilliseconds, 0, 1);
        if (progress >= 1) startedAt = null;
    }
}
