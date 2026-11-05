using System.Globalization;
using System.Text.Json;

namespace TianMu.Modules.Ai.Claude;

/// <summary>一個額度視窗（例如「5 小時」）。</summary>
/// <param name="UsedPercent">已用掉的百分比（0~100）。</param>
/// <param name="ResetsAt">何時重置；未知時為 null。</param>
public sealed record UsageWindow(double UsedPercent, DateTimeOffset? ResetsAt)
{
    /// <summary>剩餘百分比（0~100）。</summary>
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

/// <summary>某一時刻的 Claude 額度。</summary>
public sealed record ClaudeUsage(UsageWindow? FiveHour, UsageWindow? SevenDay, DateTimeOffset FetchedAt);

/// <summary>
/// 解析 Claude 的額度回應。
///
/// 注意：Anthropic 沒有公開「訂閱方案額度」的官方 API。這裡解析的是 Claude 網頁與 Claude Code 內部使用的格式：
///   { "five_hour": { "utilization": 45, "resets_at": "2026-11-05T10:00:00Z" },
///     "seven_day": { "utilization": 27, "resets_at": "..." } }
/// utilization 是「已用百分比」。格式若改變，這裡會丟出例外，讓畫面顯示「格式已變更」而不是錯誤的數字。
/// </summary>
public static class ClaudeUsageParser
{
    public static ClaudeUsage Parse(string json, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("額度回應不是 JSON 物件。");

        var fiveHour = ReadWindow(root, "five_hour");
        var sevenDay = ReadWindow(root, "seven_day");
        if (fiveHour is null && sevenDay is null)
            throw new FormatException("額度回應裡沒有 five_hour 或 seven_day。");

        return new ClaudeUsage(fiveHour, sevenDay, fetchedAt);
    }

    private static UsageWindow? ReadWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return null;

        DateTimeOffset? resetsAt = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetsAt = parsed;
        }

        return new UsageWindow(u.GetDouble(), resetsAt);
    }

    /// <summary>「今天 18:00 重置」、「明天 08:00 重置」、「11月6日 08:00 重置」。</summary>
    public static string ResetText(DateTimeOffset? resetsAt, DateTime now)
    {
        if (resetsAt is null) return string.Empty;
        var local = resetsAt.Value.ToLocalTime().DateTime;
        string time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (local.Date == now.Date) return $"今天 {time} 重置";
        if (local.Date == now.Date.AddDays(1)) return $"明天 {time} 重置";
        return $"{local.Month}月{local.Day}日 {time} 重置";
    }

    /// <summary>百分比顯示：「55%」。</summary>
    public static string PercentText(double percent) =>
        ((int)Math.Round(percent, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";
}
