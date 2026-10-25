namespace Client;

sealed class InRoomState : ClientState
{
    private readonly int roomId;
    private readonly string hostAddress;
    private sealed record RoomPlayer(string Address, bool IsReady);
    private RoomPlayer[] players;

    public InRoomState(int roomId, string hostAddress)
    {
        this.roomId = roomId;
        this.hostAddress = hostAddress;
        players = [new RoomPlayer(hostAddress, false)];
    }

    protected override string Menu
    {
        get
        {
            RoomPlayer[] currentPlayers = players;
            string playerList = string.Join(Environment.NewLine, currentPlayers.Select((player, index) =>
                $"{(index == 0 ? "호스트" : "참여자")}: {player.Address} - {(player.IsReady ? "준비 완료" : "준비 안 됨")}"));
            return $"\n[{roomId}] 호스트({hostAddress})의 게임 룸\n\n현재 플레이어\n{playerList}\n\n1. 준비 2. 나가기";
        }
    }

    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("ROOM_PLAYERS "))
        {
            string[] parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (4 or 6) ||
                !int.TryParse(parts[1], out int messageRoomId) || messageRoomId != roomId)
                return base.HandleServerMessage(message);

            var updatedPlayers = new RoomPlayer[(parts.Length - 2) / 2];
            for (int index = 0; index < updatedPlayers.Length; index++)
            {
                string ready = parts[3 + index * 2];
                if (ready is not ("0" or "1"))
                    return base.HandleServerMessage(message);

                updatedPlayers[index] = new RoomPlayer(parts[2 + index * 2], ready == "1");
            }

            players = updatedPlayers;
            return this;
        }

        switch (message)
        {
            case "GAME_STARTED":
                SetServerMessage("두 플레이어가 모두 준비했습니다. 게임을 시작합니다.");
                return ChangeState(new InGameState());
            case "READY":
                SetServerMessage("준비 완료!");
                return this;
            case "PLAYER_READY":
                SetServerMessage("상대방이 준비했습니다.");
                return this;
            case "LEFT":
                SetServerMessage("방에서 나왔습니다.");
                return ChangeState(new LobbyState());
            case "PLAYER_JOINED":
                SetServerMessage("상대방이 입장했습니다.");
                return this;
            case "PLAYER_LEFT":
                SetServerMessage("상대방이 퇴장했습니다. 새 상대방을 기다립니다.");
                return this;
            case "ROOM_CLOSED":
                return new LobbyState(); // 방장 종료 시 룸에서 강제 퇴장
            default:
                return base.HandleServerMessage(message);
        }
    }

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;
        switch (input)
        {
            case "1":
            case "2":
                await writer.WriteLineAsync(input);
                break;
            default:
                Notification = "1: 준비, 2: 나가기 중에서 선택하세요.";
                break;
        }

        return true;
    }
}
