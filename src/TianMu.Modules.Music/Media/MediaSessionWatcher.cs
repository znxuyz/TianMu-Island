using System.Diagnostics;
using System.IO;
using Windows.Media.Control;

namespace TianMu.Modules.Music.Media;

/// <summary>
/// 監看 Windows 系統媒體工作階段（SMTC）。
/// 瀏覽器（Chrome / Edge / Firefox 播 YouTube）、Spotify、Apple Music 等都會把播放資訊回報給 Windows，
/// 這裡統一讀取，不需要登入任何帳號或申請任何 API 金鑰。
/// </summary>
internal sealed class MediaSessionWatcher : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly List<GlobalSystemMediaTransportControlsSession> _subscribed = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private GlobalSystemMediaTransportControlsSession? _active;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    // 封面快取：同一首歌不重複讀取
    private string? _thumbKey;
    private byte[]? _thumbBytes;

    /// <summary>媒體狀態改變（null 代表目前沒有任何媒體）。可能在任意執行緒觸發。</summary>
    public event Action<MediaSnapshot?>? Changed;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => { Resubscribe(); ScheduleRefresh(); };
        _manager.CurrentSessionChanged += (_, _) => ScheduleRefresh();
        Resubscribe();
        await RefreshAsync();
    }

    // ── 控制 ──────────────────────────────────────────

    public Task TogglePlayPauseAsync() => Control(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => Control(s => s.TrySkipNextAsync().AsTask());
    public Task PreviousAsync() => Control(s => s.TrySkipPreviousAsync().AsTask());

    private async Task Control(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        var session = _active;
        if (session is null) return;
        try { await action(session); }
        catch (Exception ex) { Debug.WriteLine($"[萬象樂律] 控制失敗：{ex.Message}"); }
    }

    // ── 訂閱所有工作階段的事件 ────────────────────────

    private void Resubscribe()
    {
        if (_manager is null) return;
        lock (_gate)
        {
            foreach (var s in _subscribed)
            {
                s.MediaPropertiesChanged -= OnSessionChanged;
                s.PlaybackInfoChanged -= OnSessionChanged;
                s.TimelinePropertiesChanged -= OnSessionChanged;
            }
            _subscribed.Clear();

            foreach (var s in _manager.GetSessions())
            {
                s.MediaPropertiesChanged += OnSessionChanged;
                s.PlaybackInfoChanged += OnSessionChanged;
                s.TimelinePropertiesChanged += OnSessionChanged;
                _subscribed.Add(s);
            }
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => ScheduleRefresh();

    /// <summary>事件常常一次來好幾個，合併成一次刷新。</summary>
    private void ScheduleRefresh()
    {
        if (_disposed) return;
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _debounce, cts);
        old?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, cts.Token);
                await RefreshAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[萬象樂律] 刷新失敗：{ex}"); }
        });
    }

    // ── 讀取目前狀態 ──────────────────────────────────

    private async Task RefreshAsync()
    {
        if (_manager is null || _disposed) return;

        await _refreshLock.WaitAsync();
        try
        {
            var session = PickSession(_manager);
            _active = session;

            if (session is null)
            {
                Changed?.Invoke(null);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            string title = props?.Title ?? string.Empty;
            string artist = props?.Artist ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                Changed?.Invoke(null);
                return;
            }

            string app = session.SourceAppUserModelId ?? string.Empty;
            string key = $"{app}\u0001{title}\u0001{artist}";
            if (key != _thumbKey || _thumbBytes is null)
            {
                _thumbBytes = props?.Thumbnail is { } thumb ? await ReadThumbnailAsync(thumb) : null;
                _thumbKey = key;
            }

            var now = DateTimeOffset.Now;
            var updatedAt = timeline.LastUpdatedTime;
            // 部分程式回報的時間戳不可靠（太舊或在未來），以現在時間為準
            if (updatedAt.Year < 2000 || updatedAt > now) updatedAt = now;

            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;

            var snapshot = new MediaSnapshot(
                SourceApp: app,
                Title: title,
                Artist: artist,
                Album: props?.AlbumTitle ?? string.Empty,
                Thumbnail: _thumbBytes,
                IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position: position < TimeSpan.Zero ? TimeSpan.Zero : position,
                Duration: duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
                PositionUpdatedAt: updatedAt,
                PlaybackRate: playback.PlaybackRate is double rate && rate > 0 ? rate : 1.0);

            Changed?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象樂律] 讀取媒體失敗：{ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>優先選「正在播放」的工作階段；都沒在播就用系統認定的目前工作階段。</summary>
    private static GlobalSystemMediaTransportControlsSession? PickSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = manager.GetCurrentSession();
        if (current is not null && IsPlaying(current)) return current;

        foreach (var s in manager.GetSessions())
            if (IsPlaying(s)) return s;

        return current;

        static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                return s.GetPlaybackInfo().PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                return false;
            }
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象樂律] 讀取封面失敗：{ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce?.Cancel();
        lock (_gate)
        {
            foreach (var s in _subscribed)
            {
                s.MediaPropertiesChanged -= OnSessionChanged;
                s.PlaybackInfoChanged -= OnSessionChanged;
                s.TimelinePropertiesChanged -= OnSessionChanged;
            }
            _subscribed.Clear();
        }
    }
}
