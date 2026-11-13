using Pokemon.Protocol;

namespace Client;

// 상자 한 줄의 너비는 ANSI 색상 코드와 관계없이 터미널 칸 수로 맞춘다.
static class BattlePanelRenderer
{
    public const int Width = 48;
    public const int HealthBarWidth = 20;
    private const int ContentWidth = Width - 4;

    public static string[] Pokemon(PokemonView pokemon)
    {
        string markers = string.Concat(pokemon.Types.Select(type =>
            TypeColorRenderer.Colorize("■", type.ColorRgb) + " "));
        string status = pokemon.Status == null ? "" : " " + StatusColorRenderer.FormatStatus(pokemon.Status);
        int filled = pokemon.MaxHp <= 0 ? 0 : (int)Math.Ceiling(
            (double)Math.Clamp(pokemon.CurrentHp, 0, pokemon.MaxHp) * HealthBarWidth / pokemon.MaxHp);
        string color = (long)pokemon.CurrentHp * 5 <= pokemon.MaxHp ? "#FF5A5A"
            : (long)pokemon.CurrentHp * 2 <= pokemon.MaxHp ? "#FF9F43" : "#4CD964";
        string bar = TypeColorRenderer.Colorize(new string('█', filled), color) +
            TypeColorRenderer.Colorize(new string('░', HealthBarWidth - filled), "#46515C");
        return Box([markers + pokemon.Name + status, $"HP [{bar}] {pokemon.CurrentHp}/{pokemon.MaxHp}"]);
    }

    public static string[] Moves(IReadOnlyList<MoveView> moves)
    {
        const int cellWidth = ContentWidth / 2;
        string Cell(int index) => index >= moves.Count ? new string(' ', cellWidth) :
            $"{index + 1}. " + TypeColorRenderer.Colorize("■", moves[index].Type.ColorRgb) + " " +
            TextRenderer.FitToWidth(moves[index].Name, cellWidth - 5);
        return Box([Cell(0) + Cell(1), Cell(2) + Cell(3)], "기술");
    }

    public static string[] Switches(BattleView battle, IReadOnlyList<int> candidates, bool showBack)
    {
        var lines = candidates.Select((slot, index) =>
        {
            PokemonView pokemon = battle.Team[slot].Pokemon;
            string status = pokemon.Status == null ? "" : " " + StatusColorRenderer.FormatStatus(pokemon.Status);
            return $"{index + 1}. {pokemon.Name}{status}  HP {pokemon.CurrentHp}/{pokemon.MaxHp}";
        }).ToArray();
        return Box(lines, showBack ? "교체" : "교체할 포켓몬");
    }

    public static string[] StatChanges(PokemonView pokemon) => pokemon.StatChanges is { Length: > 0 } changes
        ? new[] { "능력치 변화" }.Concat(changes.Select(StatusColorRenderer.FormatStatChange)).ToArray() : [];

    public readonly record struct Panel(int X, int Y, string[] Lines);

    public static Panel[] Arrange(BattleView battle, int width, int statusY,
        IReadOnlyList<int>? switchList, bool showBack)
    {
        TeamMemberView active = battle.Team[battle.Active];
        int leftX = 2, rightX = width - Width - 2;
        string[] opponentChanges = StatChanges(battle.Opponent);
        int changesWidth = opponentChanges.Select(TextRenderer.DisplayWidth).DefaultIfEmpty(0).Max();
        return [
            new(leftX, statusY, Pokemon(active.Pokemon)),
            new(rightX, statusY, Pokemon(battle.Opponent)),
            new(leftX, statusY + 5, switchList == null ? Moves(active.Moves) : Switches(battle, switchList, showBack)),
            new(leftX + Width + 3, statusY + 1, StatChanges(active.Pokemon)),
            new(rightX - changesWidth - 3, statusY + 1, opponentChanges)
        ];
    }

    public static void Draw(IReadOnlyList<string> lines, int x, int y)
    {
        for (int row = 0; row < lines.Count; row++)
            TextRenderer.At(x, y + row, lines[row]);
    }

    private static string[] Box(IReadOnlyList<string> lines, string? title = null)
    {
        string border = "+" + new string('-', Width - 2) + "+";
        string top = title == null ? border
            : "+- " + title + " " + new string('-', Width - TextRenderer.DisplayWidth(title) - 5) + "+";
        return new[] { top }.Concat(lines.Select(line => "| " + line +
            new string(' ', Math.Max(0, ContentWidth - TextRenderer.DisplayWidth(line))) + " |"))
            .Append(border).ToArray();
    }
}
