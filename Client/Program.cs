using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Client;
//test
class Program
{
    static async Task Main(string[] args)
    {
        // 서버를 같은 PC에서 실행하면 기본값(루프백)을 사용하고,
        // 다른 PC의 서버라면 첫 번째 인자로 서버의 IPv4 주소를 전달한다
=======
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        int port = 5050;

        if (args.Length > 1 &&
            (!int.TryParse(args[1], out port) || port is < 1 or > 65535))
        {
            Console.WriteLine("포트는 1~65535 사이의 숫자여야 합니다.");
            return;
        }


        using Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            // 1. 서버에 접속 (IP 주소 또는 호스트 이름)

            client.Connect(host, port);
            using ISoundManager sound = SoundManager.Initialize();
            Console.WriteLine($"서버 접속 완료: {host}:{port}");

            // 메세지 송/수신용 스트림 버퍼 생성
            using NetworkStream stream = new NetworkStream(client);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            var events = Channel.CreateUnbounded<ClientEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            using var stopping = new CancellationTokenSource();
            Task receiving = ReceiveMessagesAsync(reader, events.Writer, stopping.Token);
            Task readingInput = ReadInputEventsAsync(events.Writer, stopping.Token);
            Task updatingAudio = WriteAudioUpdatesAsync(events.Writer, stopping.Token);
            Task updatingTimer = WriteTimerTicksAsync(events.Writer, stopping.Token);
            try
            {
                if (await ProcessEventsAsync(events.Reader, writer, stopping.Token))
                {
                    await writer.WriteLineAsync("3");
                    client.Shutdown(SocketShutdown.Send);
                    await receiving.WaitAsync(TimeSpan.FromSeconds(3));
                    Console.WriteLine("\n서버와의 연결이 종료되었습니다.");
                }
            }
            finally
            {
                stopping.Cancel();
                client.Close();
                await receiving;
                await readingInput;
                await updatingAudio;
                await updatingTimer;
                events.Writer.TryComplete();
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            Console.WriteLine($"통신 오류: {ex.Message}");
        }
    }

    private static async Task<bool> ProcessEventsAsync(
        ChannelReader<ClientEvent> events, StreamWriter writer, CancellationToken cancellationToken)
    {
        ISoundManager sound = SoundManager.Instance;
        ClientState state = new NicknameState();
        if (state.BgmPath is string initialBgm)
            sound.PlayBgm(initialBgm);
        state.ShowMenu();

        await foreach (ClientEvent clientEvent in events.ReadAllAsync(cancellationToken))
        {
            switch (clientEvent)
            {
                case AudioUpdate:
                    sound.Update();
                    state.OnFrame();
                    continue;
                case TimerTick:
                    // 화면 전체를 다시 그리지 않고 남은 시간 표시만 고친다. 입력 중인 글자는 그대로 둔다.
                    state.UpdateTimer();
                    continue;
                case UserInput input:
                    if (input.Text == null ||
                        !await state.HandleCommand(input.Text, writer, cancellationToken))
                        return true;
                    break;
                case ServerMessage message:
                    ClientState nextState = state.HandleServerMessage(message.Text);
                    // 상태 변경 시 bgm 변경
                    if (nextState.BgmPath != state.BgmPath)
                    {
                        if (nextState.BgmPath is string nextBgm)
                            sound.PlayBgm(nextBgm);
                        else
                            sound.StopBgm();
                    }
                    state = nextState;
                    break;
                case ConnectionClosed closed:
                    if (closed.Error != null)
                        Console.WriteLine($"\n{closed.Error}");
                    Console.WriteLine("\n서버와의 연결이 종료되었습니다.");
                    return false;
            }

            // 상태 변경과 모든 메뉴 출력은 이 루프에서만 수행한다.
            state.ShowMenu();
        }

        return false;
    }

    // 오디오는 일정 시간마다 주기적으로 업데이트 된다.
    private static async Task WriteAudioUpdatesAsync(
        ChannelWriter<ClientEvent> events, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
        var update = new AudioUpdate();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await events.WriteAsync(update, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 종료 시 오디오 업데이트 이벤트 생성도 중단한다.
        }
    }

    // 제한시간은 서버가 매초 보내지 않으므로 클라이언트가 스스로 센다. 초가 바뀔 때만 화면을 고친다.
    private static async Task WriteTimerTicksAsync(
        ChannelWriter<ClientEvent> events, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        var tick = new TimerTick();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await events.WriteAsync(tick, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 종료 시 제한시간 이벤트 생성도 중단한다.
        }
    }

    private static async Task ReadInputEventsAsync(
        ChannelWriter<ClientEvent> events, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? input = await Task.Run(() => Console.ReadLine()).WaitAsync(cancellationToken);
                await events.WriteAsync(new UserInput(input?.Trim()), cancellationToken);
                if (input == null)
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 서버 연결 종료 시 입력 이벤트 수집도 중단한다.
        }
        catch (IOException ex)
        {
            events.TryWrite(new ConnectionClosed($"입력 오류: {ex.Message}"));
        }
    }

    private static async Task ReceiveMessagesAsync(
        StreamReader reader, ChannelWriter<ClientEvent> events, CancellationToken cancellationToken)
    {
        string? error = null;
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is string message)
                await events.WriteAsync(new ServerMessage(message), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 클라이언트 종료에 따른 수신 취소.
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            error = $"수신 종료: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                events.TryWrite(new ConnectionClosed(error));
        }
    }
}
