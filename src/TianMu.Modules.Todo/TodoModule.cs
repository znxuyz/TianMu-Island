using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Todo.Views;

namespace TianMu.Modules.Todo;

/// <summary>
/// 法器「天機簿」：記下待辦與提醒時間，到點時天幕亮起朱砂光暈、落下一枚「令」印提醒你。
///   · 從展開面板的「記一筆」、天幕右鍵選單，或「天機簿」頁新增
///   · 提醒時可按「完成」或「十分鐘後」；不理它的話一分鐘後收起，留在清單上標成逾時
///   · 專注中也會提醒：這是你自己訂下的約
/// </summary>
public sealed class TodoModule : IIslandModule, IDescribedModule
{
    private const int PageRows = 4;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReminderVisibleFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(10);

    private IIslandHost? _host;
    private TodoStore _store = new();
    private string _storePath = string.Empty;

    // 以下只在 UI 執行緒存取
    private TodoReminderViewModel? _reminderVm;
    private TodoReminderView? _reminderView;
    private TodoPageViewModel? _pageVm;
    private TodoPageView? _pageView;
    private TodoEditorWindow? _editor;
    private DispatcherTimer? _checker;
    private Guid? _showing;
    private DateTime _shownAt;
    private bool _pageShown;

    public string Id => "tianmu.todo";
    public string DisplayName => "天機簿";
    public string Description => "記下待辦與提醒時間，到點時天幕提醒你。";
    public IReadOnlyList<ModuleOption> Options => [];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _storePath = Path.Combine(host.GetDataDirectory(Id), "todo.json");
        _store = TodoStore.Load(_storePath);
        _store.Prune(DateTime.Now);

        await host.InvokeAsync(() =>
        {
            _reminderVm = new TodoReminderViewModel
            {
                DoneCommand = new RelayCommand(() => { if (_showing is { } id) Complete(id); }),
                LaterCommand = new RelayCommand(Later),
            };
            _reminderView = new TodoReminderView { DataContext = _reminderVm };

            _pageVm = new TodoPageViewModel { AddCommand = new RelayCommand(OpenEditor) };
            _pageView = new TodoPageView { DataContext = _pageVm };

            _checker = new DispatcherTimer(DispatcherPriority.Background) { Interval = CheckInterval };
            _checker.Tick += (_, _) => Check();
            _checker.Start();

            RefreshPage();
            Check(); // 關機期間錯過的，開機後馬上補提醒
        });

        host.AddQuickAction(new QuickAction(Id, "記一筆", () => host.InvokeAsync(OpenEditor), Order: 30));
        host.AddRadialAction(new RadialAction(Id, "記一筆", "待辦", OpenEditor, Order: 30));
    }

    // ═══════════════════════ 新增 ═══════════════════════

    private void OpenEditor()
    {
        if (_editor is not null)
        {
            _editor.Activate();
            return;
        }

        _editor = new TodoEditorWindow((text, at) =>
        {
            _store.Add(text, at);
            Save();
            RefreshPage();
            _host?.Post(new IslandActivity(Id, "已記入天機簿", $"{TodoTime.Describe(at, DateTime.Now)} · {text.Trim()}",
                ActivityPriority.Normal, TimeSpan.FromSeconds(2.5)) { GlowKey = string.Empty });
        });
        _editor.Closed += (_, _) => _editor = null;
        _editor.Show();
    }

    // ═══════════════════════ 提醒 ═══════════════════════

    private void Check()
    {
        if (_host is null || _reminderVm is null) return;
        var now = DateTime.Now;

        // 時間到了、正在提醒的那一件還沒處理，就自動收起並保留在清單上（逾時）
        if (_showing is not null && now - _shownAt > ReminderVisibleFor)
        {
            _showing = null;
            _host.Dismiss(Id);
        }

        RefreshPage(); // 逾時的顏色會隨時間改變
        if (_showing is not null) return;

        var due = _store.Due(now);
        if (due.Count == 0) return;

        var item = due[0];
        item.Notified = true;
        Save();

        _showing = item.Id;
        _shownAt = now;
        _reminderVm.Text = item.Text;
        _reminderVm.When = due.Count > 1
            ? $"天機已至 · {item.DueAt:HH:mm} · 另有 {due.Count - 1} 件"
            : $"天機已至 · {TodoTime.Describe(item.DueAt, now)}";

        _host.Post(new IslandActivity(Id, item.Text, _reminderVm.When, ActivityPriority.Alert, ReminderVisibleFor)
        {
            ExpandedView = _reminderView,
            GlowKey = "ZhuShaBrush",
        });
    }

    private void Complete(Guid id)
    {
        _store.Complete(id, DateTime.Now);
        Save();
        CloseReminder(id);
    }

    private void Later()
    {
        if (_showing is not { } id) return;
        _store.Snooze(id, SnoozeFor, DateTime.Now);
        Save();
        CloseReminder(id);
    }

    private void CloseReminder(Guid id)
    {
        if (_showing == id)
        {
            _showing = null;
            _host?.Dismiss(Id);
        }
        RefreshPage();
        Check(); // 也許還有別件同時到點
    }

    // ═══════════════════════ 天機簿頁 ═══════════════════════

    private void RefreshPage()
    {
        if (_host is null || _pageVm is null || _pageView is null) return;
        var now = DateTime.Now;
        var pending = _store.Pending;

        _pageVm.Rows.Clear();
        foreach (var item in pending.Take(PageRows))
        {
            var id = item.Id;
            _pageVm.Rows.Add(new TodoRow(item.Text, TodoTime.Describe(item.DueAt, now), item.DueAt <= now, () => Complete(id)));
        }
        _pageVm.Title = $"天機簿 · {pending.Count} 件";
        _pageVm.More = pending.Count > PageRows
            ? string.Create(CultureInfo.InvariantCulture, $"還有 {pending.Count - PageRows} 件")
            : string.Empty;

        if (pending.Count > 0)
        {
            if (_pageShown) return; // 頁面已在，內容靠繫結更新
            _host.SetPage(new IslandActivity(Id, DisplayName) { ExpandedView = _pageView });
            _pageShown = true;
        }
        else if (_pageShown)
        {
            _host.RemovePage(Id);
            _pageShown = false;
        }
    }

    private void Save()
    {
        try { _store.Save(_storePath); }
        catch (Exception ex) { Debug.WriteLine($"[天機簿] 存檔失敗：{ex.Message}"); }
    }

    public ValueTask DisposeAsync()
    {
        _checker?.Stop();
        return ValueTask.CompletedTask;
    }
}
