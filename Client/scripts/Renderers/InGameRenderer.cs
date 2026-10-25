using Pokemon.Protocol;

namespace Client;

static class InGameRenderer
{
    public static void Draw(PokemonCatalogResponse? catalog, PokemonCatalogEntry? selectedPokemon,
        IReadOnlyList<int> selectedMoves, bool submitted, BattleView? battle, string? notification)
    {
        Console.Clear();
        if (battle != null)
        {
            DrawBattle(battle, notification);
            return;
        }

        TextRenderer.WriteLine("[게임 중] 포켓몬과 기술 선택");
        if (catalog == null)
            TextRenderer.WriteLine("서버에서 포켓몬 목록을 받는 중입니다.");
        else if (submitted)
        {
            TextRenderer.WriteLine($"선택한 포켓몬: {selectedPokemon!.Name}");
            TextRenderer.WriteLine("상대방의 선택을 기다립니다.");
        }
        else if (selectedPokemon == null)
        {
            DrawPokemonCatalog(catalog.Pokemon);
            TextRenderer.WriteLine("포켓몬 도감번호를 입력하세요.");
        }
        else
        {
            var movesById = catalog.Moves.ToDictionary(move => move.MoveId);
            DrawMoveCatalog(selectedPokemon, selectedPokemon.LearnableMoveIds.Select(id => movesById[id]));
            TextRenderer.WriteLine("선택한 기술: " + string.Join(" / ", selectedMoves.Select(id => movesById[id].Name)));
            TextRenderer.WriteLine($"{selectedMoves.Count + 1}번 기술의 번호를 입력하세요 (서로 다른 기술 4개).");
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

    private static void DrawBattle(BattleView battle, string? notification)
    {
        int width = Console.BufferWidth;
        int height = Console.BufferHeight;
        if (width < 76 || height < 20)
        {
            TextRenderer.WriteLine("두 포켓몬을 표시하려면 76열 × 20행 이상의 터미널이 필요합니다.");
            return;
        }

        int rightX = width / 2 + 1;
        int columns = width / 2 - 4;
        int maxRows = height - 12;
        string[] player = PokemonRenderer.Load(battle.Player.PokemonId, maxRows, columns, flip: true);
        string[] opponent = PokemonRenderer.Load(battle.Opponent.PokemonId, maxRows, columns, flip: false);
        TextRenderer.At(2, 0, "내 포켓몬");
        TextRenderer.At(rightX, 0, "상대 포켓몬");
        PokemonRenderer.Draw(player, 2, 2);
        PokemonRenderer.Draw(opponent, rightX, 2);
        int statusY = Math.Max(player.Length, opponent.Length) + 3;
        DrawStatus(battle.Player, 2, statusY, columns);
        DrawStatus(battle.Opponent, rightX, statusY, columns);
        TextRenderer.At(2, statusY + 3, "선택한 기술");
        for (int index = 0; index < battle.Moves.Length; index++)
        {
            MoveView move = battle.Moves[index];
            TextRenderer.At(2, statusY + 4 + index, $"{index + 1}. ");
            TypeColorRenderer.WriteMarker(move.Type.ColorRgb);
            TextRenderer.Write(" " + TextRenderer.FitToWidth(move.Name, columns - 6));
        }
        if (notification != null)
            TextRenderer.At(2, statusY + 8, TextRenderer.FitToWidth(notification, width - 4));
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
