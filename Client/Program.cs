using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        string host = args.Length > 0 ? args[0] : "192.168.16.1";
        int port = 5000;
        if (args.Length > 1 &&
            (!int.TryParse(args[1], out port) || port is < 1 or > 65535))
        {
            Console.WriteLine("포트는 1~65535 사이의 숫자여야 합니다.");
            return;
        }

        // 1. 서버에 접속
        IPEndPoint ipep = new IPEndPoint(IPAddress.Parse("192.168.16.1"), 5000);
        Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(ipep);

        try
        {
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
        ClientState state = new LobbyState();
        state.ShowMenu();

        await foreach (ClientEvent clientEvent in events.ReadAllAsync(cancellationToken))
        {
            switch (clientEvent)
            {
                case UserInput input:
                    if (input.Text == null ||
                        !await state.HandleCommand(input.Text, writer, cancellationToken))
                        return true;
                    break;
                case ServerMessage message:
                    state = state.HandleServerMessage(message.Text);
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
