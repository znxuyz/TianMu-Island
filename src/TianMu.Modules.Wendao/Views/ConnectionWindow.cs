using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TianMu.Modules.Wendao.Ai;
using TianMu.Modules.Wendao.Store;

namespace TianMu.Modules.Wendao.Views;

/// <summary>「連線」：每家一張卡片，貼上免費金鑰、測試、看狀態。</summary>
internal sealed class ConnectionWindow : Window
{
    private readonly Conductor _conductor;
    private readonly Dictionary<string, (PasswordBox Box, TextBlock Status)> _rows = [];

    public ConnectionWindow(Conductor conductor, Window owner)
    {
        _conductor = conductor;
        Owner = owner;
        Title = "問道 — 連線";
        Width = 640;
        Height = 700;
        MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Look.Lacquer;
        FontFamily = Look.Sans;

        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        stack.Children.Add(new TextBlock { Text = "連線", FontFamily = Look.Serif, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Look.Gold });
        stack.Children.Add(new TextBlock
        {
            Text = "問道直接連到各家的免費 API，不經過任何中間平台。至少設定一家就能用；設越多家，自動挑選越有彈性，某家額度用完時也會自動換下一家。\n金鑰存在 Windows 認證管理員（加密），只有你的帳號讀得到。",
            TextWrapping = TextWrapping.Wrap, Foreground = Look.Secondary, FontSize = 13, Margin = new Thickness(0, 6, 0, 14),
        });
        foreach (var p in Provider.All) stack.Children.Add(Card(p));
        stack.Children.Add(new TextBlock { Text = "生成圖片", FontFamily = Look.Serif, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Look.Gold, Margin = new Thickness(0, 10, 0, 4) });
        stack.Children.Add(new TextBlock
        {
            Text = "按「畫圖」時先用 Pollinations（免金鑰、免註冊）；它忙不過來時改用 Cloudflare，再來是 Gemini（用上面的 Gemini 金鑰）。",
            TextWrapping = TextWrapping.Wrap, Foreground = Look.Secondary, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10),
        });
        stack.Children.Add(Card(ImageGenerator.Cloudflare));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Look.Button("儲存並測試", SaveAndTest, primary: true));
        buttons.Children.Add(new Border { Width = 8 });
        buttons.Children.Add(Look.Button("關閉", Close));
        stack.Children.Add(buttons);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += async (_, _) => await TestAsync();
    }

    private Border Card(Provider p)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel();
        var status = new TextBlock { FontSize = 12, Foreground = Look.Tertiary, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(status);
        head.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Look.Moon });
        grid.Children.Add(head);

        var note = new TextBlock { Text = p.Note, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Look.Secondary, Margin = new Thickness(0, 4, 0, 8) };
        Grid.SetRow(note, 1);
        grid.Children.Add(note);

        var row = new DockPanel();
        if (p.KeyUrl is { } url)
        {
            var link = Look.Button(p.NeedsKey ? "取得免費金鑰" : "下載 Ollama", () => OpenUrl(url));
            link.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(link, Dock.Right);
            row.Children.Add(link);
        }
        var box = new PasswordBox
        {
            Password = KeyStore.Get(p.Id) ?? "", Background = Look.Field, Foreground = Look.Moon, CaretBrush = Look.Gold, BorderBrush = Look.Hairline,
            Padding = new Thickness(8, 5, 8, 5), FontSize = 13, IsEnabled = p.NeedsKey, ToolTip = p.NeedsKey ? "貼上金鑰" : "本機 Ollama 不需要金鑰",
        };
        row.Children.Add(p.NeedsKey ? (UIElement)box : new TextBlock { Text = "不需要金鑰，安裝並啟動 Ollama 後按「儲存並測試」。", Foreground = Look.Tertiary, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(row, 2);
        grid.Children.Add(row);
        _rows[p.Id] = (box, status);
        return new Border { Child = grid, Background = Look.Panel, BorderBrush = Look.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
    }

    private async void SaveAndTest()
    {
        foreach (var p in Provider.All.Append(ImageGenerator.Cloudflare).Where(p => p.NeedsKey)) KeyStore.Set(p.Id, _rows[p.Id].Box.Password);
        await TestAsync();
    }

    private async Task TestAsync()
    {
        foreach (var (_, row) in _rows) { row.Status.Text = "檢查中…"; row.Status.Foreground = Look.Tertiary; }
        Dictionary<string, string> report;
        try { report = await _conductor.RefreshAsync(CancellationToken.None); }
        catch (Exception e) { foreach (var (_, row) in _rows) row.Status.Text = e.Message; return; }
        foreach (var (id, row) in _rows)
        {
            if (id == ImageGenerator.CloudflareId)
            {
                string key = row.Box.Password;
                bool ok = ImageGenerator.SplitCloudflareKey(key) is not null;
                row.Status.Text = key.Length == 0 ? "未設定（可不設）" : ok ? "✓ 已儲存，畫圖時使用" : "格式要是「帳戶ID:權杖」";
                row.Status.Foreground = key.Length == 0 ? Look.Tertiary : ok ? Look.Gold : Look.Cinnabar;
                continue;
            }
            string text = report.TryGetValue(id, out var r) ? r : "";
            bool ok = text.EndsWith("個模型", StringComparison.Ordinal);
            row.Status.Text = ok ? "✓ " + text : text;
            row.Status.Foreground = ok ? Look.Gold : text == "尚未設定金鑰" ? Look.Tertiary : Look.Cinnabar;
            row.Status.ToolTip = text;
            row.Status.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Status.MaxWidth = 300;
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
    }
}
