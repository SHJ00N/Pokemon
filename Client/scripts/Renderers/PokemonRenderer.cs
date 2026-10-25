using System.Text;
using System.Text.RegularExpressions;

namespace Client;

static class PokemonRenderer
{
    private static readonly Regex AnsiCode = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public static string[] Load(int pokemonId, int maxRows, int maxColumns, bool flip)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "resources", "pokemon_scripts", $"{pokemonId:D4}.txt");
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

    public static void Draw(string[] lines, int x, int y)
    {
        for (int row = 0; row < lines.Length; row++)
        {
            Console.SetCursorPosition(x, y + row);
            Console.Write(lines[row]);
        }
    }

    private static List<(string Ansi, char Character)> Parse(string line)
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
