using System.Globalization;

namespace TianMu.Modules.Todo;

// 與解析分開放：TodoTime.cs 裡有簡體字關鍵詞，不列入介面字型；這裡的字會顯示在畫面上。
public static partial class TodoTime
{
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
}
