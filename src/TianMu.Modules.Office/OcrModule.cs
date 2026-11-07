using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TianMu.Core;
using TianMu.Modules.Office.Views;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace TianMu.Modules.Office;

/// <summary>
/// 法器「照妖鏡」：截圖取字。按 Ctrl + Alt + O（或從法陣盤），在螢幕上框一塊，裡面的文字就辨識出來並複製好。
/// 用 Windows 內建的文字辨識，不上傳；支援繁中、英文、日文等（看 Windows 裝了哪些語言）。
/// </summary>
public sealed class OcrModule : IIslandModule, IDescribedModule
{
    private const int HotkeyId = 0x4F43;   // 'OC'
    private const uint ModControlAlt = 0x0002 | 0x0001;
    private const uint KeyO = 0x4F;

    private IIslandHost? _host;
    private MessageWindow? _window;
    private bool _busy;

    public string Id => "tianmu.ocr";
    public string DisplayName => "照妖鏡";
    public string Description => "截圖取字：按 Ctrl + Alt + O 框選螢幕上的一塊，文字就辨識出來並複製好；用 Windows 內建的辨識，不上傳。";
    public IReadOnlyList<ModuleOption> Options => [];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        await host.InvokeAsync(() =>
        {
            _window = new MessageWindow();
            if (!_window.RegisterHotkey(HotkeyId, ModControlAlt, KeyO, () => _ = SnipAsync(delay: false)))
                Debug.WriteLine("[照妖鏡] Ctrl + Alt + O 已被其他程式佔用");
        });
        // 從法陣盤叫出時，先等法陣盤收起來再擷取畫面
        host.AddRadialAction(new RadialAction(Id, "照妖鏡", "截圖取字", () => _ = SnipAsync(delay: true), Order: 45));
    }

    private async Task SnipAsync(bool delay)
    {
        if (_host is null || _busy) return;
        _busy = true;
        try
        {
            if (delay) await Task.Delay(220);
            var shot = ScreenCapture.CaptureMonitorUnderCursor();
            if (shot is null) return;

            var snip = new SnipWindow(shot.Image, shot.Bounds);
            snip.ShowDialog();
            if (snip.Result is not { } crop) return;

            string text = await RecognizeAsync(crop);
            if (string.IsNullOrWhiteSpace(text))
            {
                _host.Post(new IslandActivity(Id, "照妖鏡沒看到文字", "框大一點，或換清楚一點的地方再試",
                    ActivityPriority.Normal, TimeSpan.FromSeconds(3)));
                return;
            }

            Clipboard.SetText(text);
            int count = OcrText.CountWords(text);
            _host.Post(new IslandActivity(Id, $"已取字 · {count} 字，已複製", text.Replace(Environment.NewLine, " ").Length > 40
                    ? text.Replace(Environment.NewLine, " ")[..40] + "…"
                    : text.Replace(Environment.NewLine, " "),
                ActivityPriority.Normal, TimeSpan.FromSeconds(4)) { GlowKey = "TianQingBrush" });
        }
        catch (NoOcrLanguageException)
        {
            _host.Post(new IslandActivity(Id, "照妖鏡需要語言套件",
                "Windows 設定 → 時間與語言 → 語言 → 中文（台灣）→ 選項 → 下載「光學字元辨識」",
                ActivityPriority.Alert, TimeSpan.FromSeconds(8)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[照妖鏡] 失敗：{ex}");
            _host.Post(new IslandActivity(Id, "照妖鏡失靈了", ex.Message, ActivityPriority.Normal, TimeSpan.FromSeconds(4)));
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class NoOcrLanguageException : Exception;

    /// <summary>依序試繁中、簡中、日文、英文，最後用 Windows 使用者設定的語言。</summary>
    private static OcrEngine CreateEngine()
    {
        foreach (string tag in new[] { "zh-Hant-TW", "zh-TW", "zh-Hant", "zh-HK" })
        {
            try
            {
                var lang = new Language(tag);
                if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is { } engine) return engine;
            }
            catch { /* 不認得的語言標籤 */ }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new NoOcrLanguageException();
    }

    private static async Task<string> RecognizeAsync(BitmapSource crop)
    {
        var engine = CreateEngine();

        // 字太小時辨識率差：放大到至少 60 像素高
        BitmapSource input = crop;
        if (crop.PixelHeight < 60)
        {
            double scale = Math.Min(4, 60.0 / Math.Max(1, crop.PixelHeight));
            input = new TransformedBitmap(crop, new ScaleTransform(scale, scale));
        }
        if (Math.Max(input.PixelWidth, input.PixelHeight) > OcrEngine.MaxImageDimension)
        {
            double scale = (double)OcrEngine.MaxImageDimension / Math.Max(input.PixelWidth, input.PixelHeight);
            input = new TransformedBitmap(input, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(input));
        using var png = new MemoryStream();
        encoder.Save(png);

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.ToArray().AsBuffer());
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return OcrText.Join(result.Lines.Select(l => l.Text));
    }

    public ValueTask DisposeAsync()
    {
        _host?.InvokeAsync(() => _window?.Dispose());
        return ValueTask.CompletedTask;
    }
}
