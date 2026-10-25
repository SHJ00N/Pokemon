using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

class ClientSession
{
    private readonly Socket socket;
    private readonly RoomManager rooms;
    private readonly Channel<string> outgoing = Channel.CreateBounded<string>(64);

    public Guid Id { get; } = Guid.NewGuid();
    public string Address { get; }
    // RoomManager의 잠금 안에서만 읽고 변경한다.
    public int? RoomId { get; set; }
    public bool IsReady { get; set; }
    public ClientState State { get; set; } = new LobbyState();

    public ClientSession(Socket socket, RoomManager rooms)
    {
        this.socket = socket;
        this.rooms = rooms;
        Address = socket.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public void QueueMessage(string message)
    {
        // 잠금 안에서는 큐에만 넣고 실제 송신은 송신 루프가 담당한다.
        if (!outgoing.Writer.TryWrite(message))
            socket.Dispose(); // 응답이 계속 쌓이는 느린 연결은 종료한다.
    }

    public async Task RunAsync()
    {
        Task sending = SendMessagesAsync();
        try
        {
            await ReceiveCommandsAsync();
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"{Address}: 연결 오류 ({ex.SocketErrorCode})");
        }
        catch (ObjectDisposedException)
        {
            // 송신 실패 또는 송신 큐 초과로 이미 닫힌 연결.
        }
        finally
        {
            rooms.LeaveRoom(this);
            outgoing.Writer.TryComplete();
            socket.Dispose();
            await sending;
        }
    }

    private async Task ReceiveCommandsAsync()
    {
        byte[] buffer = new byte[1024];
        var line = new StringBuilder();

        while (true)
        {
            int received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
            if (received == 0)
                return;

            // 숫자 + 줄바꿈: 분할 수신과 여러 명령의 합쳐진 수신을 처리한다.
            for (int index = 0; index < received; index++)
            {
                char character = (char)buffer[index];
                if (character == '\n')
                {
                    string command = line.ToString().TrimEnd('\r');
                    line.Clear();
                    if (!HandleCommand(command))
                        return;
                }
                else
                {
                    line.Append(character);
                    if (line.Length > 256)
                        return; // 잘못된 입력이 무한히 쌓이지 않게 제한한다.
                }
            }
        }
    }
    
    private bool HandleCommand(string text)
    {
        return State.HandleCommand(this, rooms, text);
    }

    private async Task SendMessagesAsync()
    {
        try
        {
            await foreach (string message in outgoing.Reader.ReadAllAsync())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
                int sent = 0;
                while (sent < bytes.Length)
                {
                    int count = await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None);
                    if (count == 0)
                        return;
                    sent += count;
                }
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"{Address}: 송신 오류 ({ex.SocketErrorCode})");
        }
        catch (ObjectDisposedException)
        {
            // 수신 루프가 연결을 정리했다.
        }
        finally
        {
            socket.Dispose();
        }
    }
}
