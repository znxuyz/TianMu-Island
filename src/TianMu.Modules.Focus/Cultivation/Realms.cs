using TianMu.Core;

namespace TianMu.Modules.Focus.Cultivation;

/// <summary>某個修為值對應的境界與小境界。</summary>
/// <param name="Realm">大境界。</param>
/// <param name="Stage">小境界：0 = 初期、1 = 中期、2 = 後期。</param>
/// <param name="ProgressInRealm">在本境界內的進度（0~1）。</param>
/// <param name="MinutesToNextRealm">距離下一個大境界還差幾分鐘；已是最高境界時為 null。</param>
public readonly record struct RealmStanding(IslandRealm Realm, int Stage, double ProgressInRealm, int? MinutesToNextRealm)
{
    public string RealmName => Realms.Name(Realm);
    public string StageName => Realms.StageNames[Stage];

    /// <summary>例如「築基 · 中期」。</summary>
    public string Title => $"{RealmName} · {StageName}";
}

/// <summary>
/// 修為五境的門檻（累積專注時間）：
///   練氣 0 時、築基 10 時、金丹 50 時、元嬰 150 時、化神 500 時。
/// 每個大境界再均分為初期、中期、後期。
/// </summary>
public static class Realms
{
    public static readonly string[] StageNames = ["初期", "中期", "後期"];

    /// <summary>各境界的起始門檻（分鐘）。</summary>
    public static readonly int[] ThresholdMinutes = [0, 10 * 60, 50 * 60, 150 * 60, 500 * 60];

    /// <summary>化神沒有上限，計算小境界時以 1000 時為「後期」的終點。</summary>
    private const int HuaShenCapMinutes = 1000 * 60;

    public static string Name(IslandRealm realm) => realm switch
    {
        IslandRealm.LianQi => "練氣",
        IslandRealm.ZhuJi => "築基",
        IslandRealm.JinDan => "金丹",
        IslandRealm.YuanYing => "元嬰",
        _ => "化神",
    };

    public static RealmStanding Evaluate(long totalMinutes)
    {
        long minutes = Math.Max(0, totalMinutes);
        int index = 0;
        for (int i = ThresholdMinutes.Length - 1; i >= 0; i--)
        {
            if (minutes >= ThresholdMinutes[i]) { index = i; break; }
        }

        long start = ThresholdMinutes[index];
        bool isTop = index == ThresholdMinutes.Length - 1;
        long end = isTop ? HuaShenCapMinutes : ThresholdMinutes[index + 1];

        double progress = Math.Clamp((double)(minutes - start) / (end - start), 0, 1);
        int stage = Math.Min(2, (int)(progress * 3));
        int? toNext = isTop ? null : (int)(end - minutes);

        return new RealmStanding((IslandRealm)index, stage, progress, toNext);
    }

    /// <summary>「2 時 40 分」、「35 分」。</summary>
    public static string FormatDuration(long minutes)
    {
        if (minutes < 60) return $"{minutes} 分";
        long h = minutes / 60, m = minutes % 60;
        return m == 0 ? $"{h} 時" : $"{h} 時 {m} 分";
    }
}
