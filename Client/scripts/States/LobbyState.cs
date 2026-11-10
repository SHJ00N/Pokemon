using System.Text;
using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class LobbyState : ClientState
{
    public static readonly string TitleBgm = Path.Combine("resources", "sounds", "01 Title Screen.wav");
    public override string BgmPath => TitleBgm;

    public static readonly string Logo = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "resources", "pokemon_scripts", "pokemon_logo.txt"));

    private bool browsing;              // 방 목록 화면
    private RoomSummary[]? roomList;    // 마지막으로 받은 방 목록. 받는 중이면 null

    protected override string Menu => Logo + (browsing
        ? DescribeRoomList()
        : "\n[로비] 1. 방 만들기 2. 방 목록 3. 종료");

    private static bool CanJoin(RoomSummary room) => room.PlayerCount < 2 && !room.InGame;

    private string DescribeRoomList()
    {
        if (roomList == null)
            return "\n[방 목록] 불러오는 중...";

        var text = new StringBuilder("\n[방 목록]  번호: 입장  Enter: 새로고침  0: 뒤로\n");
        if (roomList.Length == 0)
            text.Append("\n  만들어진 방이 없습니다. 0을 눌러 로비에서 방을 만들어 보세요.\n");
        for (int index = 0; index < roomList.Length; index++)
        {
            RoomSummary room = roomList[index];
            string status = room.InGame ? "게임 중" : room.PlayerCount >= 2 ? "가득 참" : "대기 중";
            text.Append($"\n {index + 1,2}. {TextRenderer.FitToWidth(room.HostName + "의 방", 20)}" +
                        $" {room.PlayerCount}/2  {status}");
        }
        return text.Append("\n\n입장할 방 번호: ").ToString();
    }

    // 서버 메세지 수신 처리
    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("ROOM_LIST "))
        {
            roomList = JsonSerializer.Deserialize<RoomSummary[]>(message[10..])
                ?? throw new InvalidDataException("방 목록을 해석할 수 없습니다.");
            return this;
        }

        if (message.StartsWith("CREATED ") || message.StartsWith("JOINED "))
        {
            string[] parts = message.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !int.TryParse(parts[1], out int roomId))
                return base.HandleServerMessage(message);

            bool created = parts[0] == "CREATED";
            SetServerMessage(created
                ? "방을 만들었습니다. 상대방을 기다립니다."
                : $"{parts[2]}의 방에 입장했습니다.");
            return ChangeState(new InRoomState(roomId, parts[2], isHost: created));
        }

        // 입장 실패 등: 서버 메시지를 보여 주고, 방 목록이면 Enter로 새로고침할 수 있다.
        return base.HandleServerMessage(message);
    }

    // 사용자 입력 커맨드 처리
    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;
        if (browsing)
        {
            await HandleRoomListInput(input, writer);
            return true;
        }

        switch (input)
        {
            case "1":
                await writer.WriteLineAsync("1");
                break;
            case "2":
                browsing = true;
                await RequestRoomList(writer);
                break;
            case "3":
                return false;
            default:
                Notification = "1, 2, 3 중에서 선택하세요.";
                break;
        }

        return true;
    }

    private async Task HandleRoomListInput(string input, StreamWriter writer)
    {
        if (input == "0")
        {
            browsing = false;
            roomList = null;
        }
        else if (input.Length == 0)
            await RequestRoomList(writer);
        else if (roomList == null)
            Notification = "방 목록을 불러오는 중입니다.";
        else if (!int.TryParse(input, out int number) || number < 1 || number > roomList.Length)
            Notification = "목록에 있는 방 번호를 입력하세요.";
        else if (!CanJoin(roomList[number - 1]))
            Notification = "게임 중이거나 가득 찬 방입니다. Enter를 눌러 목록을 새로고침하세요.";
        else
            await writer.WriteLineAsync(roomList[number - 1].RoomId.ToString());   // 방 입장
    }

    private async Task RequestRoomList(StreamWriter writer)
    {
        roomList = null;
        await writer.WriteLineAsync("ROOM_LIST");
    }
}
