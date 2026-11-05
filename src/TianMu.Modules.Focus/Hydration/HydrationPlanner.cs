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

    public void Snooze(HydrationState state, DateTime now) => state.SnoozedUntil = now + SnoozeTime;
}
