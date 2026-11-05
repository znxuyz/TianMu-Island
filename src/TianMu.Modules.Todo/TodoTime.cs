using System.Globalization;
using System.Text.RegularExpressions;

namespace TianMu.Modules.Todo;

/// <summary>
/// 把使用者輸入的時間轉成實際時刻。支援：
///   · 相對時間：「30 分」「30分鐘」「2 小時」「1.5h」「45m」
///   · 時刻：「15:30」「3 點」「3點半」「下午 3 點 15 分」「晚上 8 點」
///   · 日期：「明天 9 點」「後天 14:00」「11/20 9:00」「2026/11/20 9:00」
/// 只給時刻且已經過了，就當作明天。
/// </summary>
public static partial class TodoTime
{
    public static DateTime? Parse(string? input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string s = Normalize(input);

        // ── 相對時間 ──
        var rel = RelativeRegex().Match(s.Replace(" ", ""));
        if (rel.Success)
        {
            double n = double.Parse(rel.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (n <= 0 || n > 24 * 30) return null;
            bool hours = rel.Groups["u"].Value is "小時" or "小时" or "時" or "h" or "hr" or "hrs" or "hour" or "hours";
            return Trim(now + (hours ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n)));
        }

        // ── 日期 ──
        DateTime? date = null;
        if (s.StartsWith("今天", StringComparison.Ordinal)) { date = now.Date; s = s[2..]; }
        else if (s.StartsWith("明天", StringComparison.Ordinal) || s.StartsWith("明早", StringComparison.Ordinal) || s.StartsWith("明晚", StringComparison.Ordinal))
        {
            date = now.Date.AddDays(1);
            s = s.StartsWith("明天", StringComparison.Ordinal) ? s[2..] : (s[1] == '早' ? "早上" : "晚上") + s[2..];
        }
        else if (s.StartsWith("後天", StringComparison.Ordinal) || s.StartsWith("后天", StringComparison.Ordinal)) { date = now.Date.AddDays(2); s = s[2..]; }
        else if (s.StartsWith("今晚", StringComparison.Ordinal)) { date = now.Date; s = "晚上" + s[2..]; }

        s = s.TrimStart();
        var d = DateRegex().Match(s);
        if (d.Success)
        {
            int year = d.Groups["y"].Success ? int.Parse(d.Groups["y"].Value, CultureInfo.InvariantCulture) : now.Year;
            int month = int.Parse(d.Groups["m"].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(d.Groups["d"].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
            date = new DateTime(year, month, day);
            // 沒寫年份、而且日期已經過了，就是明年
            if (!d.Groups["y"].Success && date < now.Date) date = date.Value.AddYears(1);
            s = s[d.Length..];
        }

        s = s.Replace(" ", "");
        if (s.Length == 0)
            return date is { } onlyDate ? onlyDate.AddHours(9) : null; // 只給日期：當天早上 9 點

        // ── 時刻 ──
        var t = TimeRegex().Match(s);
        if (!t.Success) return null;

        int hour = int.Parse(t.Groups["h"].Value, CultureInfo.InvariantCulture);
        int minute = t.Groups["half"].Success ? 30
            : t.Groups["min"].Success ? int.Parse(t.Groups["min"].Value, CultureInfo.InvariantCulture)
            : 0;
        string period = t.Groups["p"].Success ? t.Groups["p"].Value : t.Groups["p2"].Value;
        if (period is "下午" or "晚上" or "傍晚" or "pm" && hour < 12) hour += 12;
        if (period is "中午" && hour < 11) hour += 12;
        if (period is "凌晨" or "早上" or "上午" or "am" && hour == 12) hour = 0;
        if (hour > 23 || minute > 59) return null;

        var at = (date ?? now.Date).AddHours(hour).AddMinutes(minute);
        if (date is null && at <= now) at = at.AddDays(1); // 只給時刻而且過了，就是明天
        return at;
    }

    /// <summary>「11/6（四）15:30」或「今天 15:30」這類給人看的寫法。</summary>
    public static string Describe(DateTime at, DateTime now)
    {
        string time = at.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (at.Date == now.Date) return $"今天 {time}";
        if (at.Date == now.Date.AddDays(1)) return $"明天 {time}";
        if (at.Date == now.Date.AddDays(-1)) return $"昨天 {time}";
        string week = "日一二三四五六"[(int)at.DayOfWeek].ToString();
        return $"{at.Month}/{at.Day}（{week}）{time}";
    }

    private static DateTime Trim(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);

    private static string Normalize(string input)
    {
        // 全形數字與符號轉半形
        var chars = input.Trim().ToLowerInvariant().Select(c => c switch
        {
            >= '０' and <= '９' => (char)('0' + (c - '０')),
            '：' => ':',
            '／' => '/',
            '．' => '.',
            '＋' => '+',
            _ => c,
        });
        // 空白收成一格（日期與時刻之間的空白要保留，「11/2 9:00」才不會被讀成 11/29）
        string text = Regex.Replace(string.Concat(chars), @"\s+", " ");
        return text.Replace("點鐘", "點").Replace("点", "點").Replace("后", "後");
    }

    [GeneratedRegex(@"^\+?(?<n>\d+(\.\d+)?)(?<u>分鐘|分钟|分|m|min|mins|minutes?|小時|小时|時|h|hr|hrs|hours?)後?$")]
    private static partial Regex RelativeRegex();

    [GeneratedRegex(@"^((?<y>\d{4})[/\-.年])?(?<m>\d{1,2})[/\-.月](?<d>\d{1,2})日?")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(?<p>凌晨|早上|上午|中午|下午|傍晚|晚上|am|pm)?(?<h>\d{1,2})((:(?<min>\d{2}))|(點((?<half>半)|(?<min>\d{1,2})分?)?))?(?<p2>am|pm)?$")]
    private static partial Regex TimeRegex();
}
