using System.Globalization;

namespace TianMu.Island.Time;

/// <summary>
/// 農曆與時辰。全部以 .NET 內建的 <see cref="ChineseLunisolarCalendar"/> 計算，不需要連網。
/// </summary>
public static class Lunar
{
    private static readonly ChineseLunisolarCalendar Calendar = new();

    private static readonly string[] Stems = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];
    private static readonly string[] Branches = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];
    private static readonly string[] Months = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "臘"];
    private static readonly string[] Digits = ["", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];

    /// <summary>干支紀年，例如「丙午」。超出曆法範圍時回傳 null。</summary>
    public static string? GanZhiYear(DateTime date)
    {
        try
        {
            int year = Calendar.GetSexagenaryYear(date);
            return Stems[Calendar.GetCelestialStem(year) - 1] + Branches[Calendar.GetTerrestrialBranch(year) - 1];
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>時辰，例如「未」。子時 = 23:00–00:59，丑時 = 01:00–02:59 ……</summary>
    public static string Shichen(DateTime date) => Branches[(date.Hour + 1) / 2 % 12];

    /// <summary>農曆月日，例如「九月廿七」、「閏六月初一」。超出曆法範圍時回傳 null。</summary>
    public static string? MonthDay(DateTime date)
    {
        try
        {
            int year = Calendar.GetYear(date);
            int month = Calendar.GetMonth(date);
            int day = Calendar.GetDayOfMonth(date);

            // 有閏月的年份共 13 個月：閏月以及其後的月份序號都會多 1
            int leap = Calendar.GetLeapMonth(year);
            bool isLeap = leap > 0 && month == leap;
            int real = leap > 0 && month >= leap ? month - 1 : month;

            return (isLeap ? "閏" : "") + MonthName(real) + "月" + DayName(day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static string MonthName(int month) => Months[month - 1];

    public static string DayName(int day) => day switch
    {
        <= 10 => "初" + Digits[day],
        < 20 => "十" + Digits[day - 10],
        20 => "二十",
        < 30 => "廿" + Digits[day - 20],
        _ => "三十",
    };
}
