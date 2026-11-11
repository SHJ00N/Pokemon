using System.Runtime.ExceptionServices;

namespace Client;

// 콘솔 커서는 하나이므로 출력 순서는 공유하되, 이미지와 정보 출력은 각각 전용 스레드가 담당한다.
// 수신·게임 상태·오디오 이벤트 루프는 출력 작업을 기다리지 않고 계속 진행한다.
sealed class ConsoleRenderThreads : IDisposable
{
    private static ConsoleRenderThreads? current;
    private readonly object gate = new();
    private readonly Queue<(bool Sprite, Action Draw)> jobs = new();
    private readonly Thread spriteThread;
    private readonly Thread informationThread;
    private bool drawing, stopping;
    private ExceptionDispatchInfo? failure;

    public ConsoleRenderThreads()
    {
        spriteThread = new Thread(() => Run(true)) { Name = "PokemonRenderer", IsBackground = true };
        informationThread = new Thread(() => Run(false)) { Name = "BattleInformationRenderer", IsBackground = true };
        if (Interlocked.CompareExchange(ref current, this, null) != null)
            throw new InvalidOperationException("Render threads are already running.");
        spriteThread.Start();
        informationThread.Start();
    }

    public static void Sprites(Action draw) => Post(true, draw);
    public static void Information(Action draw) => Post(false, draw);

    private static void Post(bool sprite, Action draw)
    {
        var renderer = Volatile.Read(ref current);
        if (renderer == null) { draw(); return; }
        lock (renderer.gate)
        {
            renderer.failure?.Throw();
            if (renderer.stopping) throw new ObjectDisposedException(nameof(ConsoleRenderThreads));
            renderer.jobs.Enqueue((sprite, draw));
            Monitor.PulseAll(renderer.gate);
        }
    }

    private void Run(bool sprite)
    {
        while (true)
        {
            Action draw;
            lock (gate)
            {
                while (!stopping && (drawing || jobs.Count == 0 || jobs.Peek().Sprite != sprite))
                    Monitor.Wait(gate);
                if (stopping) return;
                draw = jobs.Dequeue().Draw;
                drawing = true;
            }
            try { draw(); }
            catch (Exception ex)
            {
                lock (gate)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                    jobs.Clear();
                    stopping = true;
                }
            }
            finally
            {
                lock (gate)
                {
                    drawing = false;
                    Monitor.PulseAll(gate);
                }
            }
        }
    }

    public static void Flush()
    {
        var renderer = Volatile.Read(ref current);
        if (renderer == null) return;
        lock (renderer.gate)
        {
            while (renderer.drawing || renderer.jobs.Count > 0) Monitor.Wait(renderer.gate);
            renderer.failure?.Throw();
        }
    }

    public void Dispose()
    {
        try { Flush(); }
        finally
        {
            lock (gate) { stopping = true; Monitor.PulseAll(gate); }
            spriteThread.Join();
            informationThread.Join();
            Interlocked.CompareExchange(ref current, null, this);
        }
    }
}
