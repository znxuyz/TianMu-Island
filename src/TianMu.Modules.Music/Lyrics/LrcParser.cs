using System.Globalization;
using System.Text.RegularExpressions;

namespace TianMu.Modules.Music.Lyrics;

/// <summary>一行歌詞。</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>
/// LRC 同步歌詞解析器。支援：
///   [mm:ss] [mm:ss.x] [mm:ss.xx] [mm:ss.xxx] [mm:ss:xx]、一行多個時間標籤、[offset:±毫秒]。
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>\d{1,2})(?:[.:](?<f>\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\s*\[offset:\s*(?<v>[+-]?\d+)\s*\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return lines;

        var offset = TimeSpan.Zero;

        foreach (string raw in lrc.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            var off = OffsetTag().Match(line);
            if (off.Success)
            {
                // 正值代表歌詞要提早出現
                offset = TimeSpan.FromMilliseconds(int.Parse(off.Groups["v"].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var tags = TimeTag().Matches(line);
            if (tags.Count == 0) continue;

            // 文字是最後一個時間標籤之後的部分
            var last = tags[^1];
            string text = line[(last.Index + last.Length)..].Trim();

            foreach (Match tag in tags)
            {
                int minutes = int.Parse(tag.Groups["m"].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(tag.Groups["s"].Value, CultureInfo.InvariantCulture);
                int millis = 0;
                if (tag.Groups["f"].Success)
                {
                    string f = tag.Groups["f"].Value;
                    millis = f.Length switch
                    {
                        1 => int.Parse(f, CultureInfo.InvariantCulture) * 100,
                        2 => int.Parse(f, CultureInfo.InvariantCulture) * 10,
                        _ => int.Parse(f, CultureInfo.InvariantCulture),
                    };
                }

                var time = new TimeSpan(0, 0, minutes, seconds, millis);
                lines.Add(new LyricLine(time, text));
            }
        }

        if (offset != TimeSpan.Zero)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Time - offset;
                lines[i] = lines[i] with { Time = t < TimeSpan.Zero ? TimeSpan.Zero : t };
            }
        }

        // 穩定排序：同一時間的行保持原順序
        return lines.Select((l, i) => (l, i))
                    .OrderBy(x => x.l.Time)
                    .ThenBy(x => x.i)
                    .Select(x => x.l)
                    .ToList();
    }

    /// <summary>找出在 <paramref name="position"/> 時應顯示的那一行索引；還沒開始則回傳 -1。</summary>
    public static int FindIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }
}
