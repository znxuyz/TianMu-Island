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
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);
    private static readonly TimeSpan InstallNotice = TimeSpan.FromSeconds(8);

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

    /// <summary>
    /// 背景巡查：啟動 30 秒後開始，之後每小時看一次。距離上次查詢滿一天就向 GitHub 查新版；
    /// 開了「自動更新」時直接下載安裝，否則只在天幕上提醒。天幕島連開好幾天也照樣每天查。
    /// </summary>
    public async Task AutoCheckAsync()
    {
        try { await Task.Delay(StartupDelay); }
        catch (Exception) { return; }
        bool notified = false;
        while (true)
        {
            try
            {
                var s = settings.Snapshot();
                if ((s.AutoCheckUpdates || s.AutoInstallUpdates) && UpdateService.CanSelfUpdate)
                {
                    if (IsDue(s.LastUpdateCheck, DateTimeOffset.Now) && Stage is UpdateStage.Idle or UpdateStage.UpToDate or UpdateStage.Failed)
                    {
                        await CheckAsync(manual: false);
                        notified = false;
                    }

                    if (Stage == UpdateStage.Available && Available is { } release && settings.Snapshot().SkippedVersion != release.Tag)
                    {
                        if (settings.Snapshot().AutoInstallUpdates) await TryAutoInstallAsync(release);
                        else if (!notified)
                        {
                            island.Post(new IslandActivity("tianmu.update", $"新版本 {release.Tag}",
                                "右鍵系統匣圖示，或開啟設定即可更新", ActivityPriority.Normal, TimeSpan.FromSeconds(6)));
                            notified = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[天幕] 自動檢查更新失敗：{ex.Message}");
            }
            await Task.Delay(Tick);
        }
    }

    /// <summary>距離上次查詢是否已滿一個週期（純邏輯，方便測試）。</summary>
    public static bool IsDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is not { } l || now - l >= AutoCheckInterval || l > now;   // 時鐘被調回去也重新查

    /// <summary>
    /// 自動安裝：重新啟動會關掉所有視窗，所以只在你沒開著文房、問道、設定這類視窗，
    /// 也不在專注中時才進行；否則等下一個小時再看。
    /// </summary>
    private async Task TryAutoInstallAsync(ReleaseInfo release)
    {
        if (!IsSafeToRestart()) return;
        island.Post(new IslandActivity("tianmu.update", $"自動更新到 {release.Tag}",
            "下載並驗證後，天幕會自動重新啟動", ActivityPriority.Normal, InstallNotice));
        await Task.Delay(InstallNotice);
        if (!IsSafeToRestart() || Stage != UpdateStage.Available) return;
        await InstallAsync();
    }

    /// <summary>有沒有你正在用的視窗（出現在工作列上的都算），或正在專注。</summary>
    private static bool IsSafeToRestart()
    {
        if (Application.Current is not { } app) return false;
        foreach (Window w in app.Windows)
        {
            if (!w.IsVisible) continue;
            if (w.ShowInTaskbar) return false;                           // 文房、問道、設定、洗髓⋯
            if (w is TianMu.Island.Shell.FocusBarrierWindow) return false;   // 專注結界亮著
        }
        return true;
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
