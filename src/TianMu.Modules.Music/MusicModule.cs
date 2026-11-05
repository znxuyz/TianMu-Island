using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Music.Audio;
using TianMu.Modules.Music.Lyrics;
using TianMu.Modules.Music.Media;
using TianMu.Modules.Music.Views;

namespace TianMu.Modules.Music;

/// <summary>
/// 法器「萬象樂律」：
///   · 透過 Windows 系統媒體（SMTC）接管任何播放器 —— 包含瀏覽器裡的 YouTube / YouTube Music
///   · 自 LRCLIB 取得逐句同步歌詞
///   · 以 WASAPI 迴路擷取系統聲音，畫出隨節奏律動的頻譜
/// </summary>
public sealed class MusicModule : IIslandModule, IDescribedModule
{
    /// <summary>選項：是否向 LRCLIB 查詢歌詞。</summary>
    public const string LyricsOption = "lyrics";

    private const int FftSize = 2048;
    private const int BandCount = 40;
    private static readonly TimeSpan HideAfterPaused = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NewTrackPopup = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(200);

    private readonly MediaSessionWatcher _watcher = new();
    private readonly LoopbackCapture _capture = new(FftSize * 2);
    private readonly SpectrumAnalyzer _analyzer = new(FftSize, BandCount);
    private readonly LyricsService _lyrics = new();
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];

    private IIslandHost? _host;
    private MusicViewModel? _vm;
    private CompactMusicView? _compact;
    private ExpandedMusicView? _expanded;
    private SpectrumBars? _badge;
    private DispatcherTimer? _pausedTimer;

    // 以下欄位只在 UI 執行緒存取
    private MediaSnapshot? _snapshot;
    private string? _trackKey;
    private byte[]? _coverBytes;
    private IReadOnlyList<LyricLine> _lines = [];
    private int _lyricIndex = -2;
    private CancellationTokenSource? _lyricsCts;
    private bool _posted;
    private bool _rendering;
    private TimeSpan _lastFrame;

    public string Id => "tianmu.music";
    public string DisplayName => "萬象樂律";
    public string Description => "接管瀏覽器與播放器的音樂：封面、歌名、頻譜與播放控制。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(LyricsOption, "同步歌詞", "向 LRCLIB 歌詞庫查詢歌詞，只會送出歌名與歌手。", true),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;

        await host.InvokeAsync(() =>
        {
            _vm = new MusicViewModel
            {
                PlayPauseCommand = new RelayCommand(() => _ = _watcher.TogglePlayPauseAsync()),
                NextCommand = new RelayCommand(() => _ = _watcher.NextAsync()),
                PreviousCommand = new RelayCommand(() => _ = _watcher.PreviousAsync()),
            };

            _compact = new CompactMusicView { DataContext = _vm };
            _expanded = new ExpandedMusicView { DataContext = _vm };
            _compact.SpectrumBars.Bands = _bands;
            _expanded.SpectrumBars.Bands = _bands;

            // 分身小圓：四根跳動的頻譜
            _badge = new SpectrumBars { Width = 14, Height = 12, BarCount = 4, Centered = true, Bands = _bands };

            _pausedTimer = new DispatcherTimer { Interval = HideAfterPaused };
            _pausedTimer.Tick += (_, _) =>
            {
                _pausedTimer.Stop();
                Hide();
            };
        });

        _watcher.Changed += snapshot => _ = host.InvokeAsync(() => OnSnapshot(snapshot));
        await _watcher.StartAsync();
    }

    // ═══════════════════════ 媒體狀態 ═══════════════════════

    private void OnSnapshot(MediaSnapshot? s)
    {
        if (_vm is null) return;

        if (s is null)
        {
            _snapshot = null;
            _trackKey = null;
            _capture.SetActive(false);
            Hide();
            return;
        }

        bool newTrack = s.TrackKey != _trackKey;
        _snapshot = s;

        if (newTrack)
        {
            _trackKey = s.TrackKey;
            var identity = TrackTitleCleaner.Clean(s.Title, s.Artist, s.SourceApp);
            _vm.Title = identity.Title;
            _vm.Artist = identity.Artist;
            _vm.CompactText = identity.Title;
            _lines = [];
            _lyricIndex = -2;
            _vm.CurrentLyric = "正在推演歌詞…";
            _vm.NextLyric = string.Empty;
            _coverBytes = null;
            _vm.Cover = null;
            if (_host?.GetModuleSettings(Id).GetBool(LyricsOption, true) == false)
                _vm.CurrentLyric = "歌詞查詢已關閉";
            else
                StartLyricsLookup(identity, s.Duration);
        }

        if (!ReferenceEquals(s.Thumbnail, _coverBytes))
        {
            _coverBytes = s.Thumbnail;
            _vm.Cover = DecodeCover(s.Thumbnail);
            ApplyCoverColor(_vm.Cover as BitmapSource);
        }

        _vm.IsPlaying = s.IsPlaying;
        _vm.HasTimeline = s.Duration > TimeSpan.Zero;
        _vm.DurationText = Format(s.Duration);
        _capture.SetActive(s.IsPlaying);

        if (s.IsPlaying)
        {
            _pausedTimer?.Stop();
            Show(popup: newTrack);
        }
        else if (_posted && _pausedTimer is { IsEnabled: false })
        {
            _pausedTimer.Start(); // 暫停太久就讓出天幕
        }

        UpdateTimeline();
        EnsureRendering();
    }

    private void Show(bool popup)
    {
        if (_host is null) return;

        if (!_posted)
        {
            _host.Post(CreateActivity(ActivityPriority.Ambient, duration: null));
            _posted = true;
        }

        // 換歌時自動彈開幾秒，像靈動島一樣
        if (popup)
            _host.Post(CreateActivity(ActivityPriority.Normal, NewTrackPopup));
    }

    private void Hide()
    {
        if (!_posted || _host is null) return;
        _host.Dismiss(Id);
        _posted = false;
    }

    private IslandActivity CreateActivity(ActivityPriority priority, TimeSpan? duration) =>
        new(Id, _vm?.Title ?? DisplayName, _vm?.Artist, priority, duration)
        {
            CompactView = _compact,
            ExpandedView = _expanded,
            BadgeView = _badge,
        };

    /// <summary>封面取色：頻譜、進度條、分身小圓都跟著這首歌的封面換色。</summary>
    private void ApplyCoverColor(BitmapSource? cover)
    {
        if (_vm is null) return;
        var color = CoverPalette.Pick(cover);
        var top = Color.FromRgb((byte)Math.Min(255, color.R + 30), (byte)Math.Min(255, color.G + 30), (byte)Math.Min(255, color.B + 30));
        var bottom = Color.FromRgb((byte)(color.R * 0.8), (byte)(color.G * 0.8), (byte)(color.B * 0.8));
        var bars = new LinearGradientBrush(top, bottom, 90);
        bars.Freeze();

        _vm.Accent = MusicViewModel.CreateAccent(color);
        foreach (var spectrum in new[] { _compact?.SpectrumBars, _expanded?.SpectrumBars, _badge })
            if (spectrum is not null) spectrum.BarBrush = bars;
    }

    private static ImageSource? DecodeCover(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 256;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象樂律] 封面解碼失敗：{ex.Message}");
            return null;
        }
    }

    // ═══════════════════════ 歌詞 ═══════════════════════

    private void StartLyricsLookup(TrackIdentity identity, TimeSpan duration)
    {
        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        string? key = _trackKey;

        _ = Task.Run(async () =>
        {
            LyricsResult? result;
            try
            {
                result = await _lyrics.FindAsync(identity.SearchCandidates, duration, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[萬象樂律] 歌詞查詢例外：{ex.Message}");
                result = null;
            }

            if (_host is null) return;
            await _host.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested || key != _trackKey) return;
                ApplyLyrics(result);
            });
        });
    }

    private void ApplyLyrics(LyricsResult? result)
    {
        if (_vm is null) return;

        if (result is { HasSynced: true })
        {
            _lines = result.Synced;
            _lyricIndex = -2;
            UpdateTimeline();
            return;
        }

        _lines = [];
        _vm.NextLyric = string.Empty;
        _vm.CurrentLyric = result switch
        {
            { Instrumental: true } => "純音樂 · 靜心聆聽",
            { Plain: not null } => "此曲僅有純文字歌詞，無法同步",
            _ => "此曲尚無歌詞記載",
        };
    }

    // ═══════════════════════ 每幀更新（頻譜 + 進度 + 歌詞） ═══════════════════════

    private void EnsureRendering()
    {
        if (_rendering || !_posted) return;
        _rendering = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastFrame == TimeSpan.Zero ? 0 : (now - _lastFrame).TotalSeconds;
        if (_lastFrame != TimeSpan.Zero && dt <= 0) return;
        _lastFrame = now;

        if (_capture.TryRead(_samples))
            _analyzer.Update(_samples, _capture.SampleRate, dt);
        else
            _analyzer.Update(ReadOnlySpan<float>.Empty, 0, dt);

        var bands = _analyzer.Bands;
        bool anyMotion = false;
        for (int i = 0; i < _bands.Length; i++)
        {
            _bands[i] = bands[i];
            if (bands[i] > 0.001f) anyMotion = true;
        }

        _compact?.SpectrumBars.InvalidateVisual();
        _expanded?.SpectrumBars.InvalidateVisual();
        _badge?.InvalidateVisual();
        UpdateTimeline();

        bool playing = _snapshot?.IsPlaying == true;
        if (!_posted || (!playing && !anyMotion))
            StopRendering(); // 沒在播、柱子也落定了 → 停止逐幀更新，省電
    }

    private void UpdateTimeline()
    {
        var s = _snapshot;
        if (s is null || _vm is null) return;

        var pos = s.EstimatePosition(DateTimeOffset.Now);
        if (s.Duration > TimeSpan.Zero)
        {
            _vm.Progress = Math.Clamp(pos.TotalSeconds / s.Duration.TotalSeconds, 0, 1);
            _vm.PositionText = Format(pos);
        }

        if (_lines.Count == 0) return;

        if (s.Duration <= TimeSpan.Zero)
        {
            // 來源沒有回報播放進度，無法對時
            _vm.CurrentLyric = _lines[0].Text;
            _vm.NextLyric = "（此播放來源未回報進度，歌詞無法同步）";
            return;
        }

        int index = LrcParser.FindIndex(_lines, pos + LyricLead);
        if (index == _lyricIndex) return;
        _lyricIndex = index;

        _vm.CurrentLyric = index >= 0 && _lines[index].Text.Length > 0 ? _lines[index].Text : "♪";
        _vm.NextLyric = index + 1 < _lines.Count ? _lines[index + 1].Text : string.Empty;

        // 收合時就把歌詞放在島上；前奏或間奏（空白行）時顯示歌名
        _vm.CompactText = index >= 0 && _lines[index].Text.Length > 0 ? _lines[index].Text : _vm.Title;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public ValueTask DisposeAsync()
    {
        // 注意：關閉程式時 UI 執行緒正在等待，這裡不可再呼叫 host.InvokeAsync
        _lyricsCts?.Cancel();
        _watcher.Dispose();
        _capture.Dispose();
        _lyrics.Dispose();
        return ValueTask.CompletedTask;
    }
}
