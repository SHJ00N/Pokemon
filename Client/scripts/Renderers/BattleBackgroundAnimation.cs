namespace Client;

// 배경용 ANSI 파일은 114열×24행 프레임을 재생 순서대로 이어 붙인다.
sealed class BattleBackgroundAnimation
{
    private const int FrameWidth = 114;
    private const int FrameRows = 24;
    private const int FrameMilliseconds = 75;
    private readonly record struct Cell(int X, int Y, string Ansi, char Character);
    private readonly Lazy<Cell[][]> frames;

    public BattleBackgroundAnimation(string fileName) => frames = new(() => Load(fileName));

    public void Draw(long now, int width, int top, int rows, BattleSpriteBuffer buffer)
    {
        if (width <= 0 || rows <= 0) return;
        var clip = frames.Value;
        if (clip.Length == 0) return;
        int index = (int)(now / FrameMilliseconds % clip.Length);
        foreach (var cell in clip[index])
        {
            int x = (int)Math.Round(cell.X * (width - 1.0) / (FrameWidth - 1));
            int y = (int)Math.Round(cell.Y * (rows - 1.0) / (FrameRows - 1));
            buffer.SetPixel(x, top + y, cell.Ansi, cell.Character);
        }
    }

    private static Cell[][] Load(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "resources", "effect_scripts", fileName);
        if (!File.Exists(path)) return [];

        return PokemonRenderer.ParseLines(File.ReadLines(path))
            .Chunk(FrameRows)
            .Select(frame => frame.SelectMany((row, y) => row
                .Take(FrameWidth)
                .Select((cell, x) => new Cell(x, y, cell.Ansi, cell.Character)))
                .Where(cell => cell.Character != ' ')
                .ToArray())
            .ToArray();
    }
}
