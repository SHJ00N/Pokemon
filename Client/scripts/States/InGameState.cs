using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class InGameState : ClientState
{
    private const int TeamSize = 3;

    // 전투 중 입력 단계
    private enum BattleMenu { Waiting, Main, Fight, Switch, ForcedSwitch, BatonPass, Surrender }
    private const int BatonPassMoveId = 226;   // 배턴터치: 쓰기 전에 내보낼 포켓몬을 고른다.
    private int batonPassSlot;                 // 배턴터치를 고른 기술 슬롯

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
    private BattleMenu menuBeforeSurrender = BattleMenu.Main; // 항복 확인에서 뒤로 가면 돌아갈 단계
    private string? result;                                   // "WIN", "LOSE" 또는 "DRAW"
    private readonly List<string> battleLog = new();

    // 선택 제한시간 (서버 RoomManager.Timer.cs와 같은 값). 서버는 남은 시간을 보내지 않으므로
    // 안내(포켓몬 목록, CHOOSE_ACTION 등)를 받은 순간부터 직접 센다. 시간 초과 판정은 서버가 한다.
    private const int TeamTimeLimitSeconds = 60, ActionTimeLimitSeconds = 60;
    private long? deadline;         // Environment.TickCount64 기준 마감 시각(ms). null이면 제한시간 없음
    private int? shownSeconds;      // 화면에 마지막으로 그린 남은 초

    private int? RemainingSeconds => deadline is long end && result == null
        ? (int)Math.Max(0, (end - Environment.TickCount64 + 999) / 1000) : null;

    private void StartTimer(int seconds) => deadline = Environment.TickCount64 + seconds * 1000L;

    // 재도전하면 서버가 포켓몬 목록을 다시 보내지 않으므로 이전 상태의 목록을 넘겨받는다.
    public InGameState(PokemonCatalogResponse? catalog = null)
    {
        this.catalog = catalog;
        if (catalog != null)
            StartTimer(TeamTimeLimitSeconds);   // 재도전: 포켓몬 선택 제한시간이 다시 시작된다.
    }

    public override void ShowMenu()
    {
        shownSeconds = RemainingSeconds;
        InGameRenderer.Draw(
            catalog, pickedPokemon.Select(p => p.Name).ToList(), MoveTarget, selectedMoves, submitted,
            battle, Notification, battleLog, GetPrompt(),
            menu is BattleMenu.Switch or BattleMenu.ForcedSwitch or BattleMenu.BatonPass ? GetSwitchCandidates() : null,
            menu is BattleMenu.Fight or BattleMenu.Switch or BattleMenu.BatonPass, result, shownSeconds);
    }

    public override void UpdateTimer()
    {
        if (RemainingSeconds is int seconds && seconds != shownSeconds)
        {
            shownSeconds = seconds;
            InGameRenderer.UpdateTimer(seconds);
        }
    }

    private string GetPrompt() => menu switch
    {
        BattleMenu.Main => "행동을 선택하세요. 1. 싸운다 2. 교체 3. 항복: ",
        BattleMenu.Fight => "공격할 기술의 번호를 선택하세요: ",
        BattleMenu.Switch => "교체할 포켓몬의 번호를 선택하세요: ",
        BattleMenu.ForcedSwitch => "포켓몬이 쓰러졌습니다. 내보낼 포켓몬 번호: ",
        BattleMenu.BatonPass => "배턴터치로 내보낼 포켓몬의 번호를 선택하세요: ",
        BattleMenu.Surrender => "항복하면 패배합니다. 1. 항복 0. 뒤로: ",
        _ => "상대방의 행동을 기다리고 있습니다. (3. 항복)"
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
            StartTimer(TeamTimeLimitSeconds);
            return this;
        }
        if (message.StartsWith("BATTLE_STATE "))
        {
            if (battle == null)
            {
                Notification = "양쪽 플레이어의 포켓몬 선택이 완료되었습니다.";
                deadline = null;    // 포켓몬 선택 제한시간 끝. 행동 선택 제한시간은 CHOOSE_ACTION부터 센다.
            }
            battle = JsonSerializer.Deserialize<BattleView>(message[13..])
                ?? throw new InvalidDataException("전투 화면 정보를 해석할 수 없습니다.");
            return this;
        }
        if (message.StartsWith("BATTLE_LOG "))
        {
            AddLog(message[11..]);
            return this;
        }
        if (message is "CHOOSE_ACTION" or "CHOOSE_SWITCH" or "WAIT_SWITCH" or "LOCKED")
        {
            menu = message switch
            {
                "CHOOSE_ACTION" => BattleMenu.Main,
                "CHOOSE_SWITCH" => BattleMenu.ForcedSwitch,
                _ => BattleMenu.Waiting
            };
            if (message == "LOCKED")
                Notification = "난동을 부리고 있어서 같은 기술을 계속 사용합니다.";
            // 내가 고르는 중이 아니면(WAIT_SWITCH, LOCKED) 상대가 고르는 데 남은 시간이다.
            StartTimer(ActionTimeLimitSeconds);
            return this;
        }
        if (message.StartsWith("BATTLE_END "))
        {
            // BATTLE_END <WIN|LOSE|DRAW> [SURRENDER|LEFT|TIMEOUT]: 항복·퇴장·시간 초과로 끝났으면 이유가 붙는다.
            string[] parts = message.Split(' ');
            result = parts[1];
            menu = BattleMenu.Waiting;
            Notification = parts.Length < 3 ? null : (parts[1], parts[2]) switch
            {
                ("LOSE", "SURRENDER") => "항복했습니다.",
                (_, "SURRENDER") => "상대방이 항복했습니다.",
                ("LOSE", "TIMEOUT") => "제한시간 안에 선택하지 않아 패배했습니다.",
                ("WIN", "TIMEOUT") => "상대방이 제한시간 안에 선택하지 않아 승리했습니다.",
                ("DRAW", "TIMEOUT") => "양쪽 모두 제한시간 안에 선택하지 않아 무승부입니다.",
                (_, "LEFT") => "상대방이 나가서 항복으로 처리되었습니다. 재도전은 할 수 없습니다.",
                _ => null
            };
            return this;
        }
        switch (message)
        {
            case "REMATCH_REQUESTED":
                Notification = "재도전을 신청했습니다. 상대방을 기다립니다.";
                return this;
            case "PLAYER_REMATCH":
                Notification = "상대방이 재도전을 신청했습니다. 1. 재도전을 고르면 시작합니다.";
                return this;
            case "REMATCH_STARTED":
                Notification = "재도전! 포켓몬과 기술을 다시 선택하세요.";
                return ChangeState(new InGameState(catalog));
            case "LEFT":
                Notification = "방에서 나왔습니다.";
                return ChangeState(new LobbyState());
            case "PLAYER_LEFT":
                Notification = result != null
                    ? "상대방이 나갔습니다. 재도전은 할 수 없습니다."
                    : "상대방이 나갔습니다.";
                return this;
        }
        if (message == "SELECTION_ACCEPTED")
        {
            SetServerMessage("선택 완료! 상대방 선택을 기다립니다.");
            return this;
        }
        if (message == "ROOM_CLOSED")
        {
            Notification = "방장이 나가서 방이 닫혔습니다.";
            return ChangeState(new LobbyState());
        }
        if (message.StartsWith("RETURN_TO_ROOM "))
        {
            string[] parts = message.Split(' ', 3);
            if (parts.Length == 3 && int.TryParse(parts[1], out int roomId))
                return ChangeState(new InRoomState(roomId, parts[2], isHost: true));   // 남은 내가 방장
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
            await HandleResultInput(input, writer);
        else if (RemainingSeconds == 0)
            Notification = "제한시간이 지났습니다. 서버의 판정을 기다립니다.";
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
                else if (number == 3)
                    AskSurrender();
                else
                    Notification = "1. 싸운다 2. 교체 3. 항복 중에서 선택하세요.";
                break;

            case BattleMenu.Surrender:
                if (number == 1)
                {
                    await writer.WriteLineAsync("SURRENDER");
                    menuBeforeWaiting = menuBeforeSurrender;
                    menu = BattleMenu.Waiting;
                }
                else if (input == "0")
                    menu = menuBeforeSurrender;
                else
                    Notification = "1. 항복 0. 뒤로 중에서 선택하세요.";
                break;

            case BattleMenu.Fight:
                int moveCount = battle!.Team[battle.Active].Moves.Length;
                if (input == "0")
                    menu = BattleMenu.Main;
                else if (number < 1 || number > moveCount)
                    Notification = $"기술 번호 1~{moveCount}를 입력하세요. (0. 뒤로)";
                else if (battle.Team[battle.Active].Moves[number - 1].MoveId == BatonPassMoveId &&
                         GetSwitchCandidates().Count > 0)
                {
                    batonPassSlot = number;
                    menu = BattleMenu.BatonPass;
                }
                else
                {
                    await writer.WriteLineAsync($"USE_MOVE {number}");
                    WaitForOpponent();
                }
                break;

            case BattleMenu.Switch:
            case BattleMenu.ForcedSwitch:
            case BattleMenu.BatonPass:
                List<int> candidates = GetSwitchCandidates();
                if (input == "0" && menu != BattleMenu.ForcedSwitch)
                    menu = menu == BattleMenu.BatonPass ? BattleMenu.Fight : BattleMenu.Main;
                else if (number < 1 || number > candidates.Count)
                    Notification = "목록에 있는 포켓몬 번호를 입력하세요.";
                else
                {
                    // 서버에는 팀 번호(1~3)를 보낸다.
                    int teamSlot = candidates[number - 1] + 1;
                    await writer.WriteLineAsync(menu == BattleMenu.BatonPass
                        ? $"USE_MOVE {batonPassSlot} {teamSlot}" : $"SWITCH {teamSlot}");
                    WaitForOpponent();
                }
                break;

            default:
                // 상대가 행동을 고르지 않고 기다리게 해도 항복하고 나갈 수 있다.
                if (number == 3)
                    AskSurrender();
                else
                    Notification = "상대방의 행동을 기다리는 중입니다.";
                break;
        }
    }

    private void AskSurrender()
    {
        menuBeforeSurrender = menu;
        menu = BattleMenu.Surrender;
    }

    // 결과 화면: 1. 재도전 2. 나가기
    private async Task HandleResultInput(string input, StreamWriter writer)
    {
        switch (input)
        {
            case "1":
                await writer.WriteLineAsync("REMATCH");
                break;
            case "2":
                await writer.WriteLineAsync("LEAVE");
                break;
            default:
                Notification = "1. 재도전 2. 나가기 중에서 선택하세요.";
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
