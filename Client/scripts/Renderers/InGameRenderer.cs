using System.Globalization;
using Pokemon.Protocol;

namespace Client;

static class InGameRenderer
{
    private static readonly HashSet<(int X, int Y)> previousEffectCells = new();
    private static int drawnPlayerOffset, drawnOpponentOffset;
    private static bool drawnPlayerFlash, drawnOpponentFlash, battleBaseInitialized;

    private static readonly Dictionary<string, string[]> EffectSprites = new()
    {
        ["노말"] = ["..###..", ".##.##.", "###*###", "###.###", ".#####.", "..###..", "...#..."],
        ["불꽃"] = ["...*...", "..*#*..", ".*###+.", ".#####.", ".#####.", "..###..", "...#..."],
        ["물"] = ["...#...", "..###..", ".#####.", "#######", "#######", ".#####.", "..###.."],
        ["전기"] = ["....#..", "...##..", "..##...", "#######", "..##...", ".##....", "##....."],
        ["풀"] = ["...##..", "..###*.", ".#####.", "#####..", ".####..", "..##...", "..#...."],
        ["얼음"] = ["...#...", ".#.+.#.", "..###..", "###+###", "..###..", ".#.+.#.", "...#..."],
        ["격투"] = ["...##..", "..####.", ".#####.", "#######", ".#####.", "..###..", "..#...."],
        ["독"] = [".*...*.", "..###..", ".#####.", "##.#.##", "#######", "..#.#..", "......."],
        ["땅"] = ["...#...", "..###..", "#######", ".##.##.", "##...##", ".#...#.", "......."],
        ["비행"] = ["#.....#", "##...##", "###.###", "#######", ".#####.", "..###..", "...#..."],
        ["에스퍼"] = ["..###..", ".#...#.", "#..*..#", "#.###.#", "#..*..#", ".#...#.", "..###.."],
        ["벌레"] = [".#...#.", "##.#.##", ".#####.", "..###..", ".#####.", "##.#.##", ".#...#."],
        ["바위"] = ["..###..", ".#####.", "#######", "###+###", "#######", ".#####.", "..###.."],
        ["고스트"] = ["..###..", ".#####.", "###+###", "###.###", ".#####.", "..#.#..", ".#...#."],
        ["드래곤"] = ["..##...", ".####..", "#######", "#######", ".####..", "..###*.", "..##..."],
        ["악"] = ["#..#..#", "#.##.#.", ".#####.", "..###..", "...#...", "..#.#..", ".#...#."],
        ["강철"] = ["...##..", ".####..", "######.", "###*###", ".######", "..####.", "...##.."],
        ["페어리"] = ["...#...", "..###..", "#######", ".#####.", "..###..", ".#...#.", "*.....*"]
    };

    // switchList: 교체 메뉴일 때 고를 수 있는 팀 번호(0부터). null이면 기술 목록을 표시한다.
    public static void Draw(PokemonCatalogResponse? catalog, IReadOnlyList<string> teamNames,
        PokemonCatalogEntry? selectedPokemon, IReadOnlyList<int> selectedMoves, bool submitted,
        BattleView? battle, string? notification, IReadOnlyList<string> battleLog, string prompt,
        IReadOnlyList<int>? switchList, bool showBack, string? result)
    {
        TextRenderer.Clear();
        if (result != null)
        {
            DrawResult(result, notification);
            return;
        }
        if (battle != null)
        {
            DrawBattle(battle, notification, battleLog, prompt, switchList, showBack);
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
            TextRenderer.WriteLine($"{teamNames.Count + 1}번째 포켓몬을 선택해주세요. (도감번호 입력)");
        }
        else
        {
            var movesById = catalog.Moves.ToDictionary(move => move.MoveId);
            DrawMoveCatalog(selectedPokemon, selectedPokemon.LearnableMoveIds.Select(id => movesById[id]));
            TextRenderer.WriteLine("선택한 팀: " + string.Join(" / ", teamNames));
            TextRenderer.WriteLine("선택한 기술: " + string.Join(" / ", selectedMoves.Select(id => movesById[id].Name)));
            // 팀에 같은 포켓몬이 없으므로 이름으로 몇 번째인지 찾는다.
            int order = teamNames.ToList().IndexOf(selectedPokemon.Name) + 1;
            TextRenderer.Write($"{order}번째 포켓몬 ");
            TypeColorRenderer.WriteColored(selectedPokemon.Name, SelectedPokemonColor);
            TextRenderer.WriteLine($"의 {selectedMoves.Count + 1}번 기술 번호를 입력하세요 (서로 다른 기술 4개).");
        }
        if (notification != null) TextRenderer.WriteLine("\n" + notification);
    }

