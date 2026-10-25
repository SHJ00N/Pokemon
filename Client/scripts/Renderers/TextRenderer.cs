using System.Text;

namespace Client;

static class TextRenderer
{
    public static void Write(string text) => Console.Write(text);
    public static void WriteLine(string text = "") => Console.WriteLine(text);

    public static void At(int x, int y, string text)
    {
        Console.SetCursorPosition(x, y);
        Write(text);
    }

    // 한글 등 전각 문자는 두 칸으로 계산한다.
    public static string FitToWidth(string text, int width)
    {
        if (width <= 0) return "";
        int total = text.EnumerateRunes().Sum(Width);
        if (total <= width) return text + new string(' ', width - total);
        var result = new StringBuilder();
        int used = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int size = Width(rune);
            if (used + size > width - 1) break;
            result.Append(rune);
            used += size;
        }
        return result.Append('…').Append(' ', width - used - 1).ToString();
    }

    private static int Width(Rune rune) => rune.Value is >= 0x1100 and <= 0x11FF
        or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7AF
        or >= 0xF900 and <= 0xFAFF or >= 0xFF01 and <= 0xFF60 ? 2 : 1;
}
