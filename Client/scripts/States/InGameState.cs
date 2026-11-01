using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class InGameState : ClientState
{
    private const int TeamSize = 3;

    // 전투 중 입력 단계
    private enum BattleMenu { Waiting, Main, Fight, Switch, ForcedSwitch }

    protected override string Menu => "\n[게임 중]";
    private PokemonCatalogResponse? catalog;
    // 포켓몬 3마리를 먼저 모두 고른 뒤, 고른 순서대로 각 포켓몬의 기술 4개를 고른다.
    private readonly List<PokemonCatalogEntry> pickedPokemon = new();
    private readonly List<PokemonSelection> team = new();   // 기술까지 고른 포켓몬
    private readonly List<int> selectedMoves = new();       // 지금 기술을 고르는 포켓몬의 기술

    // 지금 기술을 고르는 포켓몬. 포켓몬을 아직 다 고르지 않았거나 기술 선택이 끝났으면 null
    private PokemonCatalogEntry? MoveTarget =>
        pickedPokemon.Count == TeamSize && team.Count < TeamSize ? pickedPokemon[team.Count] : null;
    private bool submitted;

    private BattleView? battle;
    private BattleMenu menu = BattleMenu.Waiting;
    private BattleMenu menuBeforeWaiting = BattleMenu.Main;  // 서버가 선택을 거절하면 돌아갈 단계
    private string? result;                                   // "WIN" 또는 "LOSE"
    private readonly List<string> battleLog = new();

    public override void ShowMenu() => InGameRenderer.Draw(
        catalog, pickedPokemon.Select(p => p.Name).ToList(), MoveTarget, selectedMoves, submitted,
        battle, Notification, battleLog, GetPrompt(),
        menu is BattleMenu.Switch or BattleMenu.ForcedSwitch ? GetSwitchCandidates() : null,
        menu is BattleMenu.Fight or BattleMenu.Switch, result);

    private string GetPrompt() => menu switch
    {
        BattleMenu.Main => "행동을 선택하세요. 1. 싸운다 2. 교체: ",
        BattleMenu.Fight => "공격할 기술의 번호를 선택하세요: ",
        BattleMenu.Switch => "교체할 포켓몬의 번호를 선택하세요: ",
        BattleMenu.ForcedSwitch => "포켓몬이 쓰러졌습니다. 내보낼 포켓몬의 번호를 선택하세요: ",
        _ => "상대방의 행동을 기다리고 있습니다."
    };

    // 교체할 수 있는 팀 번호(0부터): 출전 중이 아니고 쓰러지지 않은 포켓몬
    private List<int> GetSwitchCandidates() => battle == null ? new() : Enumerable.Range(0, battle.Team.Length)
        .Where(index => index != battle.Active && battle.Team[index].Pokemon.CurrentHp > 0).ToList();

    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("POKEMON_CATALOG "))
        {
            catalog = JsonSerializer.Deserialize<PokemonCatalogResponse>(message[16..])
                ?? throw new InvalidDataException("포켓몬 목록을 해석할 수 없습니다.");
            Notification = null;
            return this;
        }
        if (message.StartsWith("BATTLE_STATE "))
        {
            if (battle == null)
                Notification = "양쪽 플레이어의 포켓몬 선택이 완료되었습니다.";
            battle = JsonSerializer.Deserialize<BattleView>(message[13..])
                ?? throw new InvalidDataException("전투 화면 정보를 해석할 수 없습니다.");
            return this;
        }
        if (message.StartsWith("BATTLE_LOG "))
        {
            AddLog(message[11..]);
            return this;
        }
        if (message is "CHOOSE_ACTION" or "CHOOSE_SWITCH" or "WAIT_SWITCH")
        {
            menu = message switch
            {
                "CHOOSE_ACTION" => BattleMenu.Main,
                "CHOOSE_SWITCH" => BattleMenu.ForcedSwitch,
                _ => BattleMenu.Waiting
            };
            return this;
        }
        if (message.StartsWith("BATTLE_END "))
        {
            result = message[11..];
            menu = BattleMenu.Waiting;
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
            pickedPokemon.Clear();
            team.Clear();
            selectedMoves.Clear();
        }
        if (message.StartsWith("ERROR ") && battle != null && menu == BattleMenu.Waiting && result == null)
            menu = menuBeforeWaiting;
        return base.HandleServerMessage(message);
    }

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;

        if (result != null)
            Notification = "재도전·나가기는 아직 구현되지 않았습니다.";
        else if (battle != null)
            await HandleBattleInput(input, writer);
        else if (catalog == null)
            Notification = "서버에서 포켓몬 목록을 받는 중입니다.";
        else if (submitted)
            Notification = "상대방의 선택을 기다리는 중입니다.";
        else if (!int.TryParse(input, out int id))
            Notification = "목록에 표시된 번호를 입력하세요.";
        else if (MoveTarget is not PokemonCatalogEntry target)
        {
            PokemonCatalogEntry? pokemon = catalog.Pokemon.FirstOrDefault(p => p.PokemonId == id);
            if (pokemon == null)
                Notification = "목록에 있는 포켓몬 도감번호를 입력하세요.";
            else if (pickedPokemon.Any(p => p.PokemonId == id))
                Notification = "이미 팀에 있는 포켓몬입니다. 다른 포켓몬을 선택하세요.";
            else if (pokemon.LearnableMoveIds.Length < 4)
                Notification = "서로 다른 기술 4개를 배울 수 있는 포켓몬을 선택하세요.";
            else
                pickedPokemon.Add(pokemon);
        }
        else if (!target.LearnableMoveIds.Contains(id))
            Notification = "선택한 포켓몬이 배울 수 있는 기술 번호를 입력하세요.";
        else if (selectedMoves.Contains(id))
            Notification = "이미 선택한 기술입니다. 다른 기술을 선택하세요.";
        else
        {
            selectedMoves.Add(id);
            if (selectedMoves.Count == 4)
            {
                team.Add(new PokemonSelection(target.PokemonId, selectedMoves.ToArray()));
                selectedMoves.Clear();
                if (team.Count == TeamSize)
                {
                    await writer.WriteLineAsync("SELECT_TEAM " + JsonSerializer.Serialize(team));
                    submitted = true;
                }
            }
        }
        return true;
    }

    private async Task HandleBattleInput(string input, StreamWriter writer)
    {
        int.TryParse(input, out int number);
        switch (menu)
        {
            case BattleMenu.Main:
                if (number == 1)
                    menu = BattleMenu.Fight;
                else if (number == 2 && GetSwitchCandidates().Count == 0)
                    Notification = "교체할 수 있는 포켓몬이 없습니다.";
                else if (number == 2)
                    menu = BattleMenu.Switch;
                else
                    Notification = "1. 싸운다 2. 교체 중에서 선택하세요.";
                break;

            case BattleMenu.Fight:
                int moveCount = battle!.Team[battle.Active].Moves.Length;
                if (input == "0")
                    menu = BattleMenu.Main;
                else if (number < 1 || number > moveCount)
                    Notification = $"기술 번호 1~{moveCount}를 입력하세요. (0. 뒤로)";
                else
                {
                    await writer.WriteLineAsync($"USE_MOVE {number}");
                    WaitForOpponent();
                }
                break;

            case BattleMenu.Switch:
            case BattleMenu.ForcedSwitch:
                List<int> candidates = GetSwitchCandidates();
                if (input == "0" && menu == BattleMenu.Switch)
                    menu = BattleMenu.Main;
                else if (number < 1 || number > candidates.Count)
                    Notification = "목록에 있는 포켓몬 번호를 입력하세요.";
                else
                {
                    // 서버에는 팀 번호(1~3)를 보낸다.
                    await writer.WriteLineAsync($"SWITCH {candidates[number - 1] + 1}");
                    WaitForOpponent();
                }
                break;

            default:
                Notification = "상대방의 행동을 기다리는 중입니다.";
                break;
        }
    }

    private void WaitForOpponent()
    {
        menuBeforeWaiting = menu == BattleMenu.ForcedSwitch ? BattleMenu.ForcedSwitch : BattleMenu.Main;
        menu = BattleMenu.Waiting;
    }

    // 화면에는 최근 기록 몇 줄만 표시한다.
    private void AddLog(string line)
    {
        battleLog.Add(line);
        if (battleLog.Count > 5)
            battleLog.RemoveAt(0);
    }
}
