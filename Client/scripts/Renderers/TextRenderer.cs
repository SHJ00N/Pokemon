using System.Text;
using System.Text.RegularExpressions;

namespace Client;

static class TextRenderer
{
    private static readonly Regex AnsiCode = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    public static void Write(string text) => Console.Write(text);
    public static void WriteLine(string text = "") => Console.WriteLine(text);

    // 보이는 화면과 위로 스크롤되는 이전 출력까지 지운다.
    // Console.Clear()만 쓰면 터미널의 스크롤 기록에 이전 화면이 남는다.
    public static void Clear()
    {
        Console.Clear();
        Console.Write("\x1b[3J");
    }

    public static void At(int x, int y, string text)
    {
        Console.SetCursorPosition(x, y);
        Write(text);
    }

    // 마지막 글자의 받침 유무에 따라 조사를 고른다. 예: Josa("이상해씨", "이", "가") → "가"
    public static string Josa(string word, string afterFinal, string afterVowel)
    {
        char last = word[^1];
        bool hasFinal = last is >= '가' and <= '힣' && (last - '가') % 28 != 0;
        return hasFinal ? afterFinal : afterVowel;
    }

    // 터미널에 표시되는 칸 수. 한글 등 전각 문자는 두 칸이다.
    public static int DisplayWidth(string text) => AnsiCode.Replace(text, "").EnumerateRunes().Sum(Width);

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
