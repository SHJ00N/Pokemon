using System.Text;

namespace Client;

static class AttackImageRenderer
{
    // ANSI를 한 번만 해석하고 프레임마다 재사용한다. 공백은 투명 픽셀이다.
    private static readonly Dictionary<int, (string Ansi, char Character)[][]> images = new();

    private static (string Ansi, char Character)[][] Load(int typeId)
    {
        if (images.TryGetValue(typeId, out var image)) return image;
        string path = Path.Combine(AppContext.BaseDirectory, "resources", "type_scripts",
            $"{typeId:D2}_attack_image.txt");
        image = File.Exists(path)
            ? File.ReadAllLines(path, Encoding.UTF8).Select(line => PokemonRenderer.Parse(line).ToArray()).ToArray()
            : [];
        images[typeId] = image;
        return image;
    }

    // 한 픽셀은 가로 두 글자다. 비율을 유지해 축소하고 전투 그림 영역 안에 배치한다.
    internal static IEnumerable<(int X, int Y, string Ansi, char Character)> GetCells(
        int typeId, int centerX, int centerY, bool flip, int width, int top, int bottom)
    {
        var image = Load(typeId);
        if (image.Length == 0 || bottom <= top || width < 3) yield break;
        int sourcePixels = image.Max(row => row.Length) / 2;
        if (sourcePixels == 0) yield break;
        double scale = Math.Min(1, Math.Min((double)(bottom - top) / image.Length,
            (double)(width - 1) / (sourcePixels * 2)));
        int rows = Math.Max(1, (int)(image.Length * scale));
        int pixels = Math.Max(1, (int)(sourcePixels * scale));
        int left = centerX - pixels;
        int startY = Math.Clamp(centerY - rows / 2, top, bottom - rows);
        for (int row = 0; row < rows; row++)
        {
            var source = image[rows == 1 ? image.Length / 2 : (int)Math.Round((double)row * (image.Length - 1) / (rows - 1))];
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                int sourcePixel = pixels == 1 ? sourcePixels / 2
                    : (int)Math.Round((double)pixel * (sourcePixels - 1) / (pixels - 1));
                if (flip) sourcePixel = sourcePixels - 1 - sourcePixel;
                for (int half = 0; half < 2; half++)
                {
                    int sourceColumn = sourcePixel * 2 + (flip ? 1 - half : half);
                    int x = left + pixel * 2 + half;
                    if (sourceColumn >= source.Length || x < 0 || x >= width - 1) continue;
                    var cell = source[sourceColumn];
                    if (cell.Character != ' ')
                        yield return (x, startY + row, cell.Ansi, cell.Character);
                }
            }
        }
    }

    public static void Draw(int typeId, int centerX, int centerY, bool flip, int width, int top, int bottom,
        BattleSpriteBuffer buffer)
    {
        foreach (var cell in GetCells(typeId, centerX, centerY, flip, width, top, bottom))
        {
            buffer.SetPixel(cell.X, cell.Y, cell.Ansi, cell.Character);
        }
    }
}
