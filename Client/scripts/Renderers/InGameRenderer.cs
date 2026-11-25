using Pokemon.Protocol;

namespace Client;

static class InGameRenderer
{

    private static readonly BattleSpriteBuffer spriteBuffer = new();
    private static (int Width, int Height, int StatusY)? battleLayout;
    private const int BattleInformationRows = 26;

    // 목록에 표시하는 이름. 같은 종의 다른 폼과 구분해야 하면 폼 이름을 붙인다. 예: 라이츄 (알로라의 모습)
    public static string DisplayName(PokemonCatalogEntry entry) =>
        entry.FormName == null ? entry.Name : $"{entry.Name} ({entry.FormName})";

    // 남은 시간은 입력 줄 맨 앞(이 열)에 그린다. 그리지 않은 화면이면 null
    private static int? timerColumn;

    // 남은 시간 표시. 매초 같은 폭으로 고쳐 쓸 수 있게 폭을 고정한다.
    private static string FormatTimer(int seconds) =>
        TextRenderer.FitToWidth(seconds > 0 ? $"[남은 시간 {seconds,3}초]" : "[시간 초과]", 17) + " ";

    // 화면 전체를 다시 그리지 않고 입력 줄의 남은 시간만 고쳐 쓴다.
    // 커서 위치를 저장(ESC 7)하고 줄 맨 앞으로 가서 쓴 뒤 되돌리므로(ESC 8), 입력 중인 글자는 그대로 남는다.
    public static void UpdateTimer(int seconds)
    {
        if (timerColumn is not int column)
            return;
        string moveRight = column > 0 ? $"\x1b[{column}C" : "";
        // "\x1b7"은 C#에서 \x가 16진수를 최대 4자리까지 읽어 'Ʒ'(U+01B7) 한 글자가 된다. \u는 정확히 4자리만 읽는다.
        Console.Write($"\u001b7\r{moveRight}{FormatTimer(seconds)}\u001b8");
    }


    // switchList: 교체 메뉴일 때 고를 수 있는 팀 번호(0부터). null이면 기술 목록을 표시한다.
    // timerSeconds: 선택 제한시간의 남은 초. null이면 표시하지 않는다.
    public static void Draw(PokemonCatalogResponse? catalog,
        (PokemonCatalogEntry[] Entries, int? Generation, string? Type, bool MegaOnly) pokemonList, IReadOnlyList<string> teamNames,
        PokemonCatalogEntry? selectedPokemon, IReadOnlyList<int> selectedMoves, bool submitted,
        BattleView? battle, string? notification, IReadOnlyList<string> battleLog, string prompt,
        IReadOnlyList<int>? switchList, bool showBack, bool choosingMegaForm, MegaFormView? selectedMegaForm,
        string? result, int? timerSeconds)
    {
        timerColumn = null;
        if (battle != null && result == null)
        {
            DrawBattle(battle, notification, battleLog, prompt, switchList, showBack, choosingMegaForm, selectedMegaForm, timerSeconds);
            return;
        }
        TextRenderer.Clear();
        battleLayout = null;
        spriteBuffer.Reset();
        if (result != null)
        {
            DrawResult(result, notification);
            return;
        }

        TextRenderer.WriteLine("[게임 중] 포켓몬과 기술 선택");
        if (catalog == null)
            TextRenderer.WriteLine("서버에서 포켓몬 목록을 받는 중입니다.");
        else if (submitted)
        {
            TextRenderer.WriteLine("선택한 팀: " + string.Join(" / ", teamNames));
            TextRenderer.WriteLine("상대방의 선택을 기다립니다.");
        }
        else if (selectedPokemon == null)
        {
            DrawPokemonCatalog(pokemonList.Entries);
            if (teamNames.Count > 0)
                TextRenderer.WriteLine("선택한 팀: " + string.Join(" / ", teamNames));
            string generation = pokemonList.Generation is int value ? $"{value}세대" : "전체";
            string mega = pokemonList.MegaOnly ? "가능한 포켓몬만" : "전체";
            TextRenderer.WriteLine($"필터  세대: {generation} / 타입: {pokemonList.Type ?? "전체"} / 메가진화: {mega}");
            TextRenderer.WriteLine("메가진화 필터: 메가진화 / 세대·타입과 함께: 세대 3 타입 물 메가진화");
            TextRenderer.WriteLine("필터 해제: 세대 0 / 타입 0 / 메가진화 0");
            TextRenderer.WriteLine($"{teamNames.Count + 1}번째 포켓몬의 번호나 이름을 입력하세요.");
        }
        else
        {
            var movesById = catalog.Moves.ToDictionary(move => move.MoveId);
            DrawMoveCatalog(selectedPokemon, selectedPokemon.LearnableMoveIds.Select(id => movesById[id]).ToArray());
            TextRenderer.WriteLine("선택한 팀: " + string.Join(" / ", teamNames));
            TextRenderer.WriteLine("선택한 기술: " + string.Join(" / ", selectedMoves.Select(id => movesById[id].Name)));
            int requiredMoves = PokemonSelection.RequiredMoveCount(selectedPokemon.LearnableMoveIds.Length);
            bool chooseAll = requiredMoves < PokemonSelection.MaxMoves;
            TextRenderer.WriteLine(chooseAll ? "0. 배울 수 있는 기술 모두 선택 후 확정"
                : $"0. 기술 {requiredMoves}개 무작위 선택 후 확정 (현재 선택한 기술을 대체)");
            // 팀에 같은 종이 없으므로 이름으로 몇 번째인지 찾는다.
            int order = teamNames.ToList().IndexOf(DisplayName(selectedPokemon)) + 1;
            TextRenderer.Write($"{order}번째 포켓몬 ");
            TypeColorRenderer.WriteColored(DisplayName(selectedPokemon), SelectedPokemonColor);
            string instruction = chooseAll ? $"배울 수 있는 기술 {requiredMoves}개 모두" : $"서로 다른 기술 {requiredMoves}개";
            TextRenderer.WriteLine($"의 {selectedMoves.Count + 1}번 기술 번호를 입력하세요 ({instruction}).");
        }
        if (notification != null) TextRenderer.WriteLine("\n" + notification);
        // 마지막 줄이 입력 줄이다. 남은 시간 뒤에서 입력한다.
        if (catalog != null && timerSeconds is int seconds)
        {
            TextRenderer.Write(FormatTimer(seconds) + (submitted ? "" : "> "));
            timerColumn = 0;
        }
    }

