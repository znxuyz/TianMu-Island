using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TianMu.Modules.Wenfang.Shared;

namespace TianMu.Modules.Wenfang.Writer;

/// <summary>
/// 墨書：像 Word 的文件編輯器。白紙置中、上方功能區（常用、插入、版面配置、檢視）、下方字數與縮放。
/// 讀寫 .docx（Open XML SDK）、.rtf、.txt、.md。
/// </summary>
internal sealed class WriterWindow : EditorWindow
{
    public const string DefaultFont = "Microsoft JhengHei";
    public const double DefaultSizePt = 12;

    private readonly RichTextBox _rtb = new()
    {
        AcceptsTab = true, BorderThickness = new Thickness(0), Background = Brushes.White, Foreground = Brushes.Black,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, SpellCheck = { IsEnabled = false },
    };
    private readonly Border _page = new() { Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 40),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35 } };
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly DispatcherTimer _countTimer;
    private PageSetup _setup = PageSetup.A4;
    private bool _syncing, _loading;

    // 功能區上會跟著游標變化的控制項
    private readonly ComboBox _font, _size;
    private readonly ToggleButton _bold, _italic, _underline, _strike, _left, _center, _right, _justify;
    private readonly TextBlock _zoomText = new() { FontSize = 12, Foreground = Ui.Secondary, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    // 尋找與取代
    private readonly Border _findBar;
    private readonly TextBox _findBox, _replaceBox;
    private readonly StackPanel _replaceRow;

    public WriterWindow() : base("墨", "墨書")
    {
        _loading = true;   // 建構時套用紙張等設定，不算「改過」
        _rtb.Document = NewFlowDocument();
        _rtb.TextChanged += (_, _) => { if (!_loading) Dirty = true; _countTimer!.Stop(); _countTimer.Start(); };
        _rtb.SelectionChanged += (_, _) => SyncRibbon();
        _countTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _countTimer!.Stop(); UpdateCount(); }, Dispatcher);

        _page.Child = _rtb;
        _page.LayoutTransform = _zoom;
        ApplySetup(_setup);
        var scroller = new ScrollViewer { Content = _page, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x25, 0x20)),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            SetZoom(_zoom.ScaleX + (e.Delta > 0 ? 0.1 : -0.1));
            e.Handled = true;
        };

        // ── 尋找與取代列 ──
        _findBox = new TextBox { Width = 220, Style = (Style)Ui.Styles["WfField"] };
        _replaceBox = new TextBox { Width = 220, Style = (Style)Ui.Styles["WfField"] };
        _findBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) FindNext(); if (e.Key == Key.Escape) HideFind(); };
        _replaceBox.KeyDown += (_, e) => { if (e.Key == Key.Escape) HideFind(); };
        var findRow = Ui.Row(new TextBlock { Text = "尋找", Width = 40, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }, _findBox,
            TextButton("下一個", () => FindNext()), TextButton("關閉", HideFind));
        _replaceRow = Ui.Row(new TextBlock { Text = "取代為", Width = 40, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }, _replaceBox,
            TextButton("取代", ReplaceOne), TextButton("全部取代", ReplaceAll));
        _replaceRow.Margin = new Thickness(0, 4, 0, 0);
        var findStack = new StackPanel { Margin = new Thickness(14, 8, 14, 8) };
        findStack.Children.Add(findRow);
        findStack.Children.Add(_replaceRow);
        _findBar = new Border { Child = findStack, Visibility = Visibility.Collapsed, BorderBrush = Ui.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };

        var body = new DockPanel();
        DockPanel.SetDock(_findBar, Dock.Top);
        body.Children.Add(_findBar);
        body.Children.Add(scroller);
        SetBody(body);

        // ── 功能區 ──
        _font = Ui.Combo(150, FontNames(), editable: true);
        _font.SelectionChanged += (_, _) => { if (!_syncing && _font.SelectedItem is string f) ApplyFont(f); };
        _font.LostKeyboardFocus += (_, _) => { if (!_syncing && !string.IsNullOrWhiteSpace(_font.Text)) ApplyFont(_font.Text); };
        _size = Ui.Combo(58, ["8", "9", "10", "10.5", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"], editable: true);
        _size.SelectionChanged += (_, _) => { if (!_syncing && _size.SelectedItem is string s) ApplySize(s); };
        _size.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplySize(_size.Text); };

        _bold = Ui.Toggle(new TextBlock { Text = "B", FontWeight = FontWeights.Bold, FontSize = 14 }, "粗體（Ctrl+B）", _ => Exec(EditingCommands.ToggleBold));
        _italic = Ui.Toggle(new TextBlock { Text = "I", FontStyle = FontStyles.Italic, FontSize = 14, FontFamily = new FontFamily("Georgia") }, "斜體（Ctrl+I）", _ => Exec(EditingCommands.ToggleItalic));
        _underline = Ui.Toggle(new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline, FontSize = 14 }, "底線（Ctrl+U）", _ => Exec(EditingCommands.ToggleUnderline));
        _strike = Ui.Toggle(new TextBlock { Text = "S", TextDecorations = TextDecorations.Strikethrough, FontSize = 14 }, "刪除線", _ => ToggleStrike());
        _left = Ui.Toggle(Ui.Glyph("", 14), "靠左對齊（Ctrl+L）", _ => Exec(EditingCommands.AlignLeft));
        _center = Ui.Toggle(Ui.Glyph("", 14), "置中（Ctrl+E）", _ => Exec(EditingCommands.AlignCenter));
        _right = Ui.Toggle(Ui.Glyph("", 14), "靠右對齊（Ctrl+R）", _ => Exec(EditingCommands.AlignRight));
        _justify = Ui.Toggle(new TextBlock { Text = "≡", FontSize = 15 }, "左右對齊（Ctrl+J）", _ => Exec(EditingCommands.AlignJustify));

        var fontColor = Ui.Small("", "字型色彩", () => { });
        fontColor.ContextMenu = Ui.Palette(c => Apply(TextElement.ForegroundProperty, new SolidColorBrush(c ?? Colors.Black)));
        fontColor.Click += (_, _) => { fontColor.ContextMenu.PlacementTarget = fontColor; fontColor.ContextMenu.IsOpen = true; };
        var highlight = Ui.Small("", "螢光筆", () => { });
        highlight.ContextMenu = Ui.Palette(c => Apply(TextElement.BackgroundProperty, c is { } col ? new SolidColorBrush(col) : null), "無色彩");
        highlight.Click += (_, _) => { highlight.ContextMenu.PlacementTarget = highlight; highlight.ContextMenu.IsOpen = true; };

        AddTab("常用",
            Ui.Group("剪貼簿", Ui.Row(Ui.Big("", "貼上", () => Exec(ApplicationCommands.Paste), "貼上（Ctrl+V）"),
                VStack(Ui.Small("", "剪下（Ctrl+X）", () => Exec(ApplicationCommands.Cut)), Ui.Small("", "複製（Ctrl+C）", () => Exec(ApplicationCommands.Copy))))),
            Ui.Group("字型",
                Ui.Row(_font, _size, Ui.Small("A+", "放大字型", () => Exec(EditingCommands.IncreaseFontSize), text: true), Ui.Small("A-", "縮小字型", () => Exec(EditingCommands.DecreaseFontSize), text: true)),
                Ui.Row(_bold, _italic, _underline, _strike, fontColor, highlight, Ui.Small("", "清除格式", ClearFormatting))),
            Ui.Group("段落",
                Ui.Row(Ui.Small("", "項目符號", () => Exec(EditingCommands.ToggleBullets)), Ui.Small("1.", "編號", () => Exec(EditingCommands.ToggleNumbering), text: true),
                    Ui.Small("", "減少縮排", () => Exec(EditingCommands.DecreaseIndentation)), Ui.Small("", "增加縮排", () => Exec(EditingCommands.IncreaseIndentation))),
                Ui.Row(_left, _center, _right, _justify)),
            Ui.Group("樣式", Ui.Row(
                StyleButton("內文", ParagraphStyle.Normal), StyleButton("標題 1", ParagraphStyle.Heading1),
                StyleButton("標題 2", ParagraphStyle.Heading2), StyleButton("標題 3", ParagraphStyle.Heading3))),
            Ui.Group("編輯", Ui.Row(Ui.Big("", "尋找", () => ShowFind(false), "尋找（Ctrl+F）"), Ui.Big("", "取代", () => ShowFind(true), "取代（Ctrl+H）"))));

        var tableButton = Ui.Big("", "表格", () => { }, "插入表格");
        tableButton.Click += (_, _) => ShowTablePicker(tableButton);
        var widthCombo = Ui.Combo(90, ["原始大小", "頁面寬度", "一半", "四分之一"]);
        widthCombo.SelectionChanged += (_, _) => { if (widthCombo.SelectedIndex >= 0) { ResizeImages(widthCombo.SelectedIndex); widthCombo.SelectedIndex = -1; } };
        AddTab("插入",
            Ui.Group("頁面", Ui.Row(Ui.Big("", "分頁符號", InsertPageBreak, "從下一頁開始（Ctrl+Enter）"))),
            Ui.Group("表格", Ui.Row(tableButton, VStack(
                Ui.Row(Ui.Small("↑", "在上方插入列", () => TableOp(TableAction.RowAbove), text: true), Ui.Small("↓", "在下方插入列", () => TableOp(TableAction.RowBelow), text: true), Ui.Small("✕列", "刪除列", () => TableOp(TableAction.DeleteRow), text: true)),
                Ui.Row(Ui.Small("←", "在左側插入欄", () => TableOp(TableAction.ColumnLeft), text: true), Ui.Small("→", "在右側插入欄", () => TableOp(TableAction.ColumnRight), text: true), Ui.Small("✕欄", "刪除欄", () => TableOp(TableAction.DeleteColumn), text: true))))),
            Ui.Group("圖例", Ui.Row(Ui.Big("", "圖片", InsertPicture, "插入圖片"), VStack(new TextBlock { Text = "選取的圖片", FontSize = 11, Foreground = Ui.Tertiary }, widthCombo))),
            Ui.Group("文字", Ui.Row(Ui.Big("", "日期時間", () => InsertText(DateTime.Now.ToString("yyyy年M月d日 HH:mm")), "插入目前日期與時間"))));

        AddTab("版面配置",
            Ui.Group("紙張", Ui.Row(TextButton("A4", () => ApplySetup(PageSetup.A4 with { Margin = _setup.Margin })), TextButton("Letter", () => ApplySetup(PageSetup.Letter with { Margin = _setup.Margin })),
                TextButton("A5", () => ApplySetup(PageSetup.A5 with { Margin = _setup.Margin })))),
            Ui.Group("方向", Ui.Row(TextButton("直向", () => ApplySetup(_setup.Portrait())), TextButton("橫向", () => ApplySetup(_setup.Landscape())))),
            Ui.Group("邊界", Ui.Row(TextButton("標準", () => ApplySetup(_setup with { Margin = 2.54 * PageSetup.Cm })), TextButton("窄", () => ApplySetup(_setup with { Margin = 1.27 * PageSetup.Cm })),
                TextButton("寬", () => ApplySetup(_setup with { Margin = 3.18 * PageSetup.Cm })))));

        AddTab("檢視",
            Ui.Group("縮放", Ui.Row(Ui.Big("", "縮小", () => SetZoom(_zoom.ScaleX - 0.1)), Ui.Big("", "放大", () => SetZoom(_zoom.ScaleX + 0.1)),
                Ui.Big("", "100%", () => SetZoom(1)), Ui.Big("", "頁面寬度", FitWidth))));
        AddTab("檔案", FileGroup());

        StatusRight.Children.Add(Ui.Small("", "縮小", () => SetZoom(_zoom.ScaleX - 0.1)));
        StatusRight.Children.Add(Ui.Small("", "放大", () => SetZoom(_zoom.ScaleX + 0.1)));
        StatusRight.Children.Add(_zoomText);
        SetZoom(1);
        UpdateCount();
        _loading = false;
        Dirty = false;
        Loaded += (_, _) => { _rtb.Focus(); SyncRibbon(); };
    }

    // ═══════════════════════ 小工具 ═══════════════════════

    private static StackPanel VStack(params UIElement[] items)
    {
        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    private static Button TextButton(string text, Action click)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontSize = 12 }, Padding = new Thickness(10, 5, 10, 5), Style = (Style)Ui.Styles["WfButton"] };
        b.Click += (_, _) => click();
        return b;
    }

    private Button StyleButton(string label, ParagraphStyle style)
    {
        var (size, weight) = StyleLook(style);
        var b = new Button
        {
            Content = new TextBlock { Text = label, FontSize = Math.Min(16, size * 0.75), FontWeight = weight, FontFamily = Ui.Serif },
            Padding = new Thickness(10, 6, 10, 6), Style = (Style)Ui.Styles["WfButton"], ToolTip = $"套用「{label}」",
        };
        b.Click += (_, _) => ApplyParagraphStyle(style);
        return b;
    }

    private void Exec(RoutedUICommand command)
    {
        _rtb.Focus();
        command.Execute(null, _rtb);
        SyncRibbon();
    }

    private void Apply(DependencyProperty property, object? value)
    {
        _rtb.Focus();
        _rtb.Selection.ApplyPropertyValue(property, value);
        Dirty = true;
        SyncRibbon();
    }

    private static FlowDocument NewFlowDocument() => new()
    {
        FontFamily = new FontFamily(DefaultFont), FontSize = PtToPx(DefaultSizePt), Foreground = Brushes.Black,
        TextAlignment = TextAlignment.Left, LineHeight = double.NaN,
    };

    public static double PtToPx(double pt) => pt * 96 / 72;
    public static double PxToPt(double px) => px * 72 / 96;

    // ═══════════════════════ 版面 ═══════════════════════

    private void ApplySetup(PageSetup setup)
    {
        _setup = setup;
        var doc = _rtb.Document;
        doc.PageWidth = setup.Width;
        doc.PagePadding = new Thickness(setup.Margin);
        doc.ColumnWidth = setup.Width;   // 單欄
        _rtb.Width = setup.Width;
        _rtb.MinHeight = setup.Height;
        Dirty = Dirty || !_loading;
        Status.Text = $"紙張 {setup.Width / PageSetup.Cm:0.#} × {setup.Height / PageSetup.Cm:0.#} 公分 · 邊界 {setup.Margin / PageSetup.Cm:0.##} 公分";
    }

    private void SetZoom(double z)
    {
        z = Math.Clamp(Math.Round(z, 2), 0.3, 3);
        _zoom.ScaleX = _zoom.ScaleY = z;
        _zoomText.Text = $"{z * 100:0}%";
    }

    private void FitWidth()
    {
        if (_page.Parent is ScrollViewer sv && sv.ViewportWidth > 0) SetZoom((sv.ViewportWidth - 40) / _setup.Width);
    }

    private void UpdateCount()
    {
        string text = new TextRange(_rtb.Document.ContentStart, _rtb.Document.ContentEnd).Text;
        var (chars, words) = Count(text);
        Status.Text = $"字數 {chars:N0}（英文 {words:N0} 字）";
    }

    /// <summary>中文以字計、英文以詞計（純邏輯，方便測試）。</summary>
    public static (int Chars, int Words) Count(string text)
    {
        int chars = 0, words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { inWord = false; continue; }
            chars++;
            bool latin = c < 0x2E80 && char.IsLetterOrDigit(c);
            if (latin && !inWord) words++;
            inWord = latin;
        }
        return (chars, words);
    }

    // ═══════════════════════ 字型與段落 ═══════════════════════

    private readonly Dictionary<string, string> _fontByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>系統字型（有中文名稱就顯示中文名稱），常用的放最前面。</summary>
    private IEnumerable<string> FontNames()
    {
        var zh = XmlLanguage.GetLanguage("zh-tw");
        var names = new List<string>();
        foreach (var f in Fonts.SystemFontFamilies)
        {
            string name = f.FamilyNames.TryGetValue(zh, out var local) ? local : f.Source;
            _fontByName[name] = f.Source;
            _fontByName[f.Source] = f.Source;
            names.Add(name);
        }
        names = names.Distinct().OrderBy(n => n).ToList();
        string[] common = ["微軟正黑體", "新細明體", "標楷體", "Calibri", "Times New Roman", "Arial"];
        return common.Where(names.Contains).Concat(names.Where(n => !common.Contains(n)));
    }

    private string DisplayFont(string source)
    {
        foreach (var kv in _fontByName) if (kv.Value == source && kv.Key.Any(ch => ch >= 0x2E80)) return kv.Key;
        return source;
    }

    private void ApplyFont(string name)
    {
        string source = _fontByName.TryGetValue(name.Trim(), out var s) ? s : name.Trim();
        Apply(TextElement.FontFamilyProperty, new FontFamily(source));
    }

    private void ApplySize(string text)
    {
        if (double.TryParse(text, out double pt) && pt is >= 1 and <= 400) Apply(TextElement.FontSizeProperty, PtToPx(pt));
    }

    private void ToggleStrike()
    {
        var current = _rtb.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool on = current is not null && current.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        Apply(Inline.TextDecorationsProperty, on ? new TextDecorationCollection() : TextDecorations.Strikethrough);
    }

    private void ClearFormatting()
    {
        _rtb.Focus();
        var sel = _rtb.Selection;
        sel.ClearAllProperties();
        sel.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(DefaultFont));
        sel.ApplyPropertyValue(TextElement.FontSizeProperty, PtToPx(DefaultSizePt));
        Dirty = true;
        SyncRibbon();
    }

    public enum ParagraphStyle { Normal, Heading1, Heading2, Heading3 }

    /// <summary>樣式的字級（像素）與粗細。</summary>
    public static (double Size, FontWeight Weight) StyleLook(ParagraphStyle style) => style switch
    {
        ParagraphStyle.Heading1 => (PtToPx(22), FontWeights.Bold),
        ParagraphStyle.Heading2 => (PtToPx(16), FontWeights.Bold),
        ParagraphStyle.Heading3 => (PtToPx(14), FontWeights.Bold),
        _ => (PtToPx(DefaultSizePt), FontWeights.Normal),
    };

    public static void SetStyle(Paragraph p, ParagraphStyle style)
    {
        var (size, weight) = StyleLook(style);
        p.Tag = style == ParagraphStyle.Normal ? null : style.ToString();
        new TextRange(p.ContentStart, p.ContentEnd).ApplyPropertyValue(TextElement.FontSizeProperty, size);
        new TextRange(p.ContentStart, p.ContentEnd).ApplyPropertyValue(TextElement.FontWeightProperty, weight);
        p.FontSize = size;
        p.FontWeight = weight;
        p.Margin = new Thickness(0, style == ParagraphStyle.Normal ? 0 : PtToPx(8), 0, PtToPx(style == ParagraphStyle.Normal ? 6 : 4));
    }

    private void ApplyParagraphStyle(ParagraphStyle style)
    {
        foreach (var p in SelectedParagraphs()) SetStyle(p, style);
        Dirty = true;
        _rtb.Focus();
        SyncRibbon();
    }

    private IEnumerable<Paragraph> SelectedParagraphs()
    {
        var list = new List<Paragraph>();
        TextPointer? pos = _rtb.Selection.Start;
        while (pos is not null && pos.CompareTo(_rtb.Selection.End) <= 0)
        {
            var p = pos.Paragraph;
            if (p is null) { pos = pos.GetNextInsertionPosition(LogicalDirection.Forward); continue; }
            if (!list.Contains(p)) list.Add(p);
            pos = p.ContentEnd.GetNextInsertionPosition(LogicalDirection.Forward);
        }
        return list;
    }

    /// <summary>游標移動時，功能區跟著顯示目前的字型、粗細、對齊。</summary>
    private void SyncRibbon()
    {
        _syncing = true;
        try
        {
            var sel = _rtb.Selection;
            _bold.IsChecked = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w >= FontWeights.SemiBold;
            _italic.IsChecked = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
            var deco = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            _underline.IsChecked = deco?.Any(d => d.Location == TextDecorationLocation.Underline) == true;
            _strike.IsChecked = deco?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;
            var align = sel.GetPropertyValue(Paragraph.TextAlignmentProperty);
            _left.IsChecked = align is TextAlignment.Left;
            _center.IsChecked = align is TextAlignment.Center;
            _right.IsChecked = align is TextAlignment.Right;
            _justify.IsChecked = align is TextAlignment.Justify;
            _font.Text = sel.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily ff ? DisplayFont(ff.Source) : "";
            _size.Text = sel.GetPropertyValue(TextElement.FontSizeProperty) is double px ? $"{Math.Round(PxToPt(px) * 2) / 2:0.#}" : "";
        }
        finally { _syncing = false; }
    }

    // ═══════════════════════ 插入 ═══════════════════════

    private void InsertText(string text)
    {
        _rtb.Focus();
        _rtb.Selection.Text = text;
        _rtb.CaretPosition = _rtb.Selection.End;
        Dirty = true;
    }

    /// <summary>這個區塊在文件最上層的那一塊（表格、清單裡的段落要往上找）。</summary>
    private Block TopBlock(Block b)
    {
        DependencyObject current = b;
        while (current is TextElement te && te.Parent is not FlowDocument) current = te.Parent;
        return current as Block ?? b;
    }

    private Block CurrentTopBlock()
    {
        var p = _rtb.CaretPosition.Paragraph;
        if (p is null)
        {
            if (_rtb.Document.Blocks.LastBlock is { } last) return last;
            var np = new Paragraph();
            _rtb.Document.Blocks.Add(np);
            return np;
        }
        return TopBlock(p);
    }

    private void InsertPageBreak()
    {
        var after = CurrentTopBlock();
        var p = new Paragraph();
        MarkPageBreak(p);
        _rtb.Document.Blocks.InsertAfter(after, p);
        _rtb.CaretPosition = p.ContentStart;
        _rtb.Focus();
        Dirty = true;
    }

    /// <summary>分頁：列印時從新的一頁開始；畫面上用一條虛線標示。</summary>
    public static void MarkPageBreak(Paragraph p)
    {
        p.BreakPageBefore = true;
        p.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xB4, 0xA8));
        p.BorderThickness = new Thickness(0, 1, 0, 0);
        p.Padding = new Thickness(0, 12, 0, 0);
        p.Margin = new Thickness(0, 18, 0, p.Margin.Bottom);
    }

    private void ShowTablePicker(Button anchor)
    {
        const int max = 8;
        var label = new TextBlock { Text = "插入表格", Margin = new Thickness(4, 0, 4, 6), FontSize = 12 };
        var grid = new UniformGrid { Rows = max, Columns = max };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var cells = new Border[max, max];
        for (int r = 0; r < max; r++)
        for (int c = 0; c < max; c++)
        {
            int rr = r, cc = c;
            var cell = new Border { Width = 18, Height = 18, Margin = new Thickness(1), Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            cell.MouseEnter += (_, _) =>
            {
                label.Text = $"{cc + 1} × {rr + 1} 表格";
                for (int i = 0; i < max; i++) for (int j = 0; j < max; j++)
                    cells[i, j].Background = i <= rr && j <= cc ? new SolidColorBrush(Color.FromRgb(0xF0, 0xDC, 0xA8)) : Brushes.White;
            };
            cell.MouseLeftButtonUp += (_, _) => { popup.IsOpen = false; InsertTable(rr + 1, cc + 1); };
            cells[r, c] = cell;
            grid.Children.Add(cell);
        }
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(label);
        stack.Children.Add(grid);
        popup.Child = new Border { Child = stack, Background = Ui.Lacquer, BorderBrush = Ui.Gold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        popup.IsOpen = true;
    }

    public static Table NewTable(int rows, int cols, double contentWidth)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth / cols) });
        var group = new TableRowGroup();
        for (int r = 0; r < rows; r++) group.Rows.Add(NewRow(cols));
        table.RowGroups.Add(group);
        return table;
    }

    public static TableRow NewRow(int cols)
    {
        var row = new TableRow();
        for (int c = 0; c < cols; c++) row.Cells.Add(NewCell());
        return row;
    }

    public static TableCell NewCell() => new(new Paragraph()) { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Padding = new Thickness(5, 3, 5, 3) };

    private void InsertTable(int rows, int cols)
    {
        var after = CurrentTopBlock();
        var table = NewTable(rows, cols, _setup.Width - 2 * _setup.Margin);
        _rtb.Document.Blocks.InsertAfter(after, table);
        if (table.NextBlock is null) _rtb.Document.Blocks.Add(new Paragraph());
        _rtb.CaretPosition = table.RowGroups[0].Rows[0].Cells[0].ContentStart;
        _rtb.Focus();
        Dirty = true;
    }

    private enum TableAction { RowAbove, RowBelow, DeleteRow, ColumnLeft, ColumnRight, DeleteColumn }

    private void TableOp(TableAction action)
    {
        DependencyObject? d = _rtb.CaretPosition.Paragraph;
        while (d is not null and not TableCell) d = (d as TextElement)?.Parent;
        if (d is not TableCell cell || cell.Parent is not TableRow row || row.Parent is not TableRowGroup group || group.Parent is not Table table)
        {
            Status.Text = "先把游標放在表格裡";
            return;
        }
        int r = group.Rows.IndexOf(row), c = row.Cells.IndexOf(cell);
        switch (action)
        {
            case TableAction.RowAbove: group.Rows.Insert(r, NewRow(row.Cells.Count)); break;
            case TableAction.RowBelow: group.Rows.Insert(r + 1, NewRow(row.Cells.Count)); break;
            case TableAction.DeleteRow:
                if (group.Rows.Count > 1) group.Rows.Remove(row);
                else _rtb.Document.Blocks.Remove(table);
                break;
            case TableAction.ColumnLeft:
            case TableAction.ColumnRight:
                int at = action == TableAction.ColumnLeft ? c : c + 1;
                foreach (var tr in group.Rows) tr.Cells.Insert(Math.Min(at, tr.Cells.Count), NewCell());
                table.Columns.Insert(Math.Min(at, table.Columns.Count), new TableColumn { Width = table.Columns.Count > 0 ? table.Columns[0].Width : GridLength.Auto });
                break;
            case TableAction.DeleteColumn:
                if (row.Cells.Count <= 1) { _rtb.Document.Blocks.Remove(table); break; }
                foreach (var tr in group.Rows) if (c < tr.Cells.Count) tr.Cells.RemoveAt(c);
                if (c < table.Columns.Count) table.Columns.RemoveAt(c);
                break;
        }
        Dirty = true;
        _rtb.Focus();
    }

    private void InsertPicture()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有檔案|*.*", Title = "插入圖片" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            var image = MakeImage(bytes, Path.GetExtension(dlg.FileName), null, _setup.Width - 2 * _setup.Margin);
            _rtb.Focus();
            _ = new InlineUIContainer(image, _rtb.CaretPosition);
            Dirty = true;
        }
        catch (Exception e) { MessageBox.Show(this, $"這張圖片讀不進來：{e.Message}", "墨書"); }
    }

    /// <summary>圖片在文件裡的樣子；原始位元組放在 Tag，存檔時原封不動寫回。</summary>
    public static Image MakeImage(byte[] bytes, string ext, Size? size, double maxWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        double w = size?.Width ?? bmp.PixelWidth, h = size?.Height ?? bmp.PixelHeight;
        if (w > maxWidth && maxWidth > 0) { h = h * maxWidth / w; w = maxWidth; }
        return new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill, Tag = new ImageData(bytes, ext.TrimStart('.').ToLowerInvariant()) };
    }

    private void ResizeImages(int mode)
    {
        double content = _setup.Width - 2 * _setup.Margin;
        int n = 0;
        foreach (var image in ImagesIn(_rtb.Selection.Start, _rtb.Selection.End))
        {
            if (image.Source is not BitmapSource bmp) continue;
            double ratio = bmp.PixelHeight / (double)Math.Max(1, bmp.PixelWidth);
            double w = mode switch { 0 => Math.Min(bmp.PixelWidth, content), 1 => content, 2 => content / 2, _ => content / 4 };
            image.Width = w;
            image.Height = w * ratio;
            n++;
        }
        Status.Text = n == 0 ? "先選取要調整的圖片" : $"已調整 {n} 張圖片";
        if (n > 0) Dirty = true;
    }

    private static IEnumerable<Image> ImagesIn(TextPointer start, TextPointer end)
    {
        for (var p = start; p is not null && p.CompareTo(end) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: Image i }) yield return i;
            if (p.GetAdjacentElement(LogicalDirection.Forward) is BlockUIContainer { Child: Image bi }) yield return bi;
        }
    }

    // ═══════════════════════ 尋找與取代 ═══════════════════════

    private void ShowFind(bool replace)
    {
        _findBar.Visibility = Visibility.Visible;
        _replaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        if (!_rtb.Selection.IsEmpty && !_rtb.Selection.Text.Contains('\n')) _findBox.Text = _rtb.Selection.Text;
        _findBox.Focus();
        _findBox.SelectAll();
    }

    private void HideFind()
    {
        _findBar.Visibility = Visibility.Collapsed;
        _rtb.Focus();
    }

    private bool FindNext()
    {
        string term = _findBox.Text;
        if (term.Length == 0) return false;
        var found = Find(_rtb.Selection.End, term) ?? Find(_rtb.Document.ContentStart, term);
        if (found is null) { Status.Text = $"找不到「{term}」"; return false; }
        _rtb.Selection.Select(found.Start, found.End);
        found.Start.Paragraph?.BringIntoView();
        _rtb.Focus();
        Status.Text = "";
        return true;
    }

    /// <summary>從某個位置往後找一段文字（同一段文字格式內）。</summary>
    private TextRange? Find(TextPointer from, string term)
    {
        for (var p = from; p is not null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            string run = p.GetTextInRun(LogicalDirection.Forward);
            int i = run.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) continue;
            var start = p.GetPositionAtOffset(i);
            var end = start?.GetPositionAtOffset(term.Length);
            if (start is not null && end is not null) return new TextRange(start, end);
        }
        return null;
    }

    private void ReplaceOne()
    {
        if (!_rtb.Selection.IsEmpty && string.Equals(_rtb.Selection.Text, _findBox.Text, StringComparison.CurrentCultureIgnoreCase))
        {
            _rtb.Selection.Text = _replaceBox.Text;
            Dirty = true;
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        string term = _findBox.Text;
        if (term.Length == 0) return;
        int n = 0;
        var pos = _rtb.Document.ContentStart;
        while (Find(pos, term) is { } range)
        {
            range.Text = _replaceBox.Text;
            pos = range.End;
            if (++n > 10000) break;
        }
        if (n > 0) Dirty = true;
        Status.Text = n == 0 ? $"找不到「{term}」" : $"已取代 {n} 處";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.F) { ShowFind(false); e.Handled = true; return; }
        if (ctrl && e.Key == Key.H) { ShowFind(true); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Enter) { InsertPageBreak(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.L) { Exec(EditingCommands.AlignLeft); e.Handled = true; return; }
        if (ctrl && e.Key == Key.E) { Exec(EditingCommands.AlignCenter); e.Handled = true; return; }
        if (ctrl && e.Key == Key.R) { Exec(EditingCommands.AlignRight); e.Handled = true; return; }
        if (ctrl && e.Key == Key.J) { Exec(EditingCommands.AlignJustify); e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    // ═══════════════════════ 檔案 ═══════════════════════

    protected override string OpenFilter => "文件|*.docx;*.rtf;*.txt;*.md|Word 文件|*.docx|RTF|*.rtf|純文字|*.txt;*.md";
    protected override string SaveFilter => "Word 文件|*.docx|RTF 文件|*.rtf|純文字|*.txt";
    protected override string DefaultExtension => ".docx";
    protected override bool IsWritableFormat(string path) => Path.GetExtension(path).ToLowerInvariant() is ".rtf" or ".txt" or ".md";

    public override void Load(string path)
    {
        _loading = true;
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".docx")
            {
                var (doc, setup) = DocxReader.Read(path);
                _rtb.Document = doc;
                ApplySetup(setup);
            }
            else
            {
                var doc = NewFlowDocument();
                _rtb.Document = doc;
                ApplySetup(_setup);
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                if (ext == ".rtf")
                {
                    using var fs = File.OpenRead(path);
                    range.Load(fs, DataFormats.Rtf);
                }
                else
                {
                    doc.Blocks.Clear();
                    foreach (var line in ReadText(path).Replace("\r\n", "\n").Split('\n'))
                        doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0) });
                }
            }
        }
        finally
        {
            _loading = false;
            UpdateCount();
        }
    }

    /// <summary>純文字：先試 UTF-8，不合法再用 Big5（舊的繁體中文檔案）。</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(950).GetString(bytes);
        }
    }

    protected override void Write(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var doc = _rtb.Document;
        if (ext == ".docx") DocxWriter.Write(doc, _setup, path);
        else if (ext == ".rtf")
        {
            using var fs = File.Create(path);
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(fs, DataFormats.Rtf);
        }
        else File.WriteAllText(path, new TextRange(doc.ContentStart, doc.ContentEnd).Text, new UTF8Encoding(false));
    }

    protected override void NewDocument() => WenfangHub.New(WenfangHub.Kind.Writer);
    protected override void Undo() { _rtb.Undo(); }
    protected override void Redo() { _rtb.Redo(); }

    /// <summary>列印：複製一份文件（含圖片）排成紙張大小再送印；選「Microsoft Print to PDF」就能存成 PDF。</summary>
    protected override void Print()
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var source = _rtb.Document;
        var copy = new FlowDocument();
        using (var ms = new MemoryStream())
        {
            new TextRange(source.ContentStart, source.ContentEnd).Save(ms, DataFormats.XamlPackage);
            ms.Position = 0;
            new TextRange(copy.ContentStart, copy.ContentEnd).Load(ms, DataFormats.XamlPackage);
        }
        copy.FontFamily = source.FontFamily;
        copy.FontSize = source.FontSize;
        copy.PageWidth = _setup.Width;
        copy.PageHeight = _setup.Height;
        copy.PagePadding = new Thickness(_setup.Margin);
        copy.ColumnWidth = _setup.Width;
        dlg.PrintDocument(((IDocumentPaginatorSource)copy).DocumentPaginator, Title);
    }
}

/// <summary>文件裡一張圖片的原始檔案內容與格式（png、jpeg…）。</summary>
public sealed record ImageData(byte[] Bytes, string Extension);
