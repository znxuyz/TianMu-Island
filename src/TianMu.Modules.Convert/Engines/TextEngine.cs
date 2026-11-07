using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TianMu.Modules.Convert.Furnace;

namespace TianMu.Modules.Convert.Engines;

/// <summary>文字檔轉 UTF-8、字幕 SRT ↔ VTT（純邏輯部分可測試）。</summary>
public static partial class TextEngine
{
    static TextEngine() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// 讀出文字：有 BOM 照 BOM；是合法的 UTF-8 就當 UTF-8；否則依序試繁中 Big5、簡中 GBK、日文 Shift-JIS，
    /// 取「看起來最不像亂碼」的那一個。
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { /* 不是 UTF-8 */ }

        string best = string.Empty;
        int bestBad = int.MaxValue;
        foreach (int codePage in new[] { 950, 936, 932 })
        {
            string text = Encoding.GetEncoding(codePage).GetString(bytes);
            int bad = text.Count(c => c == '�' || (c >= '' && c <= ''));
            if (bad < bestBad) { best = text; bestBad = bad; }
        }
        return best;
    }

    public static string ToUtf8File(string source, string outDir)
    {
        string text = Decode(File.ReadAllBytes(source));
        string path = OutputNames.Unique(outDir, OutputNames.For(source, Path.GetExtension(source), "UTF8"), File.Exists);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string ConvertSubtitle(string source, string outDir, string target)
    {
        string text = Decode(File.ReadAllBytes(source));
        string result = target == "vtt" ? SrtToVtt(text) : VttToSrt(text);
        string path = OutputNames.Unique(outDir, OutputNames.For(source, target), File.Exists);
        File.WriteAllText(path, result, new UTF8Encoding(false));
        return path;
    }

    /// <summary>SRT → WebVTT：加上檔頭，時間的逗號改成點。</summary>
    public static string SrtToVtt(string srt)
    {
        string body = Newlines(srt).Trim();
        body = SrtTime().Replace(body, m => m.Value.Replace(',', '.'));
        return "WEBVTT\n\n" + body + "\n";
    }

    /// <summary>WebVTT → SRT：去掉檔頭與註解區塊、重新編號、補齊小時、去掉位置設定。</summary>
    public static string VttToSrt(string vtt)
    {
        var blocks = Newlines(vtt).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        int n = 0;
        foreach (string raw in blocks)
        {
            var lines = raw.Trim('\n').Split('\n').ToList();
            int timeLine = lines.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
            if (timeLine < 0) continue; // WEBVTT 檔頭、NOTE、STYLE
            var m = VttTime().Match(lines[timeLine]);
            if (!m.Success) continue;
            string start = SrtStamp(m.Groups[1].Value), end = SrtStamp(m.Groups[2].Value);
            var text = lines.Skip(timeLine + 1).Where(l => l.Length > 0);
            sb.Append(++n).Append('\n').Append(start).Append(" --> ").Append(end).Append('\n');
            foreach (string l in text) sb.Append(l).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string SrtStamp(string vtt)
    {
        string s = vtt.Replace('.', ',');
        return s.Count(c => c == ':') == 1 ? "00:" + s : s; // 「01:02.500」→「00:01:02,500」
    }

    private static string Newlines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    [GeneratedRegex(@"\d{1,2}:\d{2}:\d{2},\d{3}")]
    private static partial Regex SrtTime();

    [GeneratedRegex(@"((?:\d{1,2}:)?\d{2}:\d{2}\.\d{3})\s*-->\s*((?:\d{1,2}:)?\d{2}:\d{2}\.\d{3})")]
    private static partial Regex VttTime();
}
