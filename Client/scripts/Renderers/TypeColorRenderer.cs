using System.Globalization;

namespace Client;

static class TypeColorRenderer
{
    public static void WriteMarker(string colorRgb)
    {
        if (colorRgb.Length != 7 || colorRgb[0] != '#' ||
            !uint.TryParse(colorRgb.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new InvalidDataException($"잘못된 타입 색상: {colorRgb}");

        Console.Write($"\x1b[38;2;{rgb >> 16};{(rgb >> 8) & 255};{rgb & 255}m■\x1b[0m");
    }
}
