using System.Text;

namespace TianMu.Modules.Wenfang.Sheet;

/// <summary>CSV 讀寫（RFC 4180：雙引號包住含逗號、換行、引號的欄位；引號以兩個引號表示）。</summary>
public static class Csv
{
    /// <summary>猜分隔字元：第一行裡逗號、分號、Tab 誰多用誰。</summary>
    public static char DetectSeparator(string text)
    {
        int end = text.IndexOf('\n');
        string first = end < 0 ? text : text[..end];
        int commas = first.Count(c => c == ','), semis = first.Count(c => c == ';'), tabs = first.Count(c => c == '\t');
        return tabs > commas && tabs >= semis ? '\t' : semis > commas ? ';' : ',';
    }

    public static List<List<string>> Parse(string text, char sep = ',')
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
                continue;
            }
            if (ch == '"' && field.Length == 0) { quoted = true; any = true; }
            else if (ch == sep) { row.Add(field.ToString()); field.Clear(); any = true; }
            else if (ch == '\r') { }
            else if (ch == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; any = false; }
            else { field.Append(ch); any = true; }
        }
        if (any || field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    public static string Format(IEnumerable<IEnumerable<string>> rows, char sep = ',')
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            bool first = true;
            foreach (var f in row)
            {
                if (!first) sb.Append(sep);
                first = false;
                bool needs = f.IndexOfAny([sep, '"', '\n', '\r']) >= 0 || (f.Length > 0 && (f[0] == ' ' || f[^1] == ' '));
                sb.Append(needs ? "\"" + f.Replace("\"", "\"\"") + "\"" : f);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }
}