    private static void DrawPokemonCatalog(IReadOnlyList<PokemonCatalogEntry> pokemon)
    {
        TextRenderer.WriteLine("포켓몬 목록");
        if (pokemon.Count == 0)
            TextRenderer.WriteLine("필터에 맞는 포켓몬이 없습니다.");
        foreach (PokemonCatalogEntry entry in pokemon)
        {
            TextRenderer.Write($"{entry.PokemonId,5}. {TextRenderer.FitToWidth(DisplayName(entry), 32)}");
            foreach (TypeView type in entry.Types)
            {
                TextRenderer.Write(" ");
                TypeColorRenderer.WriteMarker(type.ColorRgb);
                TextRenderer.Write(TextRenderer.FitToWidth(type.Name, 6));
            }
            TextRenderer.WriteLine();
        }
    }

    // 기술 선택 화면에서 지금 기술을 고르는 포켓몬 이름 색
    private const string SelectedPokemonColor = "#FFD43B";

    private static void DrawMoveCatalog(PokemonCatalogEntry pokemon, IReadOnlyList<MoveView> moves)
    {
        const string nameHeader = "기술 이름";
        int nameWidth = moves.Select(move => TextRenderer.DisplayWidth(move.Name))
            .Prepend(TextRenderer.DisplayWidth(nameHeader)).Max();
        TypeColorRenderer.WriteColored(DisplayName(pokemon), SelectedPokemonColor);
        // 조사는 마지막 한글(폼 이름이 있으면 폼 이름)에 맞춘다.
        TextRenderer.WriteLine($"{TextRenderer.Josa(pokemon.FormName ?? pokemon.Name, "이", "가")} 배울 수 있는 기술 목록");
        TextRenderer.WriteLine($"번호 타입    {TextRenderer.FitToWidth(nameHeader, nameWidth)} 위력 명중률   PP");
        foreach (MoveView move in moves)
        {
            TextRenderer.Write($"{move.MoveId,3}. ");
            TypeColorRenderer.WriteMarker(move.Type.ColorRgb);
            TextRenderer.Write(TextRenderer.FitToWidth(move.Type.Name, 6));
            TextRenderer.WriteLine($" {TextRenderer.FitToWidth(move.Name, nameWidth)} {move.Power?.ToString() ?? "-",4} {move.Accuracy?.ToString() ?? "-",6} {move.MaxPp?.ToString() ?? "-",4}");
        }
    }

