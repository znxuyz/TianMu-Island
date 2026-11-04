using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace TianMu.Modules.Music.Lyrics;

/// <summary>查詢結果。</summary>
/// <param name="Synced">逐句同步歌詞（可能為空）。</param>
/// <param name="Plain">純文字歌詞（可能為 null）。</param>
/// <param name="Instrumental">是否為純音樂。</param>
public sealed record LyricsResult(IReadOnlyList<LyricLine> Synced, string? Plain, bool Instrumental)
{
    public bool HasSynced => Synced.Count > 0;
}

/// <summary>
/// 歌詞來源：LRCLIB（https://lrclib.net）——免費、免金鑰、有逐句時間軸的開放歌詞庫。
/// 只會送出「歌名、歌手」做查詢，不會送出任何個人資料。
/// </summary>
internal sealed class LyricsService : IDisposable
{
    private const string BaseUrl = "https://lrclib.net/api/";

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, LyricsResult?> _cache = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public LyricsService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(8),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TianMu-Island/0.2 (+https://github.com/znxuyz/TianMu-Island)");
    }

    /// <summary>依序嘗試候選（歌手, 歌名），找到第一份同步歌詞就回傳。</summary>
    public async Task<LyricsResult?> FindAsync(
        IReadOnlyList<(string Artist, string Title)> candidates,
        TimeSpan duration,
        CancellationToken ct)
    {
        string cacheKey = string.Join("|", candidates.Take(2).Select(c => $"{c.Artist}/{c.Title}")).ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        LyricsResult? fallback = null;   // 只有純文字歌詞時先記著

        // 最多嘗試 5 組，避免對歌詞庫發太多請求
        foreach (var (artist, title) in candidates.Take(5))
        {
            ct.ThrowIfCancellationRequested();

            string query = artist.Length > 0
                ? $"search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}"
                : $"search?q={Uri.EscapeDataString(title)}";

            var records = await SearchAsync(query, ct);
            var best = PickBest(records, duration);
            if (best is null) continue;

            var result = new LyricsResult(LrcParser.Parse(best.SyncedLyrics), best.PlainLyrics, best.Instrumental);
            if (result.HasSynced || result.Instrumental)
            {
                _cache[cacheKey] = result;
                return result;
            }
            fallback ??= result;
        }

        _cache[cacheKey] = fallback;
        return fallback;
    }

    private async Task<List<LrclibRecord>> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LrclibRecord>>(query, Json, ct) ?? [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象樂律] 歌詞查詢失敗：{ex.Message}");
            return [];
        }
    }

    /// <summary>挑最合適的一筆：有同步歌詞優先，長度最接近者優先。</summary>
    internal static LrclibRecord? PickBest(IReadOnlyList<LrclibRecord> records, TimeSpan duration)
    {
        if (records.Count == 0) return null;
        double target = duration.TotalSeconds;

        double Distance(LrclibRecord r) =>
            target > 0 && r.Duration is double d ? Math.Abs(d - target) : 0;

        // 已知長度時，差超過 8 秒的視為不同版本
        var pool = target > 0
            ? records.Where(r => r.Duration is null || Distance(r) <= 8).ToList()
            : records.ToList();
        if (pool.Count == 0) return null;

        return pool
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .ThenBy(Distance)
            .First();
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>LRCLIB API 回傳的一筆資料。</summary>
internal sealed record LrclibRecord(
    long Id,
    string? TrackName,
    string? ArtistName,
    string? AlbumName,
    double? Duration,
    bool Instrumental,
    string? PlainLyrics,
    string? SyncedLyrics);
