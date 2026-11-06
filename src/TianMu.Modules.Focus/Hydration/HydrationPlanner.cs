using System.IO;
using System.Text.Json;

namespace TianMu.Modules.Focus.Hydration;

/// <summary>飲水紀錄（每天自動歸零）。</summary>
public sealed class HydrationState
{
    public DateTime Day { get; set; }
    public int Cups { get; set; }
    public DateTime? LastDrink { get; set; }
    public DateTime? SnoozedUntil { get; set; }

    /// <summary>上一次跳出提醒的時間（「指定時刻」模式用來避免同一個時刻重複提醒）。</summary>
    public DateTime? LastPrompted { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static HydrationState Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<HydrationState>(File.ReadAllText(path), Json) ?? new();
        }
        catch { /* 損毀就重新開始 */ }
        return new HydrationState();
    }

    public void Save(string path)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// 決定什麼時候該提醒喝水。純邏輯，所有方法都傳入現在時間。
///
/// 規則：
///   · 距離上次喝水（或今天開始使用）滿一個間隔就提醒
///   · 只在「清醒時段」提醒（預設 08:00–22:00）
///   · 按「稍後」延後 15 分鐘
///   · 達成今日目標後不再提醒
/// </summary>
public sealed class HydrationPlanner(TimeSpan interval, int dailyGoal, TimeOnly dayStart, TimeOnly dayEnd)
{
    public static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(15);

    public static HydrationPlanner Default { get; } =
        new(TimeSpan.FromMinutes(45), 8, new TimeOnly(8, 0), new TimeOnly(22, 0));

    public TimeSpan Interval { get; } = interval;
    public int DailyGoal { get; } = dailyGoal;

    /// <summary>換日時把杯數歸零。</summary>
    public void RollDay(HydrationState state, DateTime now)
    {
        if (state.Day.Date == now.Date) return;
        state.Day = now.Date;
        state.Cups = 0;
        state.SnoozedUntil = null;
    }

    public bool IsWithinActiveHours(DateTime now, bool limitToDaytime)
    {
        if (!limitToDaytime) return true;
        var t = TimeOnly.FromDateTime(now);
        return t >= dayStart && t < dayEnd;
    }

    /// <summary>下一次該提醒的時刻。</summary>
    /// <param name="activeSince">本次開機使用天幕的起點（沒有喝水紀錄時以它起算）。</param>
    public DateTime NextDue(HydrationState state, DateTime activeSince)
    {
        DateTime reference = activeSince;
        if (state.LastDrink is { } last && last > reference) reference = last;

        DateTime due = reference + Interval;
        if (state.SnoozedUntil is { } snooze && snooze > due) due = snooze;
        return due;
    }

    /// <summary>
    /// 「指定時刻」模式：到了某個時刻、而且那之後還沒喝水也還沒提醒過，就該提醒。
    /// 錯過超過一小時的時刻不補提醒（例如晚上才開機，不會一次跳出早上的那幾個）。
    /// </summary>
    public bool IsDueAt(HydrationState state, DateTime now, IReadOnlyList<TimeOnly> times)
    {
        RollDay(state, now);
        if (state.Cups >= DailyGoal || times.Count == 0) return false;
        if (state.SnoozedUntil is { } snooze) return now >= snooze;

        DateTime? slot = null;
        foreach (var t in times)
        {
            var at = now.Date + t.ToTimeSpan();
            if (at <= now && (slot is null || at > slot)) slot = at;
        }
        if (slot is not { } s || now - s > TimeSpan.FromHours(1)) return false;
        if (state.LastDrink is { } drink && drink >= s) return false;
        if (state.LastPrompted is { } prompted && prompted >= s) return false;
        return true;
    }

    /// <summary>解析「10:00, 14:30、16:00」這類時刻清單；看不懂的略過，結果由早到晚排好。</summary>
    public static IReadOnlyList<TimeOnly> ParseTimes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new SortedSet<TimeOnly>();
        foreach (string raw in text.Split([',', '，', '、', ';', '；', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim().Replace('：', ':');
            // 「10」「10:00」「9:5」都接受；看不懂的略過
            var hm = part.Split(':');
            if (hm.Length is < 1 or > 2) continue;
            if (!int.TryParse(hm[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int h)) continue;
            int m = 0;
            if (hm.Length == 2 && !int.TryParse(hm[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out m)) continue;
            if (h is < 0 or > 23 || m is < 0 or > 59) continue;
            result.Add(new TimeOnly(h, m));
        }
        return [.. result];
    }

    public bool IsDue(HydrationState state, DateTime now, DateTime activeSince, bool limitToDaytime)
    {
        RollDay(state, now);
        if (state.Cups >= DailyGoal) return false;
        if (!IsWithinActiveHours(now, limitToDaytime)) return false;
        return now >= NextDue(state, activeSince);
    }

    public void RecordDrink(HydrationState state, DateTime now)
    {
        RollDay(state, now);
        state.Cups++;
        state.LastDrink = now;
        state.SnoozedUntil = null;
    }

    public void Snooze(HydrationState state, DateTime now)
    {
        // 先處理換日，否則下一次檢查時的換日歸零會把剛設定的延後一併清掉
        RollDay(state, now);
        state.SnoozedUntil = now + SnoozeTime;
    }
}
