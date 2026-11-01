using Pokemon.Protocol;

namespace Client;

static class InGameRenderer
{
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
            TextRenderer.WriteLine($"{order}번째 포켓몬 {selectedPokemon.Name}의 " +
                $"{selectedMoves.Count + 1}번 기술 번호를 입력하세요 (서로 다른 기술 4개).");
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

    private static void DrawMoveCatalog(PokemonCatalogEntry pokemon, IEnumerable<MoveView> moves)
    {
        TextRenderer.WriteLine($"{pokemon.Name}이(가) 레벨업으로 배울 수 있는 기술");
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
                    $"{index + 1}. {pokemon.Name} (HP {pokemon.CurrentHp}/{pokemon.MaxHp})");
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

    private static void DrawResult(string result, string? notification)
    {
        TextRenderer.WriteLine();
        TextRenderer.WriteLine("  ==========================");
        TextRenderer.WriteLine(result == "WIN" ? "            WIN" : "            LOSE");
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
    }
}
