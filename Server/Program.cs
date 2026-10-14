using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

class Program
{
    private static readonly ConcurrentDictionary<Socket, byte> Clients = new();

    /*
    서버는 비동기 방식으로 클라이언트의 접속과 통신을 처리합니다. 
    await 키워드가 있으면 해당 함수의 동작이 멈추고 호출한 함수의 실행 흐름으로 돌아옵니다. 
    */
    // 내부에서 async task 타입 함수를 호출하므로 main 함수의 반환 타입을 변경
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // db 조회 및 게임 데이터 목록 생성
        string databasePath = Path.Combine(AppContext.BaseDirectory, "resources\\db", "Pokemon.db");
        GameData data = GameData.Load(databasePath);
        Console.WriteLine(
            $"Loaded {data.PokemonById.Count} Pokemon, {data.TypesById.Count} types, " +
            $"{data.EffectivenessByType.Count} type matchups, and {data.MovesById.Count} moves, " +
            $"{data.LearnableMovesByPokemonId.Values.Sum(ids => ids.Count)} learnset entries from {databasePath}");
        
        // TCP 소켓 생성
        int port = args.Length == 0 ? 5000 : int.Parse(args[0]);
        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);

        using Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        server.Bind(localEndPoint);
        server.Listen(20);  // 최대 20개의 클라이언트 요청 대기

        Console.WriteLine($"클라이언트 접속 대기 중... 포트: {port}");

        while (true)
        {
            
            Socket client = await server.AcceptAsync();
            Clients.TryAdd(client, 0);
            Console.WriteLine($"{client.RemoteEndPoint}: 접속 / 현재 {Clients.Count}명");

            // 클라이언트의 수신을 기다리는 동안 다음 접속을 받는다.
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(Socket client)
    {
        string address = client.RemoteEndPoint?.ToString() ?? "unknown";    // 클라이언트 접속 확인용
        byte[] buffer = new byte[1024];

        try
        {
            while (true)
            {
                int received = await client.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);

                // 0바이트 수신은 상대가 정상적으로 연결을 종료했다는 의미다.
                if (received == 0)
                    break;

                Console.WriteLine($"{address}: {received}바이트 수신");
                // 게임 메시지 처리는 여기서 수행한다.
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"{address}: 연결 오류 ({ex.SocketErrorCode})");
        }
        finally
        {
            Clients.TryRemove(client, out _);
            client.Dispose();
            Console.WriteLine($"{address}: 접속 종료 / 현재 {Clients.Count}명");
        }
    }
}
