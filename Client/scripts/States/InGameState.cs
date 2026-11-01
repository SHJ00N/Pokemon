using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class InGameState : ClientState
{
    protected override string Menu => "\n[게임 중]";
    private PokemonCatalogResponse? catalog;
    private PokemonCatalogEntry? selectedPokemon;
    private readonly List<int> selectedMoves = new();
    private bool submitted;
    private BattleView? battle;

    private static readonly string[] RpsNames = ["", "가위", "바위", "보"];
    private int? rpsChoice;          // 이번 가위바위보에서 보낸 선택. 비기면 다시 null
    private bool? myTurn;            // 가위바위보로 순서가 정해지기 전과 전투 종료 후에는 null
    private string? result;          // "WIN" 또는 "LOSE"
    private readonly List<string> battleLog = new();

    public override void ShowMenu() => InGameRenderer.Draw(
        catalog, selectedPokemon, selectedMoves, submitted, battle, Notification, battleLog, GetPrompt(), result);

    private string GetPrompt()
    {
        if (myTurn == true) return "공격할 기술의 번호를 선택하세요: ";
        if (myTurn == false) return "상대방의 행동을 기다리고 있습니다.";
        return rpsChoice == null
            ? "가위바위보로 선공을 정합니다. 1. 가위 2. 바위 3. 보: "
            : $"{RpsNames[rpsChoice.Value]}를 냈습니다. 상대방의 선택을 기다리고 있습니다.";
    }

    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("POKEMON_CATALOG "))
        {
            catalog = JsonSerializer.Deserialize<PokemonCatalogResponse>(message[16..])
                ?? throw new InvalidDataException("포켓몬 목록을 해석할 수 없습니다.");
            Notification = null;
            return this;
        }
        if (message.StartsWith("BATTLE_READY "))
        {
            battle = JsonSerializer.Deserialize<BattleView>(message[13..])
                ?? throw new InvalidDataException("전투 화면 정보를 해석할 수 없습니다.");
            Notification = "양쪽 플레이어의 포켓몬 선택이 완료되었습니다.";
            return this;
        }
        if (message.StartsWith("RPS_RESULT "))
        {
            // RPS_RESULT <내 선택> <상대 선택> <WIN|LOSE|DRAW>
            string[] parts = message.Split(' ');
            if (parts.Length == 4 && int.TryParse(parts[1], out int mine) && int.TryParse(parts[2], out int theirs) &&
                mine is >= 1 and <= 3 && theirs is >= 1 and <= 3)
            {
                string outcome = parts[3] switch
                {
                    "WIN" => "이겼습니다! 먼저 공격합니다.",
                    "LOSE" => "졌습니다. 상대방이 먼저 공격합니다.",
                    _ => "비겼습니다. 다시 선택하세요."
                };
                AddLog($"가위바위보: 나 {RpsNames[mine]} vs 상대 {RpsNames[theirs]} → {outcome}");
                rpsChoice = null;
                Notification = null;
                return this;
            }
        }
        if (message.StartsWith("TURN "))
        {
            myTurn = message == "TURN MY";
            return this;
        }
        if (message.StartsWith("MOVE_RESULT ") && battle != null)
        {
            MoveResult move = JsonSerializer.Deserialize<MoveResult>(message[12..])
                ?? throw new InvalidDataException("기술 결과를 해석할 수 없습니다.");
            battle = battle with
            {
                Player = battle.Player with { CurrentHp = move.PlayerHp },
                Opponent = battle.Opponent with { CurrentHp = move.OpponentHp }
            };
            AddLog(DescribeMove(move));
            return this;
        }
        if (message.StartsWith("BATTLE_END "))
        {
            result = message[11..];
            myTurn = null;
            Notification = null;
            return this;
        }
        if (message == "SELECTION_ACCEPTED")
        {
            SetServerMessage("선택 완료! 상대방 선택을 기다립니다.");
            return this;
        }
        if (message == "ROOM_CLOSED")
            return new LobbyState();
        if (message.StartsWith("RETURN_TO_ROOM "))
        {
            string[] parts = message.Split(' ', 3);
            if (parts.Length == 3 && int.TryParse(parts[1], out int roomId))
                return new InRoomState(roomId, parts[2]);
        }
        if (message.StartsWith("ERROR ") && submitted && battle == null)
        {
            submitted = false;
            selectedPokemon = null;
            selectedMoves.Clear();
        }
        return base.HandleServerMessage(message);
    }

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;

        if (result != null)
            Notification = "재도전·나가기는 아직 구현되지 않았습니다.";
        else if (battle != null && myTurn == null)
        {
            if (rpsChoice != null)
                Notification = "상대방의 선택을 기다리는 중입니다.";
            else if (!int.TryParse(input, out int choice) || choice is < 1 or > 3)
                Notification = "1. 가위 2. 바위 3. 보 중에서 선택하세요.";
            else
            {
                await writer.WriteLineAsync($"RPS {choice}");
                rpsChoice = choice;
            }
        }
        else if (battle != null && myTurn == false)
            Notification = "상대방의 차례입니다.";
        else if (battle != null)
        {
            if (!int.TryParse(input, out int slot) || slot < 1 || slot > battle.Moves.Length)
                Notification = $"기술 번호 1~{battle.Moves.Length}를 입력하세요.";
            else
                await writer.WriteLineAsync($"USE_MOVE {slot}");
        }
        else if (catalog == null)
            Notification = "서버에서 포켓몬 목록을 받는 중입니다.";
        else if (submitted)
            Notification = "상대방의 선택을 기다리는 중입니다.";
        else if (!int.TryParse(input, out int id))
            Notification = "목록에 표시된 번호를 입력하세요.";
        else if (selectedPokemon == null)
        {
            PokemonCatalogEntry? pokemon = catalog.Pokemon.FirstOrDefault(p => p.PokemonId == id);
            if (pokemon == null)
                Notification = "목록에 있는 포켓몬 도감번호를 입력하세요.";
            else if (pokemon.LearnableMoveIds.Length < 4)
                Notification = "서로 다른 기술 4개를 배울 수 있는 포켓몬을 선택하세요.";
            else
                selectedPokemon = pokemon;
        }
        else if (!selectedPokemon.LearnableMoveIds.Contains(id))
            Notification = "선택한 포켓몬이 배울 수 있는 기술 번호를 입력하세요.";
        else if (selectedMoves.Contains(id))
            Notification = "이미 선택한 기술입니다. 다른 기술을 선택하세요.";
        else
        {
            selectedMoves.Add(id);
            if (selectedMoves.Count == 4)
            {
                var selection = new PokemonSelection(selectedPokemon.PokemonId, selectedMoves.ToArray());
                await writer.WriteLineAsync("SELECT_POKEMON " + JsonSerializer.Serialize(selection));
                submitted = true;
            }
        }
        return true;
    }

    // 화면에는 최근 기록 몇 줄만 표시한다.
    private void AddLog(string line)
    {
        battleLog.Add(line);
        if (battleLog.Count > 3)
            battleLog.RemoveAt(0);
    }

    private static string DescribeMove(MoveResult move)
    {
        string text = $"{(move.ByPlayer ? "내" : "상대")} {move.AttackerName}의 {move.MoveName}!";
        if (!move.Supported) return text + " (아직 구현되지 않은 기술이라 효과가 없었다)";
        if (!move.Hit) return text + " 공격이 빗나갔다!";
        if (move.Effectiveness == 0) return text + " 효과가 없는 것 같다...";
        if (move.Effectiveness > 1) text += " 효과가 굉장했다!";
        else if (move.Effectiveness < 1) text += " 효과가 별로인 듯하다...";
        return text + $" ({move.Damage} 데미지)";
    }
}
