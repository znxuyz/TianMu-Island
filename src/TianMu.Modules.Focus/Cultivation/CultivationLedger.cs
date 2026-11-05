using System.IO;
using System.Text.Json;
using TianMu.Core;

namespace TianMu.Modules.Focus.Cultivation;

/// <summary>一次累積修為的結果。</summary>
/// <param name="Before">累積前的境界。</param>
/// <param name="After">累積後的境界。</param>
/// <param name="MinutesAdded">實際加上的分鐘數。</param>
public readonly record struct CreditResult(RealmStanding Before, RealmStanding After, int MinutesAdded)
{
    /// <summary>是否突破了大境界（要渡劫）。</summary>
    public bool BrokeThrough => After.Realm > Before.Realm;
}

/// <summary>
/// 修為帳本：記錄累積專注時間與每日紀錄。純資料邏輯，不碰 UI，方便測試。
/// </summary>
public sealed class CultivationLedger
{
    private const int KeepDays = 400;

    public long TotalMinutes { get; set; }

    /// <summary>每日專注分鐘數，鍵為 yyyy-MM-dd。</summary>
    public Dictionary<string, int> Daily { get; set; } = [];

    /// <summary>完成的專注回合數（完整跑完的番茄鐘）。</summary>
    public int CompletedSessions { get; set; }

    public RealmStanding Standing => Realms.Evaluate(TotalMinutes);

    public static string DayKey(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public int MinutesOn(DateTime date) => Daily.TryGetValue(DayKey(date), out int m) ? m : 0;

    /// <summary>加上修為。分鐘數小於等於 0 時不變。</summary>
    public CreditResult Credit(int minutes, DateTime when, bool completedSession)
    {
        var before = Standing;
        if (minutes <= 0) return new CreditResult(before, before, 0);

        TotalMinutes += minutes;
        string key = DayKey(when);
        Daily[key] = (Daily.TryGetValue(key, out int existing) ? existing : 0) + minutes;
        if (completedSession) CompletedSessions++;
        Prune(when);

        return new CreditResult(before, Standing, minutes);
    }

    /// <summary>連續有修煉的天數（今天還沒修煉時，從昨天往回算）。</summary>
    public int StreakDays(DateTime today)
    {
        var day = today.Date;
        if (MinutesOn(day) == 0) day = day.AddDays(-1);

        int streak = 0;
        while (MinutesOn(day) > 0)
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    private void Prune(DateTime now)
    {
        if (Daily.Count <= KeepDays) return;
        string cutoff = DayKey(now.Date.AddDays(-KeepDays));
        foreach (string key in Daily.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList())
            Daily.Remove(key);
    }

    // ── 存檔 ──────────────────────────────────

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static CultivationLedger Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<CultivationLedger>(File.ReadAllText(path), Json) ?? new();
        }
        catch
        {
            // 帳本損毀：留一份備份，從頭開始（不讓程式崩潰）
            try { File.Copy(path, path + ".broken", overwrite: true); } catch { }
        }
        return new CultivationLedger();
    }

    public void Save(string path)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
