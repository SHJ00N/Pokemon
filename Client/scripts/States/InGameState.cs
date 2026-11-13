using System.Globalization;
using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class InGameState : ClientState
{
    private const int TeamSize = 3;

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
    private BattleMenu menu = BattleMenu.Waiting;
    private BattleMenu menuBeforeWaiting = BattleMenu.Main;  // 서버가 선택을 거절하면 돌아갈 단계
    private BattleMenu menuBeforeSurrender = BattleMenu.Main; // 항복 확인에서 뒤로 가면 돌아갈 단계
    private string? result;                                   // "WIN", "LOSE" 또는 "DRAW"
    private readonly List<string> battleLog = new();
    private long nextFrameAt;
    private const int IdleCycleMilliseconds = 800;
    private long idleStartedAt;
    private readonly BattleAnimation animation = new();
    private readonly StatusAnimation statusAnimation = new();
    private readonly HealingAnimation healingAnimation = new();
    private string? lastOverlaySound;
    private readonly FaintAnimation playerFaint = new(), opponentFaint = new();
    private bool IsAnimating => animation.IsBusy || statusAnimation.IsBusy || healingAnimation.IsBusy ||
        playerFaint.IsBusy || opponentFaint.IsBusy;
    private readonly Queue<(string Message, long ReceivedAt)> deferredBattleMessages = new();

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
        if (catalog != null)
            StartTimer(TeamTimeLimitSeconds);   // 재도전: 포켓몬 선택 제한시간이 다시 시작된다.
    }

    public override void ShowMenu()
    {
        if (battle != null && result == null && (animation.IsBusy || statusAnimation.IsBusy || healingAnimation.IsBusy)) return;
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
        {
            int offset = IdleOffset(Environment.TickCount64);
            int playerOffset = view.Battle.Team[view.Battle.Active].Pokemon.CurrentHp > 0 ? offset : 0;
            int opponentOffset = view.Battle.Opponent.CurrentHp > 0 ? offset : 0;
            double playerFaintProgress = playerFaint.Progress, opponentFaintProgress = opponentFaint.Progress;
            ConsoleRenderThreads.Sprites(() => InGameRenderer.DrawBattleFrame(
                view.Battle, playerOffset, opponentOffset, false, false, null, false, 0,
                playerFaintProgress, opponentFaintProgress));
        }
    }

    public override void UpdateTimer()
    {
        if (IsAnimating || deferredBattleMessages.Count > 0) return;
        if (RemainingSeconds is int seconds && seconds != shownSeconds)
        {
            shownSeconds = seconds;
            ConsoleRenderThreads.Information(() => InGameRenderer.UpdateTimer(seconds));
        }
    }

    private string GetPrompt() => menu switch
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
        if (battle != null && (IsAnimating || deferredBattleMessages.Count > 0) &&
            (message.StartsWith("BATTLE_") ||
             message is "CHOOSE_ACTION" or "CHOOSE_SWITCH" or "CHOOSE_SWITCH_MIDTURN" or "WAIT_SWITCH" or "LOCKED"))
        {
            deferredBattleMessages.Enqueue((message, receivedAt));
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
            if (battle == null)
            {
                Notification = "양쪽 플레이어의 포켓몬 선택이 완료되었습니다.";
                deadline = null;    // 포켓몬 선택 제한시간 끝. 행동 선택 제한시간은 CHOOSE_ACTION부터 센다.
                idleStartedAt = Environment.TickCount64;
            }
            var nextBattle = JsonSerializer.Deserialize<BattleView>(message[13..])
                ?? throw new InvalidDataException("전투 화면 정보를 해석할 수 없습니다.");
            ApplyHealthEffects(nextBattle);
            battle = nextBattle;
            return this;
        }
        if (message.StartsWith("BATTLE_EFFECT "))
        {
            string[] effect = message.Split(' ', 6);
            if (effect.Length >= 5 && int.TryParse(effect[1], out int typeId))
            {
                double effectiveness = effect.Length == 6 && double.TryParse(effect[5],
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier) ? multiplier : 1;
                animation.Enqueue(new TypeView(typeId, effect[3], effect[2]), effect[4] == "SELF", effectiveness);
            }
            return this;
        }
        if (message.StartsWith("BATTLE_STATUS "))
        {
            string[] effect = message.Split(' ');
            if (effect.Length == 3 && effect[2] is "SELF" or "OPPONENT")
                statusAnimation.Enqueue(effect[1], effect[2] == "SELF");
            return this;
        }
        if (message.StartsWith("BATTLE_STAT "))
        {
            string[] effect = message.Split(' ');
            if (effect.Length == 4 && int.TryParse(effect[1], out int typeId) && typeId is >= 1 and <= 18 &&
                effect[2] is "UP" or "DOWN" && effect[3] is "SELF" or "OPPONENT")
                statusAnimation.EnqueueStat(typeId, effect[3] == "SELF", effect[2] == "UP");
            return this;
        }
        if (message.StartsWith("BATTLE_BUFF "))
        {
            string[] effect = message.Split(' ');
            if (effect.Length == 3 && int.TryParse(effect[1], out int typeId) && typeId is >= 1 and <= 18 &&
                effect[2] is "SELF" or "OPPONENT")
                statusAnimation.EnqueueStat(typeId, effect[2] == "SELF", raised: true);
            return this;
        }
        if (message is "BATTLE_HEAL SELF" or "BATTLE_HEAL OPPONENT")
        {
            healingAnimation.Start(message == "BATTLE_HEAL SELF");
            SoundManager.Instance.PlaySoundSequence(SoundManager.HeldItemActivate, SoundManager.HealHp);
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
            SoundManager.Instance.StopSound(SoundManager.HealthLow);
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
        if (now < nextFrameAt) return;
        // 쓰러질 때의 줄 이동과 회복음 시작에 맞춘 이미지 표시를 자주 확인한다.
        nextFrameAt = now + (playerFaint.IsBusy || opponentFaint.IsBusy || healingAnimation.IsBusy ? 16 : 80);
        if (AdvanceBattleAnimation(now)) ShowMenu();
        if (result != null) return;
        if (IsAnimating || deferredBattleMessages.Count > 0) idleStartedAt = now;
        int idleOffset = IdleOffset(now);
        int playerOffset = battle.Team[battle.Active].Pokemon.CurrentHp > 0 ? idleOffset : 0;
        int opponentOffset = battle.Opponent.CurrentHp > 0 ? idleOffset : 0;
        var frame = (Battle: battle, PlayerOffset: playerOffset, OpponentOffset: opponentOffset,
            PlayerHidden: animation.FlashPlayer, OpponentHidden: animation.FlashOpponent,
            Type: animation.EffectType, FromPlayer: animation.FromPlayer, Progress: animation.Progress,
            PlayerFaintProgress: playerFaint.Progress, OpponentFaintProgress: opponentFaint.Progress,
            StatusEffect: statusAnimation.Overlay,
            HealingTarget: healingAnimation.ShowImage ? healingAnimation.OnPlayer : (bool?)null);
        ConsoleRenderThreads.Sprites(() => InGameRenderer.DrawBattleFrame(
            frame.Battle, frame.PlayerOffset, frame.OpponentOffset, frame.PlayerHidden, frame.OpponentHidden,
            frame.Type, frame.FromPlayer, frame.Progress, frame.PlayerFaintProgress, frame.OpponentFaintProgress,
            frame.StatusEffect, frame.HealingTarget));
    }

    private bool AdvanceBattleAnimation(long now)
    {
        bool wasFainting = playerFaint.IsBusy || opponentFaint.IsBusy;
        playerFaint.Update(now);
        opponentFaint.Update(now);
        // 마지막 줄이 사라진 프레임을 먼저 그린 뒤 다음 프레임에 승패·교체 안내를 반영한다.
        if (wasFainting && !playerFaint.IsBusy && !opponentFaint.IsBusy) return true;
        UpdateAnimation(now);
        bool changed = false;
        while (!IsAnimating && deferredBattleMessages.TryPeek(out var next))
        {
            // 연출 직후의 중간 HP·상태·기록을 먼저 그린 뒤 다음 연출을 시작한다.
            if (changed && (next.Message.StartsWith("BATTLE_EFFECT ") || next.Message.StartsWith("BATTLE_STATUS ") ||
                next.Message.StartsWith("BATTLE_STAT ") || next.Message.StartsWith("BATTLE_BUFF ") ||
                next.Message.StartsWith("BATTLE_HEAL "))) break;
            deferredBattleMessages.Dequeue();
            ApplyServerMessage(next.Message, next.ReceivedAt);
            changed = true;
        }
        // 새로 받은 연출만 시작한다. 중간 정보를 반영한 프레임에서는 다음 연출을 보관한다.
        UpdateAnimation(now);
        playerFaint.Update(now);
        opponentFaint.Update(now);
        return changed;
    }

    private void UpdateAnimation(long now)
    {
        if (healingAnimation.IsBusy)
            healingAnimation.Update(now, SoundManager.Instance.GetSoundState(SoundManager.HealHp));
        if (statusAnimation.Update(now) is string soundId)
        {
            // 연속된 상태·랭크 효과음이 서로 겹치지 않게 앞선 연출의 소리를 중단한다.
            if (lastOverlaySound != null && lastOverlaySound != soundId)
                SoundManager.Instance.StopSound(lastOverlaySound);
            SoundManager.Instance.StopSound(soundId);
            SoundManager.Instance.PlaySound(soundId);
            lastOverlaySound = soundId;
        }
        if (animation.Update(now) is double effectiveness)
            SoundManager.Instance.PlaySound(effectiveness < 1 ? SoundManager.HitWeak
                : effectiveness > 1 ? SoundManager.HitSuper : SoundManager.HitNormal);
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
            playerFaint.Reset();
        if (battle.Opponent.PokemonId != next.Opponent.PokemonId ||
            (battle.Opponent.CurrentHp == 0 && next.Opponent.CurrentHp > 0))
            opponentFaint.Reset();
        if (playerFainted) playerFaint.Start(InGameRenderer.GetSpriteRows(player));
        if (opponentFainted) opponentFaint.Start(InGameRenderer.GetSpriteRows(next.Opponent));
        if (playerFainted || opponentFainted)
        {
            // 교체 메뉴가 열려 있었더라도 사라지는 연출과 다음 안내를 마칠 때까지 대기한다.
            menu = BattleMenu.Waiting;
            SoundManager.Instance.PlaySound(SoundManager.Faint);
        }

        // 상대 HP만 바뀐 메시지는 내 저체력 경고음을 건드리지 않는다.
        if (battle.Active == next.Active && previousPlayer.CurrentHp == player.CurrentHp &&
            previousPlayer.MaxHp == player.MaxHp) return;
        if (!IsLowHealth(player) || battle.Active != next.Active)
            SoundManager.Instance.StopSound(SoundManager.HealthLow);
        // 살아 있는 내 출전 포켓몬이 저체력 구간에 진입할 때만 한 번 재생한다.
        if (IsLowHealth(player) && (battle.Active != next.Active || !IsLowHealth(previousPlayer)))
            SoundManager.Instance.PlaySound(SoundManager.HealthLow);
    }

    private static bool IsLowHealth(PokemonView pokemon) =>
        pokemon.CurrentHp > 0 && (long)pokemon.CurrentHp * 5 <= pokemon.MaxHp;

    // 콘솔 한 줄만 위로 올렸다가 복귀한다. 대기 없이 0.8초 주기로 반복한다.
    private int IdleOffset(long now) => IsAnimating || deferredBattleMessages.Count > 0 ? 0
        : (now - idleStartedAt) % IdleCycleMilliseconds < IdleCycleMilliseconds / 2 ? 0 : -1;

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        if ((IsAnimating || deferredBattleMessages.Count > 0) && result == null) return true;
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
