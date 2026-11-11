using System.Text;
using System.Text.RegularExpressions;

namespace Client;

static class PokemonRenderer
{
    private static readonly Regex AnsiCode = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    // 그림이 없는 포켓몬(899번 이후 등)에 쓰는 물음표 그림
    private static readonly string[] Placeholder =
    [
        "  ??????  ",
        " ??    ?? ",
        "      ??  ",
        "     ??   ",
        "    ??    ",
        "          ",
        "    ??    ",
    ];

    // 폼 번호의 그림 → 같은 종(전국도감 번호)의 그림 → 물음표 순으로 찾는다.
    // 리전폼·메가진화 등 다른 폼은 아직 그림이 없어 기본 폼 그림을 쓴다.
    public static string[] Load(int pokemonId, int speciesId, int maxRows, int maxColumns, bool flip)
    {
        string? path = new[] { pokemonId, speciesId }
            .Select(id => Path.Combine(AppContext.BaseDirectory, "resources", "pokemon_scripts", $"{id:D4}.txt"))
            .FirstOrDefault(File.Exists);
        if (path == null)
            return Placeholder.Take(maxRows).Select(line => line[..Math.Min(line.Length, maxColumns)]).ToArray();
        string[] source = File.ReadAllLines(path, Encoding.UTF8);
        int sourceRows = source.Length;
        while (sourceRows > 1 && string.IsNullOrWhiteSpace(AnsiCode.Replace(source[sourceRows - 1], "")))
            sourceRows--;
        int count = Math.Min(sourceRows, maxRows);
        var lines = new string[count];
        for (int row = 0; row < count; row++)
        {
            int index = count == 1 ? 0 : (int)Math.Round((double)row * (sourceRows - 1) / (count - 1));
            List<(string Ansi, char Character)> cells = Parse(source[index]);
            if (flip) cells.Reverse();
            var line = new StringBuilder();
            foreach (var cell in cells.Take(maxColumns))
                line.Append(cell.Ansi).Append(flip ? Mirror(cell.Character) : cell.Character);
            lines[row] = line.Append("\x1b[0m").ToString();
        }
        return lines;
    }

    public static void Draw(string[] lines, int x, int y, bool flash = false)
    {
        for (int row = 0; row < lines.Length; row++)
        {
            Console.SetCursorPosition(x, y + row);
            Console.Write(flash ? Blank(lines[row]) : lines[row]);
            Console.Write("\x1b[0m");
        }
    }

    public static void DrawCell(string[] lines, int x, int y, int row, int column, bool flash = false)
    {
        Console.SetCursorPosition(x, y);
        // 숨김 프레임에서는 공격 이미지 아래의 포켓몬 픽셀도 다시 나타나지 않게 한다.
        if (flash)
        {
            Console.Write("\x1b[0m ");
            return;
        }
        if (row < 0 || row >= lines.Length)
        {
            Console.Write(' ');
            return;
        }

        List<(string Ansi, char Character)> cells = Parse(lines[row]);
        if (column < 0 || column >= cells.Count)
        {
            Console.Write(' ');
            return;
        }
        var cell = cells[column];
        Console.Write(cell.Ansi);
        Console.Write(cell.Character);
        Console.Write("\x1b[0m");
    }

    public static (int MinX, int MaxX) GetHorizontalBounds(string[] lines)
    {
        int minX = int.MaxValue, maxX = -1;
        foreach (string line in lines)
        {
            List<(string Ansi, char Character)> cells = Parse(line);
            for (int column = 0; column < cells.Count; column++)
                if (cells[column].Character != ' ')
                {
                    minX = Math.Min(minX, column);
                    maxX = Math.Max(maxX, column);
                }
        }
        return maxX < 0 ? (0, 0) : (minX, maxX);
    }

    // ANSI 코드를 제외한 실제 글자 수만큼 공백을 써서 포켓몬을 지운다.
    private static string Blank(string line) => "\x1b[0m" + new string(' ', Parse(line).Count);

    internal static List<(string Ansi, char Character)> Parse(string line)
    {
        var cells = new List<(string, char)>();
        string ansi = "";
        for (int index = 0; index < line.Length;)
        {
            if (line[index] == '\x1b')
            {
                Match match = AnsiCode.Match(line, index);
                if (match.Success && match.Index == index)
                {
                    ansi = match.Value;
                    index += match.Length;
                    continue;
                }
            }
            cells.Add((ansi, line[index++]));
        }
        return cells;
    }

    private static char Mirror(char c) => c switch
    {
        '▌' => '▐', '▐' => '▌', '▖' => '▗', '▗' => '▖',
        '▘' => '▝', '▝' => '▘', '◢' => '◣', '◣' => '◢',
        '◤' => '◥', '◥' => '◤', '<' => '>', '>' => '<',
        '(' => ')', ')' => '(', '[' => ']', ']' => '[', '{' => '}', '}' => '{',
        _ => c
    };
}
