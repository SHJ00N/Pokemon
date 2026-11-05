namespace Client;

// 접속 직후 닉네임을 정한다. 닉네임은 방 이름("상희의 방")과 방 안의 플레이어 목록에 표시된다.
sealed class NicknameState : ClientState
{
    private const int MaxLength = 8;   // 서버의 RoomManager.MaxNicknameLength와 같다.

    // 로비와 같은 배경음을 써서 로비로 넘어갈 때 음악이 처음부터 다시 나오지 않게 한다.
    public override string BgmPath => LobbyState.TitleBgm;

    protected override string Menu => LobbyState.Logo + $"\n닉네임을 입력하세요 (띄어쓰기 없이 {MaxLength}글자까지): ";

    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("NAME_ACCEPTED "))
        {
            Notification = $"{message[14..]}님, 환영합니다!";
            return ChangeState(new LobbyState());
        }
        return base.HandleServerMessage(message);
    }

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;
        if (input.Length is 0 or > MaxLength || input.Any(char.IsWhiteSpace))
            Notification = $"닉네임은 띄어쓰기 없이 1~{MaxLength}글자로 입력하세요.";
        else
            await writer.WriteLineAsync("NAME " + input);
        return true;
    }
}