    private static void DrawBattle(BattleView battle, string? notification, IReadOnlyList<string> battleLog,
        string prompt, IReadOnlyList<int>? switchList, bool showBack, bool choosingMegaForm,
        MegaFormView? selectedMegaForm, int? timerSeconds)
    {
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        TeamMemberView active = battle.Team[battle.Active];
        int leftX = 2;
        int columns = Math.Max(1, width / 2 - 4);
        int maxRows = Math.Max(1, height - BattleInformationRows);
        string[] player = PokemonRenderer.Load(active.Pokemon, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent, maxRows, columns, flip: false);
        int statusY = GetSpritePositions(player.Length, opponent.Length).Bottom + 2;
        var layout = (width, height, statusY);
        if (battleLayout != layout)
        {
            TextRenderer.Clear();
            spriteBuffer.Reset();
            battleLayout = layout;
        }
        // HP·메뉴 갱신이 포켓몬 그림 영역을 지우지 않게 한다.
        var clearInformation = new System.Text.StringBuilder("\x1b[0m");
        for (int row = statusY; row < height; row++)
            clearInformation.Append($"\x1b[{row + 1};1H\x1b[2K");
        Console.Write(clearInformation.ToString());
        // 맨 윗줄: 날씨·필드·공간 효과와 남은 턴. 포켓몬 그림은 둘째 줄 아래에 그리므로 겹치지 않는다.
        TextRenderer.At(leftX, 0, TextRenderer.FitToWidth(string.Join("  ", battle.FieldEffects ?? []), width - 4));
        var panels = BattlePanelRenderer.Arrange(battle, width, statusY, switchList, showBack, choosingMegaForm, selectedMegaForm);
        foreach (var panel in panels)
            BattlePanelRenderer.Draw(panel.Lines, panel.X, panel.Y);

        int logY = panels.Max(panel => panel.Y + panel.Lines.Length) + 1;
        for (int index = 0; index < battleLog.Count; index++)
            TextRenderer.At(leftX, logY + index, TextRenderer.FitToWidth(battleLog[index], width - 4));
        if (notification != null)
            TextRenderer.At(leftX, logY + 6, TextRenderer.FitToWidth(notification, width - 4));
        // 입력 커서가 안내 문구 바로 뒤에 오도록 마지막에 출력한다. 남은 시간은 그 줄 맨 앞에 둔다.
        TextRenderer.At(leftX, logY + 7, (timerSeconds is int seconds ? FormatTimer(seconds) : "") + prompt);
        if (timerSeconds != null)
            timerColumn = 2;
    }

    internal static int GetSpriteRows(PokemonView pokemon) => PokemonRenderer.Load(
        pokemon, Math.Max(1, Console.BufferHeight - BattleInformationRows),
        Math.Max(1, Console.BufferWidth / 2 - 4), flip: false).Length;

