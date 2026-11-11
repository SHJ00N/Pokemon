using System.Text;

namespace Client;

// 포켓몬과 공격을 메모리에서 먼저 합성한다. 빈 화면을 거치지 않고 바뀐 칸만 한 번에 출력한다.
sealed class BattleSpriteBuffer
{
    private Dictionary<(int X, int Y), (string Ansi, char Character)> previous = new();
    private Dictionary<(int X, int Y), (string Ansi, char Character)> current = new();

    public void Reset() { previous.Clear(); current.Clear(); }
    public void BeginFrame() => current.Clear();

    public void SetPixel(int x, int y, string ansi, char character)
    {
        if (character != ' ') current[(x, y)] = (ansi, character);
    }

    public void AddImage(string[] lines, int x, int y, bool hidden)
    {
        if (hidden) return;
        for (int row = 0; row < lines.Length; row++)
        {
            var cells = PokemonRenderer.Parse(lines[row]);
            for (int column = 0; column < cells.Count; column++)
                SetPixel(x + column, y + row, cells[column].Ansi, cells[column].Character);
        }
    }

    internal string BuildUpdate(int width, int height)
    {
        var output = new StringBuilder();
        int lastX = -2, lastY = -1;
        string? lastAnsi = null;
        foreach (var point in previous.Keys.Concat(current.Keys).Distinct().OrderBy(p => p.Y).ThenBy(p => p.X))
        {
            if (point.X < 0 || point.X >= width - 1 || point.Y < 1 || point.Y >= height) continue;
            var oldCell = previous.GetValueOrDefault(point, ("\x1b[0m", ' '));
            var cell = current.GetValueOrDefault(point, ("\x1b[0m", ' '));
            if (oldCell == cell) continue;
            if (point.Y != lastY || point.X != lastX + 1)
                output.Append($"\x1b[{point.Y + 1};{point.X + 1}H");
            if (cell.Item1 != lastAnsi)
            {
                output.Append("\x1b[0m");
                if (cell.Item1.Length > 0 && cell.Item1 != "\x1b[0m") output.Append(cell.Item1);
                lastAnsi = cell.Item1;
            }
            output.Append(cell.Item2);
            lastX = point.X;
            lastY = point.Y;
        }
        (previous, current) = (current, previous);
        // 입력 커서를 유지하고, 변화가 없는 프레임은 콘솔에 아무것도 쓰지 않는다.
        return output.Length == 0 ? "" : "\u001b7" + output + "\x1b[0m\u001b8";
    }

    public void Flush(int width, int height)
    {
        string update = BuildUpdate(width, height);
        if (update.Length > 0) Console.Write(update);
    }
}
