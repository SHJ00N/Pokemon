namespace Client;

// 지정한 쪽만 몬스터볼 이동 → 등장 후 대기 순서로 소환한다. 양쪽이면 내 포켓몬부터 시작한다.
sealed class SummonAnimation
{
    public const int TravelMilliseconds = 800;
    public const int RevealMilliseconds = 320;
    public const int SideMilliseconds = TravelMilliseconds + RevealMilliseconds;
    public const int DurationMilliseconds = SideMilliseconds * 2;

    internal readonly record struct Frame(bool ShowPlayer, bool ShowOpponent,
        bool? ThrowOnPlayer, double ThrowProgress);

    private bool pending;
    private long? startedAt;
    private long elapsed;
    private int soundStage = -1;
    private bool[] targets = [true, false];

    public bool IsBusy => pending || startedAt != null;
    public int TotalMilliseconds => SideMilliseconds * targets.Length;

    public Frame? CurrentFrame
    {
        get
        {
            if (!IsBusy) return null;
            int side = (int)(elapsed / SideMilliseconds);
            bool onPlayer = targets[side];
            long sideElapsed = elapsed - side * SideMilliseconds;
            int playerIndex = Array.IndexOf(targets, true), opponentIndex = Array.IndexOf(targets, false);
            return new Frame(playerIndex < 0 || elapsed >= playerIndex * SideMilliseconds + TravelMilliseconds,
                opponentIndex < 0 || elapsed >= opponentIndex * SideMilliseconds + TravelMilliseconds,
                sideElapsed < TravelMilliseconds ? onPlayer : null,
                Math.Clamp((double)sideElapsed / TravelMilliseconds, 0, 1));
        }
    }

    public void Start(bool onPlayer = true, bool onOpponent = true)
    {
        targets = (onPlayer, onOpponent) switch
        {
            (true, true) => [true, false],
            (true, false) => [true],
            (false, true) => [false],
            _ => []
        };
        pending = targets.Length > 0;
        startedAt = null;
        elapsed = 0;
        soundStage = -1;
    }

    // 프레임마다가 아니라 투척 시작·포켓몬 등장 시점에만 효과음을 한 번 반환한다.
    public string? Update(long now)
    {
        if (pending)
        {
            pending = false;
            startedAt = now;
        }
        if (startedAt is not long start) return null;
        elapsed = Math.Max(0, now - start);
        if (elapsed >= TotalMilliseconds) startedAt = null;
        long cueTime = Math.Min(elapsed, TotalMilliseconds - 1);
        int stage = (int)(cueTime / SideMilliseconds) * 2 +
            (cueTime % SideMilliseconds < TravelMilliseconds ? 0 : 1);
        if (stage == soundStage) return null;
        soundStage = stage;
        return stage % 2 == 0 ? SoundManager.PokeballThrow : SoundManager.PokeballRelease;
    }
}