    private static void DrawPokemonCatalog(IEnumerable<PokemonCatalogEntry> pokemon)
    {
        TextRenderer.WriteLine("포켓몬 목록");
        foreach (PokemonCatalogEntry entry in pokemon)
        {
            TextRenderer.Write($"{entry.PokemonId,3}. {TextRenderer.FitToWidth(entry.Name, 10)}");
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
        TypeColorRenderer.WriteColored(pokemon.Name, SelectedPokemonColor);
        TextRenderer.WriteLine($"{TextRenderer.Josa(pokemon.Name, "이", "가")} 배울 수 있는 기술 목록");
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
        string prompt, IReadOnlyList<int>? switchList, bool showBack)
    {
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        if (width < 76 || height < 27)
        {
            TextRenderer.WriteLine("두 포켓몬을 표시하려면 76열 × 27행 이상의 터미널이 필요합니다.");
            return;
        }

        TeamMemberView active = battle.Team[battle.Active];
        int rightX = width / 2 + 1;
        int columns = width / 2 - 4;
        int maxRows = height - 19;
        string[] player = PokemonRenderer.Load(active.Pokemon.PokemonId, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent.PokemonId, maxRows, columns, flip: false);
        TextRenderer.At(2, 0, "내 포켓몬");
        TextRenderer.At(rightX, 0, "상대 포켓몬");
        PokemonRenderer.Draw(player, 2, 2);
        PokemonRenderer.Draw(opponent, rightX, 2);
        previousEffectCells.Clear();
        drawnPlayerOffset = drawnOpponentOffset = 0;
        drawnPlayerFlash = drawnOpponentFlash = false;
        battleBaseInitialized = true;
        int statusY = Math.Max(player.Length, opponent.Length) + 3;
        DrawStatus(active.Pokemon, 2, statusY, columns);
        DrawStatus(battle.Opponent, rightX, statusY, columns);

        string back = showBack ? "  (0. 뒤로)" : "";
        if (switchList != null)
        {
            TextRenderer.At(2, statusY + 3, "교체할 포켓몬" + back);
            for (int index = 0; index < switchList.Count; index++)
            {
                PokemonView pokemon = battle.Team[switchList[index]].Pokemon;
                TextRenderer.At(2, statusY + 4 + index,
                    $"{index + 1}. {pokemon.Name} (HP {pokemon.CurrentHp}/{pokemon.MaxHp}) ");
                if (pokemon.Status != null)
                    StatusColorRenderer.WriteStatus(pokemon.Status);
            }
        }
        else
        {
            TextRenderer.At(2, statusY + 3, "기술" + back);
            for (int index = 0; index < active.Moves.Length; index++)
            {
                MoveView move = active.Moves[index];
                TextRenderer.At(2, statusY + 4 + index, $"{index + 1}. ");
                TypeColorRenderer.WriteMarker(move.Type.ColorRgb);
                TextRenderer.Write(" " + TextRenderer.FitToWidth(move.Name, columns - 6));
            }
        }

        for (int index = 0; index < battleLog.Count; index++)
            TextRenderer.At(2, statusY + 9 + index, TextRenderer.FitToWidth(battleLog[index], width - 4));
        if (notification != null)
            TextRenderer.At(2, statusY + 14, TextRenderer.FitToWidth(notification, width - 4));
        // 입력 커서가 안내 문구 바로 뒤에 오도록 마지막에 출력한다.
        TextRenderer.At(2, statusY + 15, prompt);
    }

    // Animation frames update only the sprite area, leaving menus and logs steady.
    public static void DrawBattleFrame(BattleView battle, int playerJumpOffset, int opponentJumpOffset,
        bool flashPlayer, bool flashOpponent,
        string? effectColor, string? effectType, bool effectFromPlayer, double effectProgress)
    {
        int cursorX = Console.CursorLeft;
        int cursorY = Console.CursorTop;
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        if (width < 76 || height < 27) return;

        int rightX = width / 2 + 1;
        int columns = width / 2 - 4;
        int maxRows = height - 19;
        string[] player = PokemonRenderer.Load(battle.Team[battle.Active].Pokemon.PokemonId, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent.PokemonId, maxRows, columns, flip: false);

        try
        {
            bool redrawSprites = !battleBaseInitialized || playerJumpOffset != drawnPlayerOffset ||
                opponentJumpOffset != drawnOpponentOffset || flashPlayer != drawnPlayerFlash ||
                flashOpponent != drawnOpponentFlash;
            if (redrawSprites)
            {
                previousEffectCells.Clear();
                ClearSpriteArea(2, player.Length, columns);
                ClearSpriteArea(rightX, opponent.Length, columns);
                PokemonRenderer.Draw(player, 2, 2 + playerJumpOffset, flashPlayer);
                PokemonRenderer.Draw(opponent, rightX, 2 + opponentJumpOffset, flashOpponent);
                drawnPlayerOffset = playerJumpOffset;
                drawnOpponentOffset = opponentJumpOffset;
                drawnPlayerFlash = flashPlayer;
                drawnOpponentFlash = flashOpponent;
                battleBaseInitialized = true;
            }
            else
                RestorePreviousEffectCells(player, opponent, rightX, columns);

            if (effectColor != null && effectType != null && effectProgress is >= 0 and <= 1)
            {
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
                y += TypeMotionOffset(effectType, effectProgress);
                if (x >= 0 && x < width - 1 && y < height)
                {
                    if (effectProgress < 0.78)
                    {
                        int direction = effectFromPlayer ? 1 : -1;
                        DrawTypeTrail(x, y, effectType, direction, effectColor, width, height, previousEffectCells);
                        DrawTypeSprite(x, y, effectType, effectColor, width, height, previousEffectCells);
                    }
                    else
                    {
                        DrawTypeSprite(end, y, effectType, effectColor, width, height, previousEffectCells);
                        int incoming = effectFromPlayer ? -1 : 1;
                        foreach (var (offsetX, offsetY) in new[]
                        {
                            (-5, 0), (-4, -4), (-4, 4), (-2, -5), (-2, 5), (0, -5), (0, 5)
                        })
                            DrawEffectAt(end + offsetX * incoming, y + offsetY, "■", effectColor, width, height,
                                previousEffectCells, bold: true);
                    }
                }
            }
        }
        finally
        {
            if (cursorX < Console.BufferWidth && cursorY < Console.BufferHeight)
                Console.SetCursorPosition(cursorX, cursorY);
        }
    }

    private static void RestorePreviousEffectCells(string[] player, string[] opponent, int rightX, int columns)
    {
        foreach (var (x, y) in previousEffectCells)
        {
            if (x >= 2 && x < 2 + columns)
                PokemonRenderer.DrawCell(player, x, y, y - 2 - drawnPlayerOffset, x - 2, drawnPlayerFlash);
            else if (x >= rightX && x < rightX + columns)
                PokemonRenderer.DrawCell(opponent, x, y, y - 2 - drawnOpponentOffset, x - rightX, drawnOpponentFlash);
            else
            {
                Console.SetCursorPosition(x, y);
                Console.Write(" \x1b[0m");
            }
        }
        previousEffectCells.Clear();
    }

    private static void ClearSpriteArea(int x, int rows, int columns)
    {
        string blank = new(' ', columns);
        for (int row = -1; row <= rows; row++)
        {
            int y = 2 + row;
            if (y < 0 || y >= Console.BufferHeight) continue;
            Console.SetCursorPosition(x, y);
            Console.Write(blank);
            Console.Write("\x1b[0m");
        }
    }

    private static void DrawTypeSprite(int centerX, int centerY, string type, string color, int width, int height,
        HashSet<(int X, int Y)> drawnCells)
    {
        if (!EffectSprites.TryGetValue(type, out string[]? sprite)) return;
        for (int row = 0; row < sprite.Length; row++)
        for (int column = 0; column < sprite[row].Length; column++)
        {
            char pixel = sprite[row][column];
            if (pixel is '.' or ' ') continue;
            string pixelColor = pixel switch
            {
                '*' => "#FFFFFF",
                '+' => Brighten(color, 0.72),
                _ => Brighten(color, 0.38)
            };
            DrawEffectAt(centerX + column - 3, centerY + row - 3, "■", pixelColor, width, height,
                drawnCells, bold: true);
        }
    }

    private static void DrawTypeTrail(int x, int y, string type, int direction, string color, int width, int height,
        HashSet<(int X, int Y)> drawnCells)
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

    private static string Brighten(string color, double strength)
    {
        if (color.Length != 7 || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return color;
        int red = (int)(rgb >> 16), green = (int)((rgb >> 8) & 255), blue = (int)(rgb & 255);
        red += (int)((255 - red) * strength);
        green += (int)((255 - green) * strength);
        blue += (int)((255 - blue) * strength);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static void DrawEffectAt(int x, int y, string glyph, string color, int width, int height,
        HashSet<(int X, int Y)> drawnCells, bool bold = false)
    {
        if (x < 0 || x >= width - 1 || y < 0 || y >= height) return;
        if (color.Length != 7 || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return;
        int red = (int)(rgb >> 16), green = (int)((rgb >> 8) & 255), blue = (int)(rgb & 255);
        red = Math.Min(255, red * 3 / 5 + 102);
        green = Math.Min(255, green * 3 / 5 + 102);
        blue = Math.Min(255, blue * 3 / 5 + 102);
        Console.SetCursorPosition(x, y);
        Console.Write($"\x1b[{(bold ? "1;" : "")}38;2;{red};{green};{blue}m{glyph}\x1b[0m");
        drawnCells.Add((x, y));
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

    private static void DrawStatus(PokemonView pokemon, int x, int y, int columns)
    {
        TextRenderer.At(x, y, TextRenderer.FitToWidth(pokemon.Name, columns));
        TextRenderer.At(x, y + 1, $"HP {pokemon.CurrentHp}/{pokemon.MaxHp} ");
        foreach (TypeView type in pokemon.Types)
        {
            TypeColorRenderer.WriteMarker(type.ColorRgb);
            TextRenderer.Write(" ");
        }
        if (pokemon.Status != null)
            StatusColorRenderer.WriteStatus(pokemon.Status);
        if (pokemon.StatChanges is { Length: > 0 } changes)
        {
            Console.SetCursorPosition(x, y + 2);
            StatusColorRenderer.WriteStatChanges(changes, columns);
        }
    }
}
