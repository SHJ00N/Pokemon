using System.Globalization;
using Pokemon.Protocol;

namespace Client;

static class InGameRenderer
{

    private static readonly BattleSpriteBuffer spriteBuffer = new();
    private static (int Width, int Height, int StatusY)? battleLayout;
    private const int BattleInformationRows = 24;

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
    public static void Draw(PokemonCatalogResponse? catalog, IReadOnlyList<string> teamNames,
        PokemonCatalogEntry? selectedPokemon, IReadOnlyList<int> selectedMoves, bool submitted,
        BattleView? battle, string? notification, IReadOnlyList<string> battleLog, string prompt,
        IReadOnlyList<int>? switchList, bool showBack, string? result, int? timerSeconds)
    {
        timerColumn = null;
        if (battle != null && result == null)
        {
            DrawBattle(battle, notification, battleLog, prompt, switchList, showBack, timerSeconds);
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
            DrawPokemonCatalog(catalog.Pokemon);
            if (teamNames.Count > 0)
                TextRenderer.WriteLine("선택한 팀: " + string.Join(" / ", teamNames));
            TextRenderer.WriteLine($"{teamNames.Count + 1}번째 포켓몬을 선택해주세요. (목록의 번호 입력, 리전폼 등은 10001 이후 번호)");
        }
        else
        {
            var movesById = catalog.Moves.ToDictionary(move => move.MoveId);
            DrawMoveCatalog(selectedPokemon, selectedPokemon.LearnableMoveIds.Select(id => movesById[id]));
            TextRenderer.WriteLine("선택한 팀: " + string.Join(" / ", teamNames));
            TextRenderer.WriteLine("선택한 기술: " + string.Join(" / ", selectedMoves.Select(id => movesById[id].Name)));
            TextRenderer.WriteLine("0. 기술 4개 무작위 선택 후 확정 (현재 선택한 기술을 대체)");
            // 팀에 같은 종이 없으므로 이름으로 몇 번째인지 찾는다.
            int order = teamNames.ToList().IndexOf(DisplayName(selectedPokemon)) + 1;
            TextRenderer.Write($"{order}번째 포켓몬 ");
            TypeColorRenderer.WriteColored(DisplayName(selectedPokemon), SelectedPokemonColor);
            TextRenderer.WriteLine($"의 {selectedMoves.Count + 1}번 기술 번호를 입력하세요 (서로 다른 기술 4개).");
        }
        if (notification != null) TextRenderer.WriteLine("\n" + notification);
        // 마지막 줄이 입력 줄이다. 남은 시간 뒤에서 입력한다.
        if (catalog != null && timerSeconds is int seconds)
        {
            TextRenderer.Write(FormatTimer(seconds) + (submitted ? "" : "> "));
            timerColumn = 0;
        }
    }