    // Animation frames update only the sprite area, leaving menus and logs steady.
    public static void DrawBattleFrame(BattleView battle, BattleAnimationFrame frame, long now)
    {
        var (playerJumpOffset, opponentJumpOffset, flashPlayer, flashOpponent,
            attack, playerFaintProgress, opponentFaintProgress,
            statusEffect, healing, summon, withdraw, megaEvolution) = frame;
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        int columns = Math.Max(1, width / 2 - 4);
        int maxRows = Math.Max(1, height - BattleInformationRows);
        string[] player = PokemonRenderer.Load(battle.Team[battle.Active].Pokemon, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent, maxRows, columns, flip: false);
        int rightX = width - Math.Max(BattlePanelRenderer.Width,
            PokemonRenderer.GetHorizontalBounds(opponent).MaxX + 1) - 2;
        var positions = GetSpritePositions(player.Length, opponent.Length);
        int playerY = positions.PlayerY + playerJumpOffset;
        int opponentY = positions.OpponentY + opponentJumpOffset;

        spriteBuffer.BeginFrame();
        BattleWeatherRenderer.Draw(battle.Weather, now, width - 1, 2, positions.Bottom, spriteBuffer);
        BattleTerrainRenderer.Draw(battle.Terrain, now, width - 1, 2, positions.Bottom, spriteBuffer);
        if (withdraw is { } exit)
            DrawWithdrawFrame(spriteBuffer, player, opponent, rightX, width, exit, playerFaintProgress, opponentFaintProgress);
        else if (summon is { } entry)
            DrawSummonFrame(spriteBuffer, player, opponent, rightX, width, entry, playerFaintProgress, opponentFaintProgress);
        else
        {
            DrawPokemonSprites(spriteBuffer, player, opponent, rightX, playerJumpOffset, opponentJumpOffset,
                flashPlayer || megaEvolution is { OnPlayer: true, HidePokemon: true },
                flashOpponent || megaEvolution is { OnPlayer: false, HidePokemon: true },
                playerFaintProgress, opponentFaintProgress);
        }
        if (megaEvolution is { ImageIndex: > 0 } mega)
        {
            string[] target = mega.OnPlayer ? player : opponent;
            var (minX, maxX) = PokemonRenderer.GetHorizontalBounds(target);
            int centerX = (mega.OnPlayer ? 2 : rightX) + (minX + maxX + 1) / 2;
            int centerY = (mega.OnPlayer ? playerY : opponentY) + target.Length / 2;
            foreach (var cell in AttackImageRenderer.GetMegaEvolutionCells(mega.ImageIndex,
                centerX, centerY, flip: !mega.OnPlayer, width, 2, positions.Bottom))
                spriteBuffer.SetPixel(cell.X, cell.Y, cell.Ansi, cell.Character);
        }
        if (attack is { } attackFrame)
        {
            var (playerMin, playerMax) = PokemonRenderer.GetHorizontalBounds(player);
            var (opponentMin, opponentMax) = PokemonRenderer.GetHorizontalBounds(opponent);
            AttackImageRenderer.DrawAttackFrame(attackFrame,
                (2 + (playerMin + playerMax) / 2, playerY + player.Length / 2),
                (rightX + (opponentMin + opponentMax) / 2, opponentY + opponent.Length / 2),
                width, 2, positions.Bottom, spriteBuffer);
        }
        if (statusEffect is { } status)
        {
            string[] target = status.OnPlayer ? player : opponent;
            int targetX = status.OnPlayer ? 2 : rightX;
            int targetY = status.OnPlayer ? playerY : opponentY;
            DrawStatusOverlay(spriteBuffer, status, target, targetX, targetY, width);
        }
        if (healing is { } healFrame)
        {
            string[] target = healFrame.OnPlayer ? player : opponent;
            int targetX = healFrame.OnPlayer ? 2 : rightX;
            int targetY = healFrame.OnPlayer ? playerY : opponentY;
            DrawHealingOverlay(spriteBuffer, healFrame.ImageIndex, target, targetX, targetY, width);
        }
        spriteBuffer.Flush(width, height);
    }

    // 두 포켓몬의 발밑을 정보 상자 바로 위에 맞추고, 키가 작은 쪽을 아래로 내린다.
    internal static (int PlayerY, int OpponentY, int Bottom) GetSpritePositions(int playerRows, int opponentRows)
    {
        int bottom = 2 + Math.Max(playerRows, opponentRows);
        return (bottom - playerRows, bottom - opponentRows, bottom);
    }

    internal static void DrawPokemonSprites(BattleSpriteBuffer buffer, string[] player, string[] opponent,
        int rightX, int playerJumpOffset = 0, int opponentJumpOffset = 0,
        bool hidePlayer = false, bool hideOpponent = false,
        double playerFaintProgress = 0, double opponentFaintProgress = 0,
        int playerHorizontalOffset = 0, int opponentHorizontalOffset = 0)
    {
        var positions = GetSpritePositions(player.Length, opponent.Length);
        buffer.AddImage(player, 2 + playerHorizontalOffset, positions.PlayerY + playerJumpOffset, hidePlayer,
            (int)Math.Round(Math.Clamp(playerFaintProgress, 0, 1) * player.Length));
        buffer.AddImage(opponent, rightX + opponentHorizontalOffset, positions.OpponentY + opponentJumpOffset, hideOpponent,
            (int)Math.Round(Math.Clamp(opponentFaintProgress, 0, 1) * opponent.Length));
    }

    internal static void DrawSummonFrame(BattleSpriteBuffer buffer, string[] player, string[] opponent,
        int rightX, int width, SummonAnimation.Frame frame,
        double playerFaintProgress = 0, double opponentFaintProgress = 0)
    {
        DrawPokemonSprites(buffer, player, opponent, rightX,
            hidePlayer: !frame.ShowPlayer, hideOpponent: !frame.ShowOpponent,
            playerFaintProgress: playerFaintProgress, opponentFaintProgress: opponentFaintProgress);
        if (frame.ThrowOnPlayer is not bool onPlayer || width < 3) return;

        var positions = GetSpritePositions(player.Length, opponent.Length);
        string[] target = onPlayer ? player : opponent;
        var (minX, maxX) = PokemonRenderer.GetHorizontalBounds(target);
        int targetX = (onPlayer ? 2 : rightX) + (minX + maxX + 1) / 2;
        int ballRows = Math.Max(1, Math.Min(12, Math.Min(target.Length / 2, (width - 2) / 8)));
        var position = GetSummonBallPosition(onPlayer, frame.ThrowProgress, targetX,
            width, 2, positions.Bottom, ballRows);
        AttackImageRenderer.DrawPokeball(position.X, position.Y, flip: !onPlayer,
            width, 2, positions.Bottom, ballRows, buffer);
    }

