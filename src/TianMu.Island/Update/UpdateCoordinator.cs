using System.Diagnostics;
using System.Windows;
using TianMu.Core;
using TianMu.Island.Settings;

namespace TianMu.Island.Update;

/// <summary>更新流程目前所在的階段。</summary>
public enum UpdateStage
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>
/// 更新流程的狀態機：系統匣選單與設定面板都看同一份狀態，不會各說各話。
/// 所有公開成員都應在 UI 執行緒呼叫；<see cref="Changed"/> 也只在 UI 執行緒觸發。
/// </summary>
public sealed class UpdateCoordinator(SettingsStore settings, IIslandHost island) : IDisposable
{
    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(20);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly UpdateService _service = new();
    private CancellationTokenSource? _cts;

    public UpdateStage Stage { get; private set; } = UpdateStage.Idle;
    public ReleaseInfo? Available { get; private set; }
    public double Progress { get; private set; }
    public string? Error { get; private set; }

    public event Action? Changed;

    public static string CurrentVersionText => $"v{UpdateService.CurrentVersion.ToString(3)}";

    /// <summary>給人看的狀態文字。</summary>
    public string StatusText => Stage switch
    {
        UpdateStage.Checking => "正在查詢新版本…",
        UpdateStage.UpToDate => "已是最新版本",
        UpdateStage.Available => $"新版本 {Available?.Tag} 已可更新",
        UpdateStage.Downloading => $"下載中 {Progress:P0}",
        UpdateStage.Installing => "正在安裝，天幕即將重新啟動…",
        UpdateStage.Failed => $"更新失敗：{Error}",
        _ => UpdateService.CanSelfUpdate ? "尚未檢查" : "開發版不自動更新",
    };

    /// <summary>啟動後延遲一段時間，若到了檢查週期就安靜地查一次。</summary>
    public async Task AutoCheckAsync()
    {
        try
        {
            await Task.Delay(StartupDelay);
            var s = settings.Snapshot();
            if (!s.AutoCheckUpdates || !UpdateService.CanSelfUpdate) return;
            if (s.LastUpdateCheck is { } last && DateTimeOffset.Now - last < AutoCheckInterval) return;

            await CheckAsync(manual: false);

            if (Stage == UpdateStage.Available && Available is { } release &&
                settings.Snapshot().SkippedVersion != release.Tag)
            {
                island.Post(new IslandActivity("tianmu.update", $"新版本 {release.Tag}",
                    "右鍵系統匣圖示，或開啟設定即可更新", ActivityPriority.Normal, TimeSpan.FromSeconds(6)));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[天幕] 自動檢查更新失敗：{ex.Message}");
        }
    }

    /// <summary>查詢新版本。手動查詢時失敗會顯示原因；自動查詢失敗則保持安靜。</summary>
    public async Task CheckAsync(bool manual = true)
    {
        if (Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Installing) return;

        SetStage(UpdateStage.Checking);
        try
        {
            var latest = await _service.GetLatestAsync();
            settings.Update(s => s.LastUpdateCheck = DateTimeOffset.Now);

            if (latest is not null && UpdateService.IsNewer(latest))
            {
                Available = latest;
                SetStage(UpdateStage.Available);
            }
            else
            {
                Available = null;
                SetStage(UpdateStage.UpToDate);
            }
        }
        catch (Exception ex)
        {
            if (manual)
            {
                Error = "無法連線到 GitHub，請稍後再試";
                Debug.WriteLine($"[天幕] 檢查更新失敗：{ex}");
                SetStage(UpdateStage.Failed);
            }
            else
            {
                SetStage(UpdateStage.Idle);
            }
        }
    }

    /// <summary>下載、驗證並安裝新版，成功後天幕會重新啟動。</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } release || Stage is UpdateStage.Downloading or UpdateStage.Installing) return;
        if (!UpdateService.CanSelfUpdate)
        {
            Error = "開發版無法自我更新，請到 GitHub 下載";
            SetStage(UpdateStage.Failed);
            return;
        }

        _cts = new CancellationTokenSource();
        Progress = 0;
        SetStage(UpdateStage.Downloading);

        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Changed?.Invoke();
            });

            string downloaded = await _service.DownloadAsync(release, progress, _cts.Token);

            SetStage(UpdateStage.Installing);
            UpdateService.ApplyAndRestart(downloaded);
            App.Current.Quit();
        }
        catch (OperationCanceledException)
        {
            SetStage(UpdateStage.Available);
        }
        catch (UnauthorizedAccessException)
        {
            Error = "沒有權限覆寫程式所在的資料夾，請把天幕島放到「文件」或「桌面」等個人資料夾";
            SetStage(UpdateStage.Failed);
        }
        catch (Exception ex)
        {
            Error = ex is System.IO.InvalidDataException ? ex.Message : "下載失敗，請檢查網路後再試";
            Debug.WriteLine($"[天幕] 更新失敗：{ex}");
            SetStage(UpdateStage.Failed);
        }
    }

    /// <summary>略過這個版本，不再提醒。</summary>
    public void SkipAvailable()
    {
        if (Available is not { } release) return;
        settings.Update(s => s.SkippedVersion = release.Tag);
        Available = null;
        SetStage(UpdateStage.Idle);
    }

    /// <summary>開啟新版的發佈頁面（看更新內容）。</summary>
    public void OpenReleasePage()
    {
        string url = Available?.PageUrl is { Length: > 0 } page
            ? page
            : $"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}/releases";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[天幕] 無法開啟網頁：{ex.Message}"); }
    }

    private void SetStage(UpdateStage stage)
    {
        Stage = stage;
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.InvokeAsync(() => Changed?.Invoke());
        else
            Changed?.Invoke();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _service.Dispose();
    }
}