    private static void DrawPokemonCatalog(IEnumerable<PokemonCatalogEntry> pokemon)
    {
        TextRenderer.WriteLine("포켓몬 목록");
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

    private static void DrawMoveCatalog(PokemonCatalogEntry pokemon, IEnumerable<MoveView> moves)
    {
        TypeColorRenderer.WriteColored(DisplayName(pokemon), SelectedPokemonColor);
        // 조사는 마지막 한글(폼 이름이 있으면 폼 이름)에 맞춘다.
        TextRenderer.WriteLine($"{TextRenderer.Josa(pokemon.FormName ?? pokemon.Name, "이", "가")} 배울 수 있는 기술 목록");
        TextRenderer.WriteLine("번호 타입    기술이름 위력 명중률");
        foreach (MoveView move in moves)
        {
            TextRenderer.Write($"{move.MoveId,3}. ");
            TypeColorRenderer.WriteMarker(move.Type.ColorRgb);
            TextRenderer.Write(TextRenderer.FitToWidth(move.Type.Name, 6));
            TextRenderer.WriteLine($" {TextRenderer.FitToWidth(move.Name, 8)} {move.Power?.ToString() ?? "-",3} {move.Accuracy?.ToString() ?? "-",3}");
        }
    }

    private static void DrawBattle(BattleView battle, string? notification, IReadOnlyList<string> battleLog,
        string prompt, IReadOnlyList<int>? switchList, bool showBack, int? timerSeconds)
    {
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        TeamMemberView active = battle.Team[battle.Active];
        int leftX = 2;
        int columns = Math.Max(1, width / 2 - 4);
        int maxRows = Math.Max(1, height - BattleInformationRows);
        string[] player = PokemonRenderer.Load(active.Pokemon.PokemonId, active.Pokemon.SpeciesId, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent.PokemonId, battle.Opponent.SpeciesId, maxRows, columns, flip: false);
        int statusY = Math.Max(player.Length, opponent.Length) + 4;
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
        var panels = BattlePanelRenderer.Arrange(battle, width, statusY, switchList, showBack);
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
        pokemon.PokemonId, pokemon.SpeciesId, Math.Max(1, Console.BufferHeight - BattleInformationRows),
        Math.Max(1, Console.BufferWidth / 2 - 4), flip: false).Length;

    // Animation frames update only the sprite area, leaving menus and logs steady.
    public static void DrawBattleFrame(BattleView battle, int playerJumpOffset, int opponentJumpOffset,
        bool flashPlayer, bool flashOpponent,
        TypeView? effectType, bool effectFromPlayer, double effectProgress,
        double playerFaintProgress = 0, double opponentFaintProgress = 0,
        StatusAnimation.Effect? statusEffect = null, bool? healingOnPlayer = null)
    {
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        int columns = Math.Max(1, width / 2 - 4);
        int maxRows = Math.Max(1, height - BattleInformationRows);
        string[] player = PokemonRenderer.Load(battle.Team[battle.Active].Pokemon.PokemonId, battle.Team[battle.Active].Pokemon.SpeciesId, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent.PokemonId, battle.Opponent.SpeciesId, maxRows, columns, flip: false);
        int rightX = width - Math.Max(BattlePanelRenderer.Width,
            PokemonRenderer.GetHorizontalBounds(opponent).MaxX + 1) - 2;

        spriteBuffer.BeginFrame();
        spriteBuffer.AddImage(player, 2, 2 + playerJumpOffset, flashPlayer,
            (int)Math.Round(Math.Clamp(playerFaintProgress, 0, 1) * player.Length));
        spriteBuffer.AddImage(opponent, rightX, 2 + opponentJumpOffset, flashOpponent,
            (int)Math.Round(Math.Clamp(opponentFaintProgress, 0, 1) * opponent.Length));
        if (effectType != null && effectProgress is >= 0 and <= 1)
        {
            string effectColor = effectType.ColorRgb;
            int effectBottom = 2 + Math.Max(player.Length, opponent.Length);
            var (_, playerMax) = PokemonRenderer.GetHorizontalBounds(player);
            var (opponentMin, _) = PokemonRenderer.GetHorizontalBounds(opponent);
            int playerFront = 2 + playerMax;
            int opponentFront = rightX + opponentMin;
            int leftStart = playerFront - 3, leftEnd = opponentFront - 3;
            if (leftEnd < leftStart)
                (leftStart, leftEnd) = (playerFront, opponentFront);
            int start = effectFromPlayer ? leftStart : leftEnd;
            int end = effectFromPlayer ? leftEnd : leftStart;
            double travelProgress = Math.Clamp(effectProgress / 0.78, 0, 1);
            int x = start + (int)Math.Round((end - start) * travelProgress);
            int targetOffset = effectFromPlayer ? opponentJumpOffset : playerJumpOffset;
            int y = Math.Max(4, 2 + Math.Max(player.Length, opponent.Length) / 2 + targetOffset);
            y += TypeMotionOffset(effectType.Name, effectProgress);
            if (x >= 0 && x < width - 1 && y < height)
            {
                if (effectProgress < 0.78)
                {
                    int direction = effectFromPlayer ? 1 : -1;
                    DrawTypeTrail(x, y, effectType.Name, direction, effectColor, width, effectBottom, spriteBuffer);
                    AttackImageRenderer.Draw(effectType.TypeId, x, y, !effectFromPlayer,
                        width, 2, effectBottom, spriteBuffer);
                }
                else
                {
                    AttackImageRenderer.Draw(effectType.TypeId, end, y, !effectFromPlayer,
                        width, 2, effectBottom, spriteBuffer);
                    int incoming = effectFromPlayer ? -1 : 1;
                    foreach (var (offsetX, offsetY) in new[]
                    {
                        (-5, 0), (-4, -4), (-4, 4), (-2, -5), (-2, 5), (0, -5), (0, 5)
                    })
                        DrawEffectAt(end + offsetX * incoming, y + offsetY, "■", effectColor, width, effectBottom,
                            spriteBuffer, bold: true);
                }
            }
        }
        if (statusEffect is { } status)
        {
            string[] target = status.OnPlayer ? player : opponent;
            int targetX = status.OnPlayer ? 2 : rightX;
            int targetY = 2 + (status.OnPlayer ? playerJumpOffset : opponentJumpOffset);
            DrawStatusOverlay(spriteBuffer, status, target, targetX, targetY, width);
        }
        if (healingOnPlayer is bool onPlayer)
        {
            string[] target = onPlayer ? player : opponent;
            int targetX = onPlayer ? 2 : rightX;
            int targetY = 2 + (onPlayer ? playerJumpOffset : opponentJumpOffset);
            DrawHealingOverlay(spriteBuffer, target, targetX, targetY, width);
        }
        spriteBuffer.Flush(width, height);
    }

    internal static void DrawStatusOverlay(BattleSpriteBuffer buffer, StatusAnimation.Effect effect,
        string[] target, int x, int y, int width)
    {
        var (minX, maxX) = PokemonRenderer.GetHorizontalBounds(target);
        AttackImageRenderer.Draw(effect.TypeId, x + (minX + maxX + 1) / 2,
            y + target.Length / 2, flip: !effect.OnPlayer, width, y, y + target.Length, buffer);
    }

    internal static void DrawHealingOverlay(BattleSpriteBuffer buffer, string[] target, int x, int y, int width)
    {
        var (minX, maxX) = PokemonRenderer.GetHorizontalBounds(target);
        AttackImageRenderer.DrawHeal(x + (minX + maxX + 1) / 2,
            y + target.Length / 2, width, y, y + target.Length, buffer);
    }

    private static void DrawTypeTrail(int x, int y, string type, int direction, string color, int width, int height,
        BattleSpriteBuffer drawnCells)
    {
        (int X, int Y)[] particles = type switch
        {
            "불꽃" => [(-5, 0), (-4, -1), (-6, 1)],
            "물" => [(-5, -1), (-5, 1), (-7, 0)],
            "전기" => [(-4, -1), (-5, 1), (-7, -1)],
            "풀" => [(-5, -1), (-5, 1), (-7, 0)],
            "얼음" => [(-5, 0), (-7, -1), (-7, 1)],
            "독" => [(-5, -1), (-6, 1), (-8, 0)],
            "비행" => [(-5, -1), (-5, 1), (-8, 0)],
            "고스트" or "에스퍼" => [(-6, -1), (-7, 1)],
            "땅" or "바위" => [(-5, 1), (-7, 1)],
            "드래곤" => [(-5, 0), (-7, -1), (-7, 1)],
            "강철" => [(-5, 0), (-7, 0)],
            _ => [(-5, 0), (-7, 0)]
        };

        foreach (var particle in particles)
            DrawEffectAt(x + particle.X * direction, y + particle.Y, "■", color, width, height, drawnCells,
                bold: particle.X == -5);
    }

    private static int TypeMotionOffset(string type, double progress)
    {
        int frame = (int)(progress * 12);
        return type switch
        {
            "전기" => frame % 2 == 0 ? -1 : 1,
            "불꽃" => frame % 3 == 0 ? -1 : 0,
            "물" => (int)Math.Round(Math.Sin(progress * Math.PI * 4)),
            "풀" or "비행" => (int)Math.Round(-Math.Sin(progress * Math.PI)),
            "벌레" => frame % 3 == 0 ? 1 : frame % 3 == 1 ? -1 : 0,
            "땅" or "바위" => progress > 0.5 ? 1 : 0,
            "얼음" => frame % 4 == 0 ? -1 : 0,
            "고스트" or "에스퍼" => (int)Math.Round(Math.Sin(progress * Math.PI * 2)),
            _ => 0
        };
    }

    private static void DrawEffectAt(int x, int y, string glyph, string color, int width, int height,
        BattleSpriteBuffer drawnCells, bool bold = false)
    {
        if (x < 0 || x >= width - 1 || y < 2 || y >= height) return;
        if (color.Length != 7 || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return;
        int red = (int)(rgb >> 16), green = (int)((rgb >> 8) & 255), blue = (int)(rgb & 255);
        red = Math.Min(255, red * 3 / 5 + 102);
        green = Math.Min(255, green * 3 / 5 + 102);
        blue = Math.Min(255, blue * 3 / 5 + 102);
        drawnCells.SetPixel(x, y, $"\x1b[{(bold ? "1;" : "")}38;2;{red};{green};{blue}m", glyph[0]);
    }

    private static void DrawResult(string result, string? notification)
    {
        TextRenderer.WriteLine();
        TextRenderer.WriteLine("  ==========================");
        TextRenderer.WriteLine("            " + (result is "WIN" or "DRAW" ? result : "LOSE"));
        TextRenderer.WriteLine("  ==========================");
        TextRenderer.WriteLine();
        TextRenderer.WriteLine("  1. 재도전  2. 나가기");
        if (notification != null)
            TextRenderer.WriteLine("\n  " + notification);
    }

}