    internal static void DrawWithdrawFrame(BattleSpriteBuffer buffer, string[] player, string[] opponent,
        int rightX, int width, WithdrawAnimation.Frame frame,
        double playerFaintProgress = 0, double opponentFaintProgress = 0)
    {
        double progress = Math.Clamp(frame.Progress, 0, 1);
        int playerDistance = 2 + PokemonRenderer.GetHorizontalBounds(player).MaxX + 1;
        int opponentDistance = Math.Max(0, width - 1 - rightX - PokemonRenderer.GetHorizontalBounds(opponent).MinX);
        DrawPokemonSprites(buffer, player, opponent, rightX,
            playerFaintProgress: playerFaintProgress, opponentFaintProgress: opponentFaintProgress,
            playerHorizontalOffset: frame.OnPlayer ? -(int)Math.Round(playerDistance * progress) : 0,
            opponentHorizontalOffset: frame.OnOpponent ? (int)Math.Round(opponentDistance * progress) : 0);
    }

    // 내 볼은 왼쪽 바깥에서 오른쪽으로, 상대 볼은 대칭 방향으로 날아와 포켓몬의 발치에 도착한다.
    internal static (int X, int Y) GetSummonBallPosition(bool onPlayer, double progress,
        int targetX, int width, int top, int bottom, int ballRows)
    {
        double t = Math.Clamp(progress, 0, 1);
        int startX = onPlayer ? -ballRows : width - 1 + ballRows;
        int minY = top + ballRows / 2;
        int endY = Math.Max(minY, bottom - (ballRows + 1) / 2);
        double space = endY - minY;
        double startY = minY + space * 0.35;
        double y = startY + (endY - startY) * t - 4 * space * 0.6 * t * (1 - t);
        return ((int)Math.Round(startX + (targetX - startX) * t), (int)Math.Round(y));
    }

    internal static void DrawStatusOverlay(BattleSpriteBuffer buffer, StatusAnimation.Effect effect,
        string[] target, int x, int y, int width)
    {
        var (minX, maxX) = PokemonRenderer.GetHorizontalBounds(target);
        AttackImageRenderer.Draw(effect.TypeId, x + (minX + maxX + 1) / 2,
            y + target.Length / 2, flip: !effect.OnPlayer, width, y, y + target.Length, buffer);
    }

    internal static void DrawHealingOverlay(BattleSpriteBuffer buffer, int imageIndex, string[] target, int x, int y, int width)
    {
        var (minX, maxX) = PokemonRenderer.GetHorizontalBounds(target);
        AttackImageRenderer.DrawHeal(imageIndex, x + (minX + maxX + 1) / 2,
            y + target.Length / 2, width, y, y + target.Length, buffer);
    }

    private static void DrawResult(string result, string? notification)
    {
        int width = Console.BufferWidth, height = Console.BufferHeight;
        var cells = AttackImageRenderer.GetResultCells(result, width, 1, Math.Max(2, height - 6)).ToArray();
        int menuY = 3;
        if (cells.Length > 0)
        {
            var buffer = new BattleSpriteBuffer();
            foreach (var cell in cells)
                buffer.SetPixel(cell.X, cell.Y, cell.Ansi, cell.Character);
            TextRenderer.Write(buffer.BuildUpdate(width, height));
            menuY = cells.Max(cell => cell.Y) + 2;
        }
        else
        {
            // 무승부나 리소스가 없는 경우에도 결과와 재도전 메뉴를 표시한다.
            TextRenderer.At(2, 1, result == "DRAW" ? "DRAW" : result == "WIN" ? "Victory!" : "Loss...");
        }
        Console.SetCursorPosition(0, Math.Min(menuY, Math.Max(0, height - 4)));
        TextRenderer.WriteLine();
        TextRenderer.WriteLine("  1. 재도전  2. 나가기");
        if (notification != null)
            TextRenderer.WriteLine("\n  " + notification);
    }

}
