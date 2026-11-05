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
///   · 主要來源：在天幕島裡登入 claude.ai 一次（內嵌瀏覽器，跟網站用一樣的方式查詢）
///   · 備用來源：這台電腦上 Claude Code 的登入（可在設定關閉）
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
    private ClaudeWebSession? _web;
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
        _web = new ClaudeWebSession(Path.Combine(host.GetDataDirectory(Id), "webview"));

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
        if (_vm is null || _host is null || _web is null) return;

        _fetchCts?.Cancel();
        var cts = _fetchCts = new CancellationTokenSource();
        _vm.Status = "查詢中…";

        bool useClaudeCode = _host.GetModuleSettings(Id).GetBool(UseClaudeCodeOption, true);
        var login = useClaudeCode ? ClaudeCodeCredentials.TryRead() : null;

        try
        {
            ClaudeUsage usage;
            if (await _web.IsLoggedInAsync())
            {
                // 主要來源：天幕島裡的 claude.ai 登入
                var result = await _web.FetchUsageAsync(cts.Token);
                string body = result.Body.TrimStart();
                ClaudeUsageClient.ThrowIfFailed(result.Status, body.StartsWith('{') || body.StartsWith('['), "claude.ai");
                usage = ClaudeUsageParser.Parse(result.Body, DateTimeOffset.Now);
                _plan = result.Plan;
            }
            else if (login is not null)
            {
                // 備用來源：Claude Code 的登入
                try
                {
                    usage = await Task.Run(() => _client.FetchWithClaudeCodeAsync(login, cts.Token), cts.Token);
                    _plan = login.Plan;
                }
                catch (UsageException ex)
                {
                    throw new UsageException($"{ex.Message}或按右上角「連結」直接登入 Claude。");
                }
            }
            else
            {
                ShowDisconnected("按右上角「連結」登入你的 Claude 帳號，登入一次之後就會自動顯示剩餘額度。");
                return;
            }

            if (cts.IsCancellationRequested) return;
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
        if (_web is null) return;
        var web = _web;
        var window = new ConnectWindow(
            claudeCodeFound: ClaudeCodeCredentials.Exists(),
            isLoggedIn: web.IsLoggedInAsync,
            login: OpenLoginWindow,
            logout: async () =>
            {
                await web.LogOutAsync();
                _plan = null;
                _ = RefreshAsync();
            });
        window.Show();
        window.Activate();
    }

    private void OpenLoginWindow()
    {
        if (_web is null) return;
        var window = new LoginWindow(_web, () => _ = RefreshAsync());
        window.Show();
        window.Activate();
    }

    public ValueTask DisposeAsync()
    {
        _fetchCts?.Cancel();
        _timer?.Stop();
        _client.Dispose();
        // 內嵌瀏覽器隨程式結束一併關閉（不可在此跨執行緒操作它）
        return ValueTask.CompletedTask;
    }
}
