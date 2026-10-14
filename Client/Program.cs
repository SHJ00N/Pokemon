using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        string host = args.Length > 0 ? args[0] : "10.201.11.76";
        int port = 5000;
        if (args.Length > 1 &&
            (!int.TryParse(args[1], out port) || port is < 1 or > 65535))
        {
            Console.WriteLine("포트는 1~65535 사이의 숫자여야 합니다.");
            return;
        }

        // 1. 서버에 접속
        IPEndPoint ipep = new IPEndPoint(IPAddress.Parse("10.201.11.76"), 5000);
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

            // 사용자 입력 대기중 서버 종료에 대응하기 위해 비동기 사용
            // 사용자 입력이 대기상태에 걸리면 ReceiveMessagesAsync를 일시정지하고 try문 실행 -- 비동기 작업
            using var disconnected = new CancellationTokenSource(); // 입력 중단을 위한 객체
            Task receiving = ReceiveMessagesAsync(reader, disconnected);
            try
            {
                while (!disconnected.IsCancellationRequested)
                {
                    Console.WriteLine("\n1. 방 만들기\n2. 방 입장\n3. 종료");
                    Console.Write("선택: ");
                    string? input = await ReadInputAsync(disconnected.Token);

                    if (input == null || input == "3")
                    {
                        await writer.WriteLineAsync("3");
                        client.Shutdown(SocketShutdown.Send);
                        await receiving.WaitAsync(TimeSpan.FromSeconds(3));
                        break;
                    }

                    if (input == "1")
                    {
                        await writer.WriteLineAsync("1");
                    }
                    else if (input == "2")
                    {
                        Console.Write("방 번호 (1000 이상): ");
                        string? roomInput = await ReadInputAsync(disconnected.Token);
                        if (roomInput == null)
                            break;

                        if (int.TryParse(roomInput, out int roomId) && roomId >= 1000)
                            await writer.WriteLineAsync(roomId.ToString());
                        else
                            Console.WriteLine("1000 이상의 방 번호를 입력하세요.");
                    }
                    else
                    {
                        Console.WriteLine("1, 2, 3 중에서 선택하세요.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 서버가 연결을 닫으면 입력 대기도 종료한다.
            }
            finally
            {
                client.Close();
                await receiving;
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            Console.WriteLine($"통신 오류: {ex.Message}");
        }
    }

    private static async Task<string?> ReadInputAsync(CancellationToken cancellationToken)
    {
        return (await Task.Run(() => Console.ReadLine()).WaitAsync(cancellationToken))?.Trim();
    }

    private static async Task ReceiveMessagesAsync(
        StreamReader reader, CancellationTokenSource disconnected)
    {
        try
        {
            while (await reader.ReadLineAsync() is string message)
            {
                string display = message switch
                {
                    var text when text.StartsWith("CREATED ") =>
                        $"방 생성 완료! 방 번호: {text[8..]} / 상대방 대기 중",
                    var text when text.StartsWith("JOINED ") =>
                        $"방 {text[7..]} 입장 완료!",
                    "PLAYER_JOINED" => "상대방이 입장했습니다.",
                    "PLAYER_LEFT" => "상대방이 퇴장했습니다. 새 상대방을 기다립니다.",
                    "ROOM_CLOSED" => "방장이 나가서 방이 닫혔습니다. 다른 방에 참여할 수 있습니다.",
                    var text when text.StartsWith("ERROR ") => text[6..],
                    _ => message
                };
                Console.WriteLine($"\n[서버] {display}");
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Console.WriteLine($"\n수신 종료: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("\n서버와의 연결이 종료되었습니다.");
            disconnected.Cancel();
        }
    }
}
