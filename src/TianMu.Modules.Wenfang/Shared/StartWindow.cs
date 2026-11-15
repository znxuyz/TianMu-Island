using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace TianMu.Modules.Wenfang.Shared;

/// <summary>文房的起始頁：開新的墨書／算籌／畫卷、開啟檔案、最近的檔案。也可以直接把檔案拖進來。</summary>
internal sealed class StartWindow : Window
{
    private readonly StackPanel _recent = new();

    public StartWindow()
    {
        Resources.MergedDictionaries.Add(Ui.Styles);
        Title = "文房";
        Width = 860; Height = 620; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        Background = Ui.Lacquer; Foreground = Ui.Moon; FontFamily = Ui.Sans;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 64, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0, 0, 0, 1), UseAeroCaptionButtons = false });
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { foreach (var f in files) WenfangHub.Open(f); Close(); }
        };

        var root = new DockPanel { Margin = new Thickness(32, 18, 14, 24) };

        // 標頭
        var head = new DockPanel();
        var close = Ui.Small("", "關閉", Close);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        close.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(close);
        var seal = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(5), Background = Ui.Cinnabar, Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock { Text = "文", FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        DockPanel.SetDock(seal, Dock.Left);
        head.Children.Add(seal);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "文房", FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 24 });
        titles.Children.Add(new TextBlock { Text = "文件、試算表、簡報 · 開得了 Word、Excel、PowerPoint 的檔案", FontSize = 12, Foreground = Ui.Tertiary, Margin = new Thickness(0, 2, 0, 0) });
        head.Children.Add(titles);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // 開新的
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 26, 6, 0) };
        cards.Children.Add(Card("墨", "墨書", "新文件", "像 Word：寫文章、報告、信件", () => WenfangHub.New(WenfangHub.Kind.Writer)));
        cards.Children.Add(Card("籌", "算籌", "新試算表", "像 Excel：表格、公式、加總", () => WenfangHub.New(WenfangHub.Kind.Sheet)));
        cards.Children.Add(Card("卷", "畫卷", "新簡報", "像 PowerPoint：投影片、全螢幕放映", () => WenfangHub.New(WenfangHub.Kind.Slides)));
        DockPanel.SetDock(cards, Dock.Top);
        root.Children.Add(cards);

        // 最近
        var recentHead = new DockPanel { Margin = new Thickness(0, 28, 18, 8) };
        var open = new Button { Content = "開啟檔案…", Style = Application.Current.TryFindResource("TianMu.Button.Ghost") as Style };
        open.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "文房能開的檔案|*.docx;*.rtf;*.txt;*.md;*.xlsx;*.xlsm;*.csv;*.pptx|Word 文件|*.docx;*.rtf;*.txt;*.md|Excel 試算表|*.xlsx;*.xlsm;*.csv|PowerPoint 簡報|*.pptx",
                Title = "開啟 — 文房",
            };
            if (dlg.ShowDialog(this) == true) { WenfangHub.Open(dlg.FileName); Close(); }
        };
        DockPanel.SetDock(open, Dock.Right);
        recentHead.Children.Add(open);
        recentHead.Children.Add(new TextBlock { Text = "最近的檔案", FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Ui.Gold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(recentHead, Dock.Top);
        root.Children.Add(recentHead);

        root.Children.Add(new ScrollViewer { Content = _recent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 18, 0) });
        Content = root;
        FillRecent();
        Activated += (_, _) => FillRecent();
    }

    private Border Card(string seal, string app, string action, string desc, Action click)
    {
        var stack = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        var icon = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xC8, 0xA8, 0x69)), HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = Ui.Gold, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = seal, FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 22, Foreground = Ui.Gold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = app, FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 20, Margin = new Thickness(0, 12, 0, 0) });
        stack.Children.Add(new TextBlock { Text = action, FontSize = 13, Foreground = Ui.Gold, Margin = new Thickness(0, 2, 0, 0) });
        stack.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Ui.Tertiary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var b = new Button { Content = stack, Style = (Style)Ui.Styles["WfButton"], HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
        b.Click += (_, _) => { click(); Close(); };
        return new Border { Child = b, CornerRadius = new CornerRadius(12), Background = Ui.Hover, BorderBrush = Ui.Hairline, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
    }

    private void FillRecent()
    {
        _recent.Children.Clear();
        var list = WenfangHub.Recent.Load();
        if (list.Count == 0)
        {
            _recent.Children.Add(new TextBlock { Text = "還沒有開過檔案。也可以把 Word、Excel、PowerPoint 檔案直接拖到這個視窗。", Foreground = Ui.Tertiary, FontSize = 13, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var path in list)
        {
            string seal = WenfangHub.KindOf(path) switch { WenfangHub.Kind.Sheet => "籌", WenfangHub.Kind.Slides => "卷", _ => "墨" };
            bool exists = File.Exists(path);
            var row = new DockPanel { Margin = new Thickness(6, 7, 6, 7) };
            var mark = new TextBlock { Text = seal, FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Ui.Gold, Width = 30, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            var when = new TextBlock { FontSize = 12, Foreground = Ui.Tertiary, VerticalAlignment = VerticalAlignment.Center,
                Text = exists ? File.GetLastWriteTime(path).ToString("M/d HH:mm") : "找不到檔案" };
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 14, Foreground = exists ? Ui.Moon : Ui.Tertiary });
            names.Children.Add(new TextBlock { Text = Path.GetDirectoryName(path), FontSize = 11, Foreground = Ui.Tertiary, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(names);
            var b = new Button { Content = row, Style = (Style)Ui.Styles["WfButton"], HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = exists };
            b.Click += (_, _) => { WenfangHub.Open(path); Close(); };
            _recent.Children.Add(b);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnPreviewKeyDown(e);
    }
}
