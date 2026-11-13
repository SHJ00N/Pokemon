namespace Client;

// 내려간 줄 수만큼 그림의 아래쪽을 잘라 내며, 끝나면 교체 전까지 숨긴 상태를 유지한다.
sealed class FaintAnimation
{
    public const int RowIntervalMilliseconds = 40;
    private bool pending;
    private long? startedAt;
    private int rows;

    public bool IsBusy => pending || startedAt != null;
    public double Progress { get; private set; }

    public void Start(int rowCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCount);
        Reset();
        rows = rowCount;
        pending = true;
    }

    public void Reset()
    {
        pending = false;
        startedAt = null;
        Progress = 0;
    }

    public void Update(long now)
    {
        if (pending)
        {
            startedAt = now;
            pending = false;
        }
        if (startedAt is not long start) return;
        long sunkRows = Math.Clamp((now - start) / RowIntervalMilliseconds, 0, rows);
        Progress = (double)sunkRows / rows;
        if (Progress >= 1) startedAt = null;
    }
}
