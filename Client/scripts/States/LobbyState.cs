namespace Client;

sealed class LobbyState : ClientState
{
    private static readonly string logo = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "resources", "pokemon_scripts", "pokemon_logo.txt"));
    private bool waitingForRoomNumber;

    protected override string Menu => logo + (waitingForRoomNumber
        ? "\n[로비] 방 번호 (1000 이상): "
        : "\n[로비] 1. 방 만들기 2. 방 입장 3. 종료");

    // 서버 메세지 수신 처리
    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("CREATED ") || message.StartsWith("JOINED "))
        {
            string[] parts = message.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !int.TryParse(parts[1], out int roomId))
                return base.HandleServerMessage(message);

            SetServerMessage(parts[0] == "CREATED"
                ? $"방 생성 완료! 방 번호: {roomId} / 상대방 대기 중"
                : $"방 {roomId} 입장 완료!");
            return ChangeState(new InRoomState(roomId, parts[2]));
        }

        return base.HandleServerMessage(message);
    }

    // 사용자 입력 커맨드 처리
    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;
        if (waitingForRoomNumber)
        {
            waitingForRoomNumber = false;
            if (int.TryParse(input, out int roomId) && roomId >= 1000)
                await writer.WriteLineAsync(roomId.ToString());
            else
                Notification = "1000 이상의 방 번호를 입력하세요.";

            return true;
        }

        switch (input)
        {
            case "1":
                await writer.WriteLineAsync("1");
                break;
            case "2":
                waitingForRoomNumber = true;
                break;
            case "3":
                return false;
            default:
                Notification = "1, 2, 3 중에서 선택하세요.";
                break;
        }

        return true;
    }
}
