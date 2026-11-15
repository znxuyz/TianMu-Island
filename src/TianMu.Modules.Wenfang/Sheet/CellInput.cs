using System.Globalization;
using ClosedXML.Excel;

namespace TianMu.Modules.Wenfang.Sheet;

/// <summary>
/// 使用者在儲存格裡打的字要變成什麼（純邏輯，方便測試）：
///   「=SUM(A1:A3)」公式、「12%」百分比、「1,234.5」數字、「2026/11/15」日期、「TRUE」布林、「'007」強制文字，其餘是文字。
/// </summary>
public static class CellInput
{
    public enum Kind { Empty, Formula, Number, Percent, Date, Boolean, Text }

    public readonly record struct Parsed(Kind Kind, double Number = 0, DateTime Date = default, bool Bool = false, string Text = "");

    private static readonly string[] DateFormats =
        ["yyyy/M/d", "yyyy-M-d", "yyyy/M/d H:mm", "yyyy-M-d H:mm", "yyyy/M/d H:mm:ss", "M/d/yyyy", "yyyy年M月d日"];

    public static Parsed Parse(string? input)
    {
        string text = input ?? "";
        if (text.Length == 0) return new(Kind.Empty);
        if (text.StartsWith('=') && text.Length > 1) return new(Kind.Formula, Text: text[1..]);
        if (text.StartsWith('\'')) return new(Kind.Text, Text: text[1..]);
        string t = text.Trim();
        if (t.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new(Kind.Boolean, Bool: true);
        if (t.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new(Kind.Boolean, Bool: false);
        if (t.EndsWith('%') && TryNumber(t[..^1], out double pct)) return new(Kind.Percent, Number: pct / 100);
        if (TryNumber(t, out double n)) return new(Kind.Number, Number: n);
        if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return new(Kind.Date, Date: d);
        return new(Kind.Text, Text: text);
    }

    private static bool TryNumber(string s, out double value)
    {
        s = s.Replace(",", "").Replace("NT$", "").Replace("$", "").Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>把輸入寫進儲存格；數字格式（百分比、日期）跟著設好，像 Excel 一樣。</summary>
    public static void Apply(IXLCell cell, string? input)
    {
        var p = Parse(input);
        switch (p.Kind)
        {
            case Kind.Empty:
                cell.Clear(XLClearOptions.Contents);
                break;
            case Kind.Formula:
                cell.FormulaA1 = p.Text;
                break;
            case Kind.Number:
                cell.Value = p.Number;
                break;
            case Kind.Percent:
                cell.Value = p.Number;
                if (!cell.Style.NumberFormat.Format.Contains('%')) cell.Style.NumberFormat.Format = "0%";
                break;
            case Kind.Date:
                cell.Value = p.Date;
                if (string.IsNullOrEmpty(cell.Style.NumberFormat.Format) || cell.Style.NumberFormat.Format == "General")
                    cell.Style.NumberFormat.Format = p.Date.TimeOfDay == TimeSpan.Zero ? "yyyy/m/d" : "yyyy/m/d h:mm";
                break;
            case Kind.Boolean:
                cell.Value = p.Bool;
                break;
            default:
                cell.Value = p.Text;
                break;
        }
    }

    /// <summary>儲存格「原本打的字」：公式顯示公式，數字顯示完整數值（給編輯與公式列用）。</summary>
    public static string Raw(IXLCell cell)
    {
        if (cell.HasFormula) return "=" + cell.FormulaA1;
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsBoolean) return v.GetBoolean() ? "TRUE" : "FALSE";
        if (v.IsDateTime)
        {
            var d = v.GetDateTime();
            return d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy/M/d", CultureInfo.InvariantCulture) : d.ToString("yyyy/M/d H:mm", CultureInfo.InvariantCulture);
        }
        if (v.IsNumber)
        {
            double n = v.GetNumber();
            return cell.Style.NumberFormat.Format.Contains('%')
                ? (n * 100).ToString("G15", CultureInfo.InvariantCulture) + "%"
                : n.ToString("G15", CultureInfo.InvariantCulture);
        }
        return v.IsText ? v.GetText() : v.ToString();
    }

    /// <summary>儲存格顯示的字（套用數字格式）；公式出錯時顯示 Excel 的錯誤碼。</summary>
    public static string Display(IXLCell cell)
    {
        try { return cell.GetFormattedString(CultureInfo.CurrentCulture); }
        catch (Exception) { return "#VALUE!"; }
    }

    // ── 欄名與位址 ──

    /// <summary>1 → A、27 → AA。</summary>
    public static string ColumnName(int col)
    {
        string s = "";
        for (int c = col; c > 0; c = (c - 1) / 26) s = (char)('A' + (c - 1) % 26) + s;
        return s;
    }

    public static string Address(int row, int col) => ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);

    /// <summary>「B12」→ (12, 2)；看不懂回傳 null。</summary>
    public static (int Row, int Col)? ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().ToUpperInvariant().Replace("$", "");
        int i = 0, col = 0;
        while (i < text.Length && text[i] is >= 'A' and <= 'Z') { col = col * 26 + (text[i] - 'A' + 1); i++; }
        if (i == 0 || i == text.Length || col > 16384) return null;
        return int.TryParse(text[i..], NumberStyles.None, CultureInfo.InvariantCulture, out int row) && row is >= 1 and <= 1048576 ? (row, col) : null;
    }

    // ── 數字格式 ──

    public static readonly (string Label, string Format)[] Formats =
    [
        ("通用格式", "General"), ("數值", "#,##0.00"), ("貨幣", "\"NT$\"#,##0"), ("百分比", "0%"),
        ("日期", "yyyy/m/d"), ("時間", "h:mm"), ("文字", "@"),
    ];

    /// <summary>增加或減少小數位數（「0.00」→「0.000」）。</summary>
    public static string AdjustDecimals(string format, int delta)
    {
        if (string.IsNullOrEmpty(format) || format == "General") format = "0";
        int dot = format.IndexOf('.');
        int zeros = 0;
        if (dot >= 0) for (int i = dot + 1; i < format.Length && format[i] == '0'; i++) zeros++;
        int target = Math.Clamp(zeros + delta, 0, 10);
        string head = dot >= 0 ? format[..dot] : TrimTail(format, out _);
        string tail = dot >= 0 ? format[(dot + 1 + zeros)..] : TailOf(format);
        return target == 0 ? head + tail : head + "." + new string('0', target) + tail;
    }

    private static string TrimTail(string f, out string tail)
    {
        int i = f.Length;
        while (i > 0 && !char.IsDigit(f[i - 1]) && f[i - 1] != '#') i--;
        tail = f[i..];
        return f[..i];
    }

    private static string TailOf(string f)
    {
        TrimTail(f, out var tail);
        return tail;
    }
}
