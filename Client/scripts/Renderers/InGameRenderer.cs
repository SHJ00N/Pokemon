using Pokemon.Protocol;

namespace Client;

static class InGameRenderer
{
    // 목록에 표시하는 이름. 같은 종의 다른 폼과 구분해야 하면 폼 이름을 붙인다. 예: 라이츄 (알로라의 모습)
    public static string DisplayName(PokemonCatalogEntry entry) =>
        entry.FormName == null ? entry.Name : $"{entry.Name} ({entry.FormName})";

    // 남은 시간은 입력 줄 맨 앞(이 열)에 그린다. 그리지 않은 화면이면 null
    private static int? timerColumn;

    // 남은 시간 표시. 매초 같은 폭으로 고쳐 쓸 수 있게 폭을 고정한다.
    private static string FormatTimer(int seconds) =>
        TextRenderer.FitToWidth(seconds > 0 ? $"[남은 시간 {seconds,2}초]" : "[시간 초과]", 16) + " ";

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
        TextRenderer.Clear();
        timerColumn = null;
        if (result != null)
        {
            DrawResult(result, notification);
            return;
        }
        if (battle != null)
        {
            DrawBattle(battle, notification, battleLog, prompt, switchList, showBack, timerSeconds);
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
        if (width < 76 || height < 27)
        {
            TextRenderer.WriteLine("두 포켓몬을 표시하려면 76열 × 27행 이상의 터미널이 필요합니다.");
            return;
        }

        TeamMemberView active = battle.Team[battle.Active];
        int rightX = width / 2 + 1;
        int columns = width / 2 - 4;
        int maxRows = height - 19;
        string[] player = PokemonRenderer.Load(active.Pokemon.PokemonId, active.Pokemon.SpeciesId, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent.PokemonId, battle.Opponent.SpeciesId, maxRows, columns, flip: false);
        TextRenderer.At(2, 0, "내 포켓몬");
        TextRenderer.At(rightX, 0, "상대 포켓몬");
        PokemonRenderer.Draw(player, 2, 2);
        PokemonRenderer.Draw(opponent, rightX, 2);
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
        // 입력 커서가 안내 문구 바로 뒤에 오도록 마지막에 출력한다. 남은 시간은 그 줄 맨 앞에 둔다.
        TextRenderer.At(2, statusY + 15, (timerSeconds is int seconds ? FormatTimer(seconds) : "") + prompt);
        if (timerSeconds != null)
            timerColumn = 2;
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
