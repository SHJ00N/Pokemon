using System.Globalization;
using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class InGameState : ClientState
{
    private const int TeamSize = 3;
    private static readonly string TeamSelectBgm = Path.Combine("resources", "sounds", "Team Select Music.mp3");
    private static readonly string BattleBgm = Path.Combine("resources", "sounds", "Battle Music.mp3");
    private static readonly string CriticalHealthBgm = Path.Combine("resources", "sounds", "Critical Health Music.mp3");
    private static readonly string VictoryBgm = Path.Combine("resources", "sounds", "Victory.mp3");

    public override string? BgmPath => result switch
    {
        "WIN" => VictoryBgm,
        null => battle == null ? TeamSelectBgm : BattleBgm,
        _ => null
    };
    public override string? BgmOverridePath
    {
        get
        {
            if (battle == null || result != null || withdrawingPlayer) return null;
            PokemonView player = battle.Team[battle.Active].Pokemon;
            return player.CurrentHp > 0 && (long)player.CurrentHp * 5 <= player.MaxHp ? CriticalHealthBgm : null;
        }
    }

    // 전투 중 입력 단계
    // MoveSwitch: 유턴·볼트체인지·막말내뱉기·배턴터치를 쓴 뒤 턴 도중에 교체할 포켓몬을 고른다(CHOOSE_SWITCH_MIDTURN).
    private enum BattleMenu { Waiting, Main, Fight, Switch, ForcedSwitch, MoveSwitch, Surrender }

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
    private bool withdrawingPlayer;
    private BattleMenu menu = BattleMenu.Waiting;
    private BattleMenu menuBeforeWaiting = BattleMenu.Main;  // 서버가 선택을 거절하면 돌아갈 단계
    private BattleMenu menuBeforeSurrender = BattleMenu.Main; // 항복 확인에서 뒤로 가면 돌아갈 단계
    private string? result;                                   // "WIN", "LOSE" 또는 "DRAW"
    private readonly List<string> battleLog = new();
    private readonly BattleAnimationManager animations;

    // 선택 제한시간 (서버 RoomManager.Timer.cs와 같은 값). 서버는 남은 시간을 보내지 않으므로
    // 안내(포켓몬 목록, CHOOSE_ACTION 등)를 받은 순간부터 직접 센다. 시간 초과 판정은 서버가 한다.
    private const int TeamTimeLimitSeconds = 300, ActionTimeLimitSeconds = 60;
    private long? deadline;         // Environment.TickCount64 기준 마감 시각(ms). null이면 제한시간 없음
    private int? shownSeconds;      // 화면에 마지막으로 그린 남은 초

    private int? RemainingSeconds => deadline is long end && result == null
        ? (int)Math.Max(0, (end - Environment.TickCount64 + 999) / 1000) : null;

    private void StartTimer(int seconds, long? startedAt = null) =>
        deadline = (startedAt ?? Environment.TickCount64) + seconds * 1000L;

    // 재도전하면 서버가 포켓몬 목록을 다시 보내지 않으므로 이전 상태의 목록을 넘겨받는다.
    public InGameState(PokemonCatalogResponse? catalog = null)
    {
        this.catalog = catalog;
        animations = new BattleAnimationManager(SoundManager.Instance);
        if (catalog != null)
            StartTimer(TeamTimeLimitSeconds);   // 재도전: 포켓몬 선택 제한시간이 다시 시작된다.
    }

    public override void ShowMenu()
    {
        if (battle != null && result == null && animations.HoldInformation) return;
        shownSeconds = RemainingSeconds;
        // 출력 스레드에는 이후 입력·서버 메시지로 바뀌지 않는 시점의 값만 전달한다.
        var view = (Catalog: catalog, Names: pickedPokemon.Select(InGameRenderer.DisplayName).ToArray(),
            Target: MoveTarget, Moves: selectedMoves.ToArray(), Submitted: submitted, Battle: battle,
            Notice: Notification, Log: battleLog.ToArray(), Prompt: GetPrompt(),
            Switches: menu is BattleMenu.Switch or BattleMenu.ForcedSwitch or BattleMenu.MoveSwitch ? GetSwitchCandidates() : null,
            Back: menu is BattleMenu.Fight or BattleMenu.Switch,
            Result: result, Seconds: shownSeconds);
        ConsoleRenderThreads.Information(() => InGameRenderer.Draw(
            view.Catalog, view.Names, view.Target, view.Moves, view.Submitted, view.Battle, view.Notice,
            view.Log, view.Prompt, view.Switches, view.Back, view.Result, view.Seconds));
        if (view.Battle != null && view.Result == null)
            QueueBattleFrame(view.Battle, Environment.TickCount64);
    }

    public override void UpdateTimer()
    {
        if (animations.IsBusy) return;
        if (RemainingSeconds is int seconds && seconds != shownSeconds)
        {
            shownSeconds = seconds;
            ConsoleRenderThreads.Information(() => InGameRenderer.UpdateTimer(seconds));
        }
    }

    private string GetPrompt() => animations.IsSwitching ? "포켓몬을 교체하는 중입니다..."
        : animations.IsSummoning ? "포켓몬을 소환하는 중입니다..." : menu switch
    {
        BattleMenu.Main => "행동을 선택하세요. 1. 싸운다 2. 교체 3. 항복: ",
        BattleMenu.Fight => "공격할 기술의 번호를 선택하세요(0 : 취소): ",
        BattleMenu.Switch => "교체할 포켓몬의 번호를 선택하세요(0 : 취소): ",
        BattleMenu.ForcedSwitch => "포켓몬이 쓰러졌습니다. 내보낼 포켓몬 번호: ",
        BattleMenu.MoveSwitch => "기술 효과로 교체합니다. 내보낼 포켓몬 번호: ",
        BattleMenu.Surrender => "항복하면 패배합니다. 1. 항복 0. 뒤로: ",
        _ => "상대방의 행동을 기다리고 있습니다. (3. 항복)"
    };

    // 교체할 수 있는 팀 번호(0부터): 출전 중이 아니고 쓰러지지 않은 포켓몬
    private List<int> GetSwitchCandidates() => battle == null ? new() : Enumerable.Range(0, battle.Team.Length)
        .Where(index => index != battle.Active && battle.Team[index].Pokemon.CurrentHp > 0).ToList();

    public override ClientState HandleServerMessage(string message)
    {
        long receivedAt = Environment.TickCount64;
        // 뒤따르는 HP·교체·승패를 먼저 표시하면 공격 연출이 중간에 사라지므로 순서대로 보관한다.
        if (battle != null &&
            (message.StartsWith("BATTLE_") ||
             message is "CHOOSE_ACTION" or "CHOOSE_SWITCH" or "CHOOSE_SWITCH_MIDTURN" or "WAIT_SWITCH" or "LOCKED"))
        {
            bool startsAnimation = message.StartsWith("BATTLE_EFFECT ") || message.StartsWith("BATTLE_STATUS ") ||
                message.StartsWith("BATTLE_STAT ") || message.StartsWith("BATTLE_BUFF ") || message.StartsWith("BATTLE_HEAL ");
            animations.ExecuteOrQueue(() => ApplyServerMessage(message, receivedAt), startsAnimation);
            return this;
        }
        return ApplyServerMessage(message, receivedAt);
    }

    private ClientState ApplyServerMessage(string message, long receivedAt)
    {
        if (message.StartsWith("POKEMON_CATALOG "))
        {
            catalog = CatalogCodec.Decode(message[16..]);   // Base64 → Brotli 압축 풀기 → JSON
            Notification = null;
            StartTimer(TeamTimeLimitSeconds);
            return this;
        }
        if (message.StartsWith("BATTLE_STATE "))
        {
            var nextBattle = JsonSerializer.Deserialize<BattleView>(message[13..])
                ?? throw new InvalidDataException("전투 화면 정보를 해석할 수 없습니다.");
            if (battle == null)
            {
                Notification = "양쪽 플레이어의 포켓몬 선택이 완료되었습니다.";
                deadline = null;    // 포켓몬 선택 제한시간 끝. 행동 선택 제한시간은 CHOOSE_ACTION부터 센다.
                animations.StartSummon();
            }
            else
            {
                PokemonView previousPlayer = battle.Team[battle.Active].Pokemon;
                PokemonView nextPlayer = nextBattle.Team[nextBattle.Active].Pokemon;
                bool playerChanged = battle.Active != nextBattle.Active || previousPlayer.SpeciesId != nextPlayer.SpeciesId;
                bool opponentChanged = battle.Opponent.SpeciesId != nextBattle.Opponent.SpeciesId;
                if (playerChanged || opponentChanged)
                {
                    // 퇴장 중에는 위기 음악을 끄고, 새 포켓몬이 등장할 때 그 HP로 다시 판정한다.
                    withdrawingPlayer = playerChanged;
                    animations.PlaySwitch(playerChanged, opponentChanged, () => ApplyBattleSnapshot(nextBattle),
                        recallPlayer: previousPlayer.CurrentHp > 0, recallOpponent: battle.Opponent.CurrentHp > 0);
                    return this;
                }
            }
            ApplyBattleSnapshot(nextBattle);
            return this;
        }
        if (message.StartsWith("BATTLE_EFFECT "))
        {
            string[] effect = message.Split(' ', 6);
            if (effect.Length >= 5 && int.TryParse(effect[1], out int typeId))
            {
                double effectiveness = effect.Length == 6 && double.TryParse(effect[5],
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier) ? multiplier : 1;
                animations.PlayAttack(new TypeView(typeId, effect[3], effect[2]), effect[4] == "SELF", effectiveness);
            }
            return this;
        }
        if (message.StartsWith("BATTLE_STATUS "))
        {
            string[] effect = message.Split(' ');
            if (effect.Length == 3 && effect[2] is "SELF" or "OPPONENT")
                animations.PlayStatus(effect[1], effect[2] == "SELF");
            return this;
        }
        if (message.StartsWith("BATTLE_STAT "))
        {
            string[] effect = message.Split(' ');
            if (effect.Length == 4 && int.TryParse(effect[1], out int typeId) && typeId is >= 1 and <= 18 &&
                effect[2] is "UP" or "DOWN" && effect[3] is "SELF" or "OPPONENT")
                animations.PlayStat(typeId, effect[3] == "SELF", effect[2] == "UP");
            return this;
        }
        if (message.StartsWith("BATTLE_BUFF "))
        {
            string[] effect = message.Split(' ');
            if (effect.Length == 3 && int.TryParse(effect[1], out int typeId) && typeId is >= 1 and <= 18 &&
                effect[2] is "SELF" or "OPPONENT")
                animations.PlayStat(typeId, effect[2] == "SELF", raised: true);
            return this;
        }
        if (message is "BATTLE_HEAL SELF" or "BATTLE_HEAL OPPONENT")
        {
            animations.PlayHealing(message == "BATTLE_HEAL SELF");
            return this;
        }
        if (message.StartsWith("BATTLE_LOG "))
        {
            AddLog(message[11..]);
            return this;
        }
        if (message is "CHOOSE_ACTION" or "CHOOSE_SWITCH" or "CHOOSE_SWITCH_MIDTURN" or "WAIT_SWITCH" or "LOCKED")
        {
            menu = message switch
            {
                "CHOOSE_ACTION" => BattleMenu.Main,
                "CHOOSE_SWITCH" => BattleMenu.ForcedSwitch,
                "CHOOSE_SWITCH_MIDTURN" => BattleMenu.MoveSwitch,
                _ => BattleMenu.Waiting
            };
            if (message == "LOCKED")
                Notification = "이번 턴은 행동이 정해져 있어서 고를 수 없습니다.";   // 난동, 2턴 기술의 공격 턴, 반동으로 쉬는 턴
            // 내가 고르는 중이 아니면(WAIT_SWITCH, LOCKED) 상대가 고르는 데 남은 시간이다.
            StartTimer(ActionTimeLimitSeconds, receivedAt);
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

    public override void OnFrame()
    {
        if (battle == null || result != null) return;
        long now = Environment.TickCount64;
        if (!animations.IsFrameDue(now)) return;
        if (animations.Update(now)) ShowMenu();
        if (result == null) QueueBattleFrame(battle, now);
    }

    private void QueueBattleFrame(BattleView view, long now)
    {
        BattleAnimationFrame frame = animations.CaptureFrame(now,
            playerAlive: view.Team[view.Active].Pokemon.CurrentHp > 0,
            opponentAlive: view.Opponent.CurrentHp > 0);
        ConsoleRenderThreads.Sprites(() => InGameRenderer.DrawBattleFrame(view, frame));
    }

    private void ApplyHealthEffects(BattleView next)
    {
        if (battle == null) return;

        PokemonView player = next.Team[next.Active].Pokemon;
        PokemonView previousPlayer = battle.Team[next.Active].Pokemon;
        bool playerFainted = previousPlayer.CurrentHp > 0 && player.CurrentHp == 0;
        bool opponentFainted = battle.Opponent.PokemonId == next.Opponent.PokemonId &&
            battle.Opponent.CurrentHp > 0 && next.Opponent.CurrentHp == 0;
        if (battle.Active != next.Active || (previousPlayer.CurrentHp == 0 && player.CurrentHp > 0))
            animations.ResetFaint(onPlayer: true);
        if (battle.Opponent.PokemonId != next.Opponent.PokemonId ||
            (battle.Opponent.CurrentHp == 0 && next.Opponent.CurrentHp > 0))
            animations.ResetFaint(onPlayer: false);
        if (playerFainted || opponentFainted)
        {
            // 교체 메뉴가 열려 있었더라도 사라지는 연출과 다음 안내를 마칠 때까지 대기한다.
            menu = BattleMenu.Waiting;
            animations.PlayFaint(playerFainted ? InGameRenderer.GetSpriteRows(player) : null,
                opponentFainted ? InGameRenderer.GetSpriteRows(next.Opponent) : null);
        }

    }

    private void ApplyBattleSnapshot(BattleView next)
    {
        ApplyHealthEffects(next);
        battle = next;
        withdrawingPlayer = false;
    }

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        if (animations.IsBusy && result == null) return true;
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
                Notification = "목록에 있는 포켓몬 번호를 입력하세요.";
            else if (pickedPokemon.Any(p => p.SpeciesId == pokemon.SpeciesId))
                Notification = "이미 팀에 있는 포켓몬입니다. 같은 포켓몬은 폼이 달라도 한 마리만 넣을 수 있습니다.";
            else if (pokemon.LearnableMoveIds.Length < 4)
                Notification = "서로 다른 기술 4개를 배울 수 있는 포켓몬을 선택하세요.";
            else
                pickedPokemon.Add(pokemon);
        }
        else if (id == 0)
        {
            int[] moveIds = target.LearnableMoveIds.Distinct().ToArray();
            Random.Shared.Shuffle(moveIds);
            selectedMoves.Clear();
            selectedMoves.AddRange(moveIds.Take(4));
            var movesById = catalog.Moves.ToDictionary(move => move.MoveId);
            Notification = $"{InGameRenderer.DisplayName(target)}의 무작위 기술: " +
                string.Join(" / ", selectedMoves.Select(moveId => movesById[moveId].Name));
            await CompleteMoveSelectionAsync(target, writer);
        }
        else if (!target.LearnableMoveIds.Contains(id))
            Notification = "선택한 포켓몬이 배울 수 있는 기술 번호를 입력하세요.";
        else if (selectedMoves.Contains(id))
            Notification = "이미 선택한 기술입니다. 다른 기술을 선택하세요.";
        else
        {
            selectedMoves.Add(id);
            if (selectedMoves.Count == 4)
                await CompleteMoveSelectionAsync(target, writer);
        }
        return true;
    }

    private async Task CompleteMoveSelectionAsync(PokemonCatalogEntry pokemon, StreamWriter writer)
    {
        team.Add(new PokemonSelection(pokemon.PokemonId, selectedMoves.ToArray()));
        selectedMoves.Clear();
        if (team.Count == TeamSize)
        {
            await writer.WriteLineAsync("SELECT_TEAM " + JsonSerializer.Serialize(team));
            submitted = true;
        }
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
                    Notification = $"기술 번호 1~{moveCount}를 입력하세요.";
                else
                {
                    await writer.WriteLineAsync($"USE_MOVE {number}");
                    WaitForOpponent();
                }
                break;

            case BattleMenu.Switch:
            case BattleMenu.ForcedSwitch:
            case BattleMenu.MoveSwitch:
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
        menuBeforeWaiting = menu is BattleMenu.ForcedSwitch or BattleMenu.MoveSwitch ? menu : BattleMenu.Main;
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
