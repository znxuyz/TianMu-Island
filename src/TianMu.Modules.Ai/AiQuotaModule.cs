using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Ai.Claude;
using TianMu.Modules.Ai.Views;

namespace TianMu.Modules.Ai;

/// <summary>
/// 法器「AI 額度」：在天幕上常駐一頁，用雙環顯示 Claude 訂閱方案的 5 小時與 7 天剩餘額度。
///   · 自動使用 Claude Code 的登入（可在設定關閉），或使用者貼上的 claude.ai sessionKey
///   · 每 5 分鐘更新一次；5 小時額度低於 15% 時提醒一次
/// </summary>
public sealed class AiQuotaModule : IIslandModule, IDescribedModule
{
    public const string UseClaudeCodeOption = "useClaudeCode";
    public const string LowQuotaAlertOption = "lowQuotaAlert";

    private const double PageWidth = 440;
    private const double PageHeight = 176;
    private const double LowQuotaThreshold = 15;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ClaudeUsageClient _client = new();
    private IIslandHost? _host;
    private SecretStore? _secrets;
    private CancellationTokenSource? _fetchCts;

    // 以下只在 UI 執行緒存取
    private QuotaViewModel? _vm;
    private QuotaView? _view;
    private DispatcherTimer? _timer;
    private bool _alertedLow;
    private string? _plan;

    public string Id => "tianmu.ai-quota";
    public string DisplayName => "AI 額度";
    public string Description => "在天幕上用雙環顯示 Claude 的 5 小時與 7 天剩餘額度。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(UseClaudeCodeOption, "使用 Claude Code 的登入", "自動讀取這台電腦上 Claude Code 的登入來查額度（只讀取，不會更動它）。", true),
        new(LowQuotaAlertOption, "額度不足提醒", "5 小時額度剩不到 15% 時，天幕彈出提醒一次。", true),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _secrets = new SecretStore(Path.Combine(host.GetDataDirectory(Id), "claude-session.bin"));

        await host.InvokeAsync(() =>
        {
            _vm = new QuotaViewModel
            {
                RefreshCommand = new RelayCommand(() => _ = RefreshAsync()),
                ConnectCommand = new RelayCommand(OpenConnectWindow),
            };
            _view = new QuotaView { DataContext = _vm };

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
            _timer.Tick += (_, _) => _ = RefreshAsync();
            _timer.Start();
        });

        host.SetPage(new IslandActivity(Id, DisplayName)
        {
            ExpandedView = _view,
            ExpandedWidth = PageWidth,
            ExpandedHeight = PageHeight,
        });

