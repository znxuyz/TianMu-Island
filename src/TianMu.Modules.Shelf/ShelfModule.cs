using System.Diagnostics;
using System.IO;
using TianMu.Core;
using TianMu.Modules.Shelf.Views;

namespace TianMu.Modules.Shelf;

/// <summary>
/// 法器「乾坤袋」：把檔案拖到天幕上暫存，需要時再從天幕拖出去，
/// 例如從下載資料夾收進來、再拖到聊天視窗或網頁上傳。
/// 袋裡有東西時，天幕多出一頁「乾坤袋」；只記住路徑，檔案本身不會被複製或搬動。
/// </summary>
public sealed class ShelfModule : IIslandModule, IDescribedModule
{
    private IIslandHost? _host;
    private ShelfStore _store = new();
    private string _storePath = string.Empty;

    // 以下只在 UI 執行緒存取
    private ShelfViewModel? _vm;
    private ShelfView? _view;
    private bool _pageShown;
    private IslandActivity? _ambient;

    public string Id => "tianmu.shelf";
    public string DisplayName => "乾坤袋";
    public string Description => "把檔案拖到天幕上暫存，需要時再拖出去用。只記住位置，不會複製或搬動檔案。";
    public IReadOnlyList<ModuleOption> Options => [];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _storePath = Path.Combine(host.GetDataDirectory(Id), "shelf.json");
        _store = ShelfStore.Load(_storePath);
        _store.Prune(p => File.Exists(p) || Directory.Exists(p));

        await host.InvokeAsync(() =>
        {
            _vm = new ShelfViewModel { ClearCommand = new RelayCommand(Clear) };
            _view = new ShelfView { DataContext = _vm };
            // 背景常駐：膠囊沒別的事時顯示件數；有音樂等常駐時退居分身小圓
            _ambient = new IslandActivity(Id, DisplayName, Priority: ActivityPriority.Ambient)
            {
                CompactView = new ShelfCompactView { DataContext = _vm },
                BadgeView = new ShelfBadge { DataContext = _vm },
                ExpandedView = _view,
                IsBackground = true,
            };
            Refresh();
        });

        host.AddFileDropHandler(Id, "放入乾坤袋", OnDrop);
        host.AddRadialAction(new RadialAction(Id, "乾坤袋", "暫存", () =>
        {
            if (!host.ShowPage(Id))
                host.Post(new IslandActivity(Id, "乾坤袋是空的", "把檔案拖到天幕上就能收進來",
                    ActivityPriority.Normal, TimeSpan.FromSeconds(3)));
        }, Order: 40));
    }

    private void OnDrop(IReadOnlyList<string> paths)
    {
        if (_host is null) return;
        int added = _store.Add(paths);
        Save();
        Refresh();

        _host.Post(new IslandActivity(Id,
            added > 0 ? $"收入乾坤袋 · {added} 件" : "已在乾坤袋中",
            $"袋中共 {_store.Paths.Count} 件 · 展開天幕即可拖出",
            ActivityPriority.Normal, TimeSpan.FromSeconds(2.5)));
    }

    private void Remove(ShelfItem item)
    {
        _store.Remove(item.Path);
        Save();
        Refresh();
    }

    private void Clear()
    {
        _store.Clear();
        Save();
        Refresh();
    }

    /// <summary>重建畫面；袋子空了就收起這一頁。</summary>
    private void Refresh()
    {
        if (_host is null || _vm is null || _view is null) return;

        _vm.Items.Clear();
        foreach (string path in _store.Paths)
            _vm.Items.Add(new ShelfItem(path, ShellIcons.For(path), Remove));
        _vm.Title = $"乾坤袋 · {_store.Paths.Count} 件";
        _vm.Count = _store.Paths.Count;

        if (_store.Paths.Count > 0)
        {
            if (_pageShown) return; // 件數靠繫結更新
            if (_ambient is not null) _host.Post(_ambient);
            _pageShown = true;
        }
        else if (_pageShown)
        {
            _host.Dismiss(Id);
            _pageShown = false;
        }
    }

    private void Save()
    {
        try { _store.Save(_storePath); }
        catch (Exception ex) { Debug.WriteLine($"[乾坤袋] 存檔失敗：{ex.Message}"); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
