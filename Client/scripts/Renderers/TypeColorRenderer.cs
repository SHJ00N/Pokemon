using System.Globalization;

namespace Client;

static class TypeColorRenderer
{
    public static void WriteMarker(string colorRgb) => WriteColored("■", colorRgb);

    // colorRgb(#RRGGBB) 글자색으로 text를 출력하고 색을 원래대로 돌린다.
    public static void WriteColored(string text, string colorRgb) => Console.Write(Colorize(text, colorRgb));

    public static string Colorize(string text, string colorRgb)
    {
        if (colorRgb.Length != 7 || colorRgb[0] != '#' ||
            !uint.TryParse(colorRgb.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new InvalidDataException($"잘못된 타입 색상: {colorRgb}");

        return $"\x1b[38;2;{rgb >> 16};{(rgb >> 8) & 255};{rgb & 255}m{text}\x1b[0m";
    }
}