        await host.InvokeAsync(() => _ = RefreshAsync());
    }

    // ═══════════════════════ 查詢 ═══════════════════════

    private async Task RefreshAsync()
    {
        if (_vm is null || _host is null || _secrets is null) return;

        _fetchCts?.Cancel();
        var cts = _fetchCts = new CancellationTokenSource();
        _vm.Status = "查詢中…";

        bool useClaudeCode = _host.GetModuleSettings(Id).GetBool(UseClaudeCodeOption, true);
        string? sessionKey = _secrets.Read();
        var login = useClaudeCode ? ClaudeCodeCredentials.TryRead() : null;

        if (sessionKey is null && login is null)
        {
            ShowDisconnected(useClaudeCode
                ? "按右上角「連結」：登入過 Claude Code 會自動偵測，或貼上 claude.ai 的 sessionKey。"
                : "已關閉「使用 Claude Code 的登入」。按右上角「連結」貼上 claude.ai 的 sessionKey。");
            return;
        }

        try
        {
            // 使用者主動貼上的 sessionKey 優先；否則用 Claude Code 的登入
            var usage = await Task.Run(() => sessionKey is not null
                ? _client.FetchWithSessionKeyAsync(sessionKey, cts.Token)
                : _client.FetchWithClaudeCodeAsync(login!, cts.Token), cts.Token);

            if (cts.IsCancellationRequested) return;
            _plan = sessionKey is null ? login?.Plan : _plan;
            ShowUsage(usage);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (UsageException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            ShowError("額度資料的格式已變更，需要更新天幕島。");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AI 額度] 查詢失敗：{ex}");
            ShowError("查詢失敗，稍後會再試。");
        }
    }

    private void ShowUsage(ClaudeUsage usage)
    {
        if (_vm is null) return;
        var now = DateTime.Now;

        _vm.Connected = true;
        _vm.PlanText = FormatPlan(_plan);

        if (usage.FiveHour is { } five)
        {
            _vm.FiveHourRemaining = five.RemainingPercent / 100;
            _vm.FiveHourPercent = ClaudeUsageParser.PercentText(five.RemainingPercent);
            _vm.FiveHourReset = ClaudeUsageParser.ResetText(five.ResetsAt, now);
            CheckLowQuota(five);
        }
        else
        {
            _vm.FiveHourRemaining = 0;
            _vm.FiveHourPercent = "—";
            _vm.FiveHourReset = string.Empty;
        }

        if (usage.SevenDay is { } seven)
        {
            _vm.SevenDayRemaining = seven.RemainingPercent / 100;
            _vm.SevenDayPercent = ClaudeUsageParser.PercentText(seven.RemainingPercent);
            _vm.SevenDayReset = ClaudeUsageParser.ResetText(seven.ResetsAt, now);
        }
        else
        {
            _vm.SevenDayRemaining = 0;
            _vm.SevenDayPercent = "—";
            _vm.SevenDayReset = string.Empty;
        }

        _vm.Status = $"更新於 {now:HH:mm}";
    }

    private void ShowDisconnected(string hint)
    {
        if (_vm is null) return;
        _vm.Connected = false;
        _vm.EmptyHint = hint;
        _vm.Status = string.Empty;
    }

    /// <summary>查詢失敗：保留上次的數字（若有），只在狀態列說明原因。</summary>
    private void ShowError(string message)
    {
        if (_vm is null) return;
        if (!_vm.Connected) _vm.EmptyHint = message;
        _vm.Status = message;
    }

    /// <summary>剩不到 15% 時提醒一次；額度回升後重新武裝。</summary>
    private void CheckLowQuota(UsageWindow five)
    {
        if (_host is null) return;

        if (five.RemainingPercent >= LowQuotaThreshold)
        {
            _alertedLow = false;
            return;
        }
        if (_alertedLow || !_host.GetModuleSettings(Id).GetBool(LowQuotaAlertOption, true)) return;

        _alertedLow = true;
        _host.Post(new IslandActivity(Id, $"Claude 額度剩 {ClaudeUsageParser.PercentText(five.RemainingPercent)}",
            ClaudeUsageParser.ResetText(five.ResetsAt, DateTime.Now), ActivityPriority.Alert, TimeSpan.FromSeconds(5)));
    }

    private static string FormatPlan(string? plan) => plan?.ToLowerInvariant() switch
    {
        null or "" => string.Empty,
        "max" => "Max",
        "pro" => "Pro",
        "team" => "Team",
        "enterprise" => "Enterprise",
        var other => other,
    };

    // ═══════════════════════ 連結 ═══════════════════════

    private void OpenConnectWindow()
    {
        if (_secrets is null) return;
        var window = new ConnectWindow(
            claudeCodeFound: ClaudeCodeCredentials.Exists(),
            hasSavedKey: _secrets.HasValue,
            save: key =>
            {
                _secrets.Write(key);
                _client.ForgetOrganization();
                _plan = null;
                _ = RefreshAsync();
            },
            clear: () =>
            {
                _secrets.Clear();
                _client.ForgetOrganization();
                _ = RefreshAsync();
            });
        window.Show();
        window.Activate();
    }

    public ValueTask DisposeAsync()
    {
        _fetchCts?.Cancel();
        _timer?.Stop();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
