using System.Text.RegularExpressions;

namespace TianMu.Modules.Music.Media;

/// <summary>清洗後的曲目身分。</summary>
/// <param name="Title">顯示用歌名。</param>
/// <param name="Artist">顯示用歌手。</param>
/// <param name="SearchCandidates">查歌詞時依序嘗試的（歌手, 歌名）組合。</param>
public sealed record TrackIdentity(string Title, string Artist, IReadOnlyList<(string Artist, string Title)> SearchCandidates);

/// <summary>
/// 把瀏覽器回報的雜亂影片標題洗成乾淨的「歌手 / 歌名」。
/// 例如：「周杰倫 Jay Chou【晴天 Sunny Day】Official MV」→ 歌手「周杰倫 Jay Chou」、歌名「晴天 Sunny Day」。
/// </summary>
public static partial class TrackTitleCleaner
{
    private const string Cjk = @"぀-ヿ㐀-䶿一-鿿가-힯豈-﫿";
    private const string TrimChars = " \t-–—|｜/·・:：~～_";

    // 括號內出現這些字，整個括號就是雜訊
    [GeneratedRegex(
        @"official|music\s*video|lyrics?|audio|visuali[sz]er|video|\bm/?v\b|\bhd\b|\bhq\b|\b4k\b|\b\d{3,4}p\b|remaster|" +
        @"\blive\b|\bcover\b|\bver\b|version|\bfeat\b|\bft\b|featuring|\bprod\b|" +
        @"官方|完整版|高畫質|高画质|高音質|高音质|動態歌詞|动态歌词|歌詞|歌词|字幕|中字|首播|伴奏|純享|纯享|現場|现场|翻唱|音樂錄影帶|音乐录影带",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoiseWords();

    // 各種括號
    [GeneratedRegex(@"\((?<c>[^()]*)\)|\[(?<c>[^\[\]]*)\]|（(?<c>[^（）]*)）|【(?<c>[^【】]*)】|〔(?<c>[^〔〕]*)〕|「(?<c>[^「」]*)」|『(?<c>[^『』]*)』|《(?<c>[^《》]*)》|〈(?<c>[^〈〉]*)〉")]
    private static partial Regex AnyBracket();

    // 通常用來包住歌名的括號
    [GeneratedRegex(@"【(?<c>[^【】]+)】|「(?<c>[^「」]+)」|『(?<c>[^『』]+)』|《(?<c>[^《》]+)》|〈(?<c>[^〈〉]+)〉")]
    private static partial Regex TitleBracket();

    // 結尾沒有括號的雜訊，例如 "Official MV"、"官方完整版MV"
    [GeneratedRegex(
        @"[\s\-–—|｜/]*(official|官方)?\s*(完整版)?\s*(?<![A-Za-z])(music\s*video|lyrics?\s*video|video|m/?v|audio|visuali[sz]er|lyrics?|音樂錄影帶|音乐录影带|官方版)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrailingNoise();

    // 「歌手 - 歌名」分隔符號
    [GeneratedRegex(@"\s+[-–—|｜]\s+|\s*－\s*")]
    private static partial Regex DashSeparator();

    [GeneratedRegex(@"\s*[\(\[（]?\s*\b(feat\.?|ft\.?|featuring)\s+[^\)\]）]*[\)\]）]?", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s+-\s+.*\b(remaster(ed)?|version|live|mono|stereo|edit|mix)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s*-\s*Topic$|VEVO$|\s*(Official|官方)\s*(YouTube)?\s*(Channel|頻道|频道)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("[" + Cjk + "]")]
    private static partial Regex HasCjk();

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex HasLatin();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9'’.&!?\s]*")]
    private static partial Regex LatinRun();

    [GeneratedRegex("[" + Cjk + "]+")]
    private static partial Regex CjkRun();

    private static readonly string[] BrowserHints =
        ["chrome", "msedge", "firefox", "308046b0af4a39cb", "brave", "opera", "vivaldi", "thebrowsercompany", "youtube"];

    public static bool IsBrowser(string sourceApp)
    {
        string s = sourceApp.ToLowerInvariant();
        return BrowserHints.Any(s.Contains);
    }

    public static TrackIdentity Clean(string rawTitle, string rawArtist, string sourceApp)
    {
        string title = Tidy(rawTitle);
        string channel = CleanArtist(rawArtist);

        if (!IsBrowser(sourceApp))
        {
            // 正規播放器（Spotify 等）的資訊本來就乾淨，只需替查詢多準備幾種寫法
            var list = new List<(string, string)>();
            Add(list, channel, title);
            Add(list, channel, Tidy(VersionSuffix().Replace(RemoveNoiseBrackets(title), "")));
            Add(list, channel, Tidy(Featuring().Replace(title, "")));
            return new TrackIdentity(title, channel, list);
        }

        // ── 瀏覽器（YouTube 等）：標題通常是「歌手 - 歌名 (Official MV)」之類 ──
        string work = RemoveNoiseBrackets(title);
        work = RemoveTrailingNoise(work);

        string song = work;
        string? titleArtist = null;

        var quoted = TitleBracket().Match(work);
        if (quoted.Success)
        {
            song = Tidy(quoted.Groups["c"].Value);
            string outside = Tidy(work.Remove(quoted.Index, quoted.Length));
            if (outside.Length > 0) titleArtist = outside;
        }
        else
        {
            var parts = DashSeparator().Split(work, 2);
            if (parts.Length == 2 && Tidy(parts[0]).Length > 0 && Tidy(parts[1]).Length > 0)
            {
                titleArtist = Tidy(parts[0]);
                song = Tidy(parts[1]);
            }
        }

        song = Tidy(Featuring().Replace(song, ""));
        if (song.Length == 0) song = title;   // 洗過頭就退回原標題

        string displayArtist = titleArtist ?? channel;

        var candidates = new List<(string, string)>();
        Add(candidates, displayArtist, song);
        if (titleArtist is not null) Add(candidates, channel, song);
        foreach (string a in Variants(displayArtist))
            foreach (string t in Variants(song))
                Add(candidates, a, t);
        Add(candidates, string.Empty, song);

        return new TrackIdentity(song, displayArtist, candidates);
    }

    public static string CleanArtist(string artist)
    {
        string s = Tidy(artist);
        string cleaned = Tidy(ArtistNoise().Replace(s, ""));
        return cleaned.Length > 0 ? cleaned : s;
    }

    private static string RemoveNoiseBrackets(string s)
    {
        string result = AnyBracket().Replace(s, m => NoiseWords().IsMatch(m.Groups["c"].Value) ? " " : m.Value);
        return Tidy(result);
    }

    private static string RemoveTrailingNoise(string s)
    {
        string current = s;
        while (true)
        {
            string next = Tidy(TrailingNoise().Replace(current, ""));
            if (next == current || next.Length == 0) return current;
            current = next;
        }
    }

    /// <summary>中英並列的名字拆成中文版與英文版，例如「周杰倫 Jay Chou」→「周杰倫」、「Jay Chou」。</summary>
    private static IEnumerable<string> Variants(string s)
    {
        yield return s;
        if (!HasCjk().IsMatch(s) || !HasLatin().IsMatch(s)) yield break;

        string cjkOnly = Tidy(LatinRun().Replace(s, " "));
        string latinOnly = Tidy(CjkRun().Replace(s, " "));
        if (cjkOnly.Length > 0) yield return cjkOnly;
        if (HasLatin().IsMatch(latinOnly)) yield return latinOnly;
    }

    private static void Add(List<(string, string)> list, string artist, string title)
    {
        artist = Tidy(artist);
        title = Tidy(title);
        if (title.Length == 0) return;
        if (list.Any(x => string.Equals(x.Item1, artist, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(x.Item2, title, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add((artist, title));
    }

    private static string Tidy(string s) =>
        Spaces().Replace(s.Replace('　', ' '), " ").Trim().Trim(TrimChars.ToCharArray()).Trim();
}
