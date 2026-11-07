using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Office.Views;

namespace TianMu.Modules.Office;

/// <summary>
/// 法器「萬象簿」：記住最近複製過的十筆（文字、圖片、檔案），點一下就再複製一次。
///   · 只存在記憶體裡：關掉天幕島就清空，不寫進硬碟，也不上傳
///   · 密碼管理程式標記為「不要記錄」的內容一律略過
///   · 釘選的不會被新的擠掉
/// </summary>
public sealed class ClipboardModule : IIslandModule, IDescribedModule
{
    private const int ThumbnailSize = 64;

    private readonly ClipHistory _history = new();
    private IIslandHost? _host;
    private MessageWindow? _window;
    private ClipPageViewModel? _vm;
    private DispatcherTimer? _debounce;
    private DateTime _ignoreUntil;

    public string Id => "tianmu.clipboard";
    public string DisplayName => "萬象簿";
    public string Description => "記住最近複製過的十筆文字、圖片或檔案，點一下就再複製一次；只存在記憶體，關掉就清空。";
    public IReadOnlyList<ModuleOption> Options => [];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        await host.InvokeAsync(() =>
        {
            _vm = new ClipPageViewModel { ClearCommand = new RelayCommand(() => { _history.ClearUnpinned(); Refresh(); }) };
            var view = new ClipPageView { DataContext = _vm };
            host.SetPage(new IslandActivity(Id, DisplayName) { ExpandedView = view });

            // 剪貼簿常在一瞬間被寫好幾次（不同格式），稍等一下再讀
            _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
            {
                _debounce!.Stop();
                Capture();
            }, Dispatcher.CurrentDispatcher);
            _debounce.Stop();

            _window = new MessageWindow();
            _window.ClipboardChanged += () => { _debounce.Stop(); _debounce.Start(); };
            if (!_window.ListenClipboard()) Debug.WriteLine("[萬象簿] 無法監聽剪貼簿");
            Refresh();
        });

        host.AddRadialAction(new RadialAction(Id, "萬象簿", "剪貼簿", () => host.ShowPage(Id), Order: 35));
    }

    // ═══════════════════════ 讀取剪貼簿 ═══════════════════════

    private void Capture()
    {
        if (DateTime.Now < _ignoreUntil) return; // 是我們自己剛寫回去的

        IDataObject? data = null;
        for (int attempt = 0; attempt < 3 && data is null; attempt++)
        {
            try { data = Clipboard.GetDataObject(); }
            catch { Thread.Sleep(40); } // 別的程式還開著剪貼簿
        }
        if (data is null || ShouldSkip(data)) return;

        try
        {
            ClipEntry? entry = null;
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                entry = new ClipEntry { Kind = ClipKind.Files, Files = files };
            }
            else if (data.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } raw)
            {
                // 剪貼簿給的圖是暫時的，複製一份自己的（凍結後才能安全保存）
                var image = new WriteableBitmap(raw);
                image.Freeze();
                var (thumb, hash) = Thumbnail(image);
                entry = new ClipEntry { Kind = ClipKind.Image, Image = image, ImageHash = hash, Thumb = thumb };
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text)
            {
                if (text.Length > ClipHistory.MaxTextLength) text = text[..ClipHistory.MaxTextLength];
                entry = new ClipEntry { Kind = ClipKind.Text, Text = text };
            }
            if (entry is not null && _history.Add(entry)) Refresh();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象簿] 讀取剪貼簿失敗：{ex.Message}");
        }
    }

    /// <summary>密碼管理程式、遠端桌面等會加上「不要記錄」的標記，照它的意思不記。</summary>
    internal static bool ShouldSkip(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return true;
        if (data.GetDataPresent("Clipboard Viewer Ignore")) return true;
        foreach (string format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (!data.GetDataPresent(format)) continue;
            try
            {
                if (data.GetData(format) is MemoryStream ms && ms.Length >= 4)
                {
                    var bytes = ms.ToArray();
                    if (BitConverter.ToInt32(bytes, 0) == 0) return true;
                }
            }
            catch { return true; } // 讀不懂就保守一點，不記
        }
        return false;
    }

    /// <summary>做一張小縮圖，順便算出用來判斷「同一張圖」的指紋。</summary>
    private static (BitmapSource Thumb, string Hash) Thumbnail(BitmapSource image)
    {
        double scale = Math.Min(1, ThumbnailSize / (double)Math.Max(image.PixelWidth, image.PixelHeight));
        var small = new FormatConvertedBitmap(new TransformedBitmap(image, new ScaleTransform(scale, scale)), PixelFormats.Bgra32, null, 0);
        int stride = small.PixelWidth * 4;
        var pixels = new byte[stride * small.PixelHeight];
        small.CopyPixels(pixels, stride, 0);
        var thumb = BitmapSource.Create(small.PixelWidth, small.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        thumb.Freeze();
        return (thumb, Convert.ToHexString(SHA1.HashData(pixels))[..16]);
    }

    // ═══════════════════════ 再複製一次 ═══════════════════════

    private void CopyBack(ClipEntry entry)
    {
        try
        {
            _ignoreUntil = DateTime.Now.AddMilliseconds(600);
            switch (entry.Kind)
            {
                case ClipKind.Text: Clipboard.SetText(entry.Text); break;
                case ClipKind.Image when entry.Image is not null: Clipboard.SetImage(entry.Image); break;
                case ClipKind.Files:
                    var list = new StringCollection();
                    list.AddRange(entry.Files.ToArray());
                    Clipboard.SetFileDropList(list);
                    break;
            }
            // 再複製的那一筆移到最前面
            _history.Add(new ClipEntry { Kind = entry.Kind, Text = entry.Text, Files = entry.Files, Image = entry.Image, ImageHash = entry.ImageHash, Thumb = entry.Thumb });
            Refresh();
            _host?.Post(new IslandActivity(Id, "已複製", entry.Kind == ClipKind.Text ? ClipHistory.Preview(entry.Text, 30) : "可以貼上了",
                ActivityPriority.Normal, TimeSpan.FromSeconds(1.6)) { GlowKey = string.Empty });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[萬象簿] 寫入剪貼簿失敗：{ex.Message}");
            _host?.Post(new IslandActivity(Id, "剪貼簿被佔用", "等一下再點一次", ActivityPriority.Normal, TimeSpan.FromSeconds(2)));
        }
    }

    private void Refresh()
    {
        if (_vm is null) return;
        _vm.Rows.Clear();
        foreach (var e in _history.Items)
            _vm.Rows.Add(new ClipRow(e, CopyBack, x => { _history.TogglePin(x.Id); Refresh(); }, x => { _history.Remove(x.Id); Refresh(); }));
        _vm.Title = _history.Items.Count > 0 ? $"萬象簿 · {_history.Items.Count} 筆" : "萬象簿";
        _vm.Empty = _history.Items.Count == 0;
    }

    public ValueTask DisposeAsync()
    {
        _host?.InvokeAsync(() => _window?.Dispose());
        return ValueTask.CompletedTask;
    }
}
