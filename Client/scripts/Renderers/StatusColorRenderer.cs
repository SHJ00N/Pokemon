namespace Client;

// 전투 화면의 상태이상 표시([마비])와 능력치 변화 줄(공격 +2)을 색으로 구분해 출력한다.
static class StatusColorRenderer
{
    private static readonly Dictionary<string, string> StatusColors = new()
    {
        ["마비"] = "#F7D02C",
        ["화상"] = "#EE8130",
        ["독"] = "#B565D9",
        ["맹독"] = "#E040FB",
        ["잠듦"] = "#A8A8A8",
        ["얼음"] = "#7FDBFF",
    };

    private const string RaisedColor = "#4CD964";   // 오른 능력치
    private const string LoweredColor = "#FF5A5A";  // 내려간 능력치
    private const string EffectColor = "#5BC0EB";   // 기충전·아쿠아링·뿌리박기

    public static string FormatStatus(string status) =>
        TypeColorRenderer.Colorize($"[{status}]", StatusColors.GetValueOrDefault(status, "#FFFFFF"));

    public static void WriteStatus(string status) => TextRenderer.Write(FormatStatus(status));

    public static string FormatStatChange(string change) => TypeColorRenderer.Colorize(change,
        change.Contains('+') ? RaisedColor : change.Contains('-') ? LoweredColor : EffectColor);

    // 능력치 변화를 한 줄로 출력한다. maxWidth를 넘는 항목은 출력하지 않는다.
    public static void WriteStatChanges(IEnumerable<string> changes, int maxWidth)
    {
        int used = 0;
        foreach (string change in changes)
        {
            int width = TextRenderer.DisplayWidth(change) + (used > 0 ? 1 : 0);
            if (used + width > maxWidth)
                break;
            if (used > 0)
                TextRenderer.Write(" ");
            TextRenderer.Write(FormatStatChange(change));
            used += width;
        }
    }
}
