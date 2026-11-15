using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;

using TianMu.Modules.Wenfang.Shared;

namespace TianMu.Modules.Wenfang.Slides;

/// <summary>
/// 畫卷（簡報）：左邊投影片縮圖、中間編輯畫面（拖曳移動、拉角落調大小、按兩下打字），
/// F5 全螢幕放映；讀寫 .pptx。
/// </summary>
internal sealed class SlidesWindow : EditorWindow
{
    private const int MaxUndo = 50;

    private Deck _deck = Deck.CreateDefault();
    private int _current;
    private SlideItem? _selected;
    private readonly ListBox _thumbs = new()
    {
        Width = 214, Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x18, 0x14)), BorderThickness = new Thickness(0),
        Padding = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Grid _stageRoot = new() { Background = Brushes.Transparent };
    private readonly Canvas _overlay = new();
    private readonly Viewbox _viewbox = new() { Stretch = Stretch.Uniform, Margin = new Thickness(36) };
    private readonly Dictionary<SlideItem, FrameworkElement> _elements = [];
    private readonly List<Deck> _undo = [], _redo = [];
    private readonly Rectangle _selRect = new() { Stroke = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x69)), StrokeThickness = 2, StrokeDashArray = [4, 2], IsHitTestVisible = false };
    private readonly Thumb[] _handles = new Thumb[4];
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed,
        Background = new SolidColorBrush(Color.FromArgb(0x18, 0xC8, 0xA8, 0x69)), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private readonly ComboBox _size, _font;
    private readonly ToggleButton _bold, _italic, _bullets;
    private bool _syncing, _dragMoved;
    private Point _dragStart;
    private (double X, double Y) _dragOrigin;
    private SlideItem? _dragItem;
    private SlideItem? _clip;

    public SlidesWindow() : base("卷", "畫卷")
    {
        // ── 主體：縮圖＋舞台 ──
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        _thumbs.SelectionChanged += (_, _) => { if (!_syncing && _thumbs.SelectedIndex >= 0) GoTo(_thumbs.SelectedIndex); };
        _thumbs.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteSlide(); e.Handled = true; } };
        _thumbs.ContextMenu = Menu(("新增投影片", () => AddSlide(SlideLayout.TitleContent)), ("複製投影片", DuplicateSlide), ("刪除投影片", DeleteSlide),
            ("上移", () => MoveSlide(-1)), ("下移", () => MoveSlide(1)));
        body.Children.Add(_thumbs);

        var stage = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x25, 0x20)), ClipToBounds = true };
        Grid.SetColumn(stage, 1);
        _viewbox.Child = _stageRoot;
        stage.Child = _viewbox;
        body.Children.Add(stage);
        _overlay.Children.Add(_selRect);
        for (int i = 0; i < 4; i++)
        {
            int corner = i;
            var t = new Thumb { Width = 14, Height = 14, Cursor = i is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW, Visibility = Visibility.Collapsed, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0x86, 0x3E)), BorderThickness = new Thickness(2) };
            t.DragStarted += (_, _) => PushUndo();
            t.DragDelta += (_, e) => ResizeBy(corner, e.HorizontalChange, e.VerticalChange);
            t.DragCompleted += (_, _) => RefreshThumb(_current);
            _handles[i] = t;
            _overlay.Children.Add(t);
        }
        _overlay.Children.Add(_editor);
        _editor.LostKeyboardFocus += (_, _) => CommitText();
        _editor.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CommitText(); e.Handled = true; } };

        _stageRoot.MouseLeftButtonDown += StageDown;
        _stageRoot.MouseMove += StageMove;
        _stageRoot.MouseLeftButtonUp += (_, _) => StageUp();
        stage.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == stage) { CommitText(); Select(null); } };
        stage.AllowDrop = true;
        stage.Drop += StageDrop;
        SetBody(body);

        // ── 功能區 ──
        _font = Ui.Combo(130, Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCulture).Distinct(), editable: true);
        _font.SelectionChanged += (_, _) => { if (!_syncing && _font.SelectedItem is string f) Format(i => i.Font = f); };
        _size = Ui.Combo(54, ["12", "14", "16", "18", "20", "24", "28", "32", "36", "40", "44", "48", "54", "60", "72", "96"], editable: true);
        _size.SelectionChanged += (_, _) => { if (!_syncing && _size.SelectedItem is string s) SetSize(s); };
        _size.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetSize(_size.Text); };
        _bold = Ui.Toggle(new TextBlock { Text = "B", FontWeight = FontWeights.Bold, FontSize = 14 }, "粗體（Ctrl+B）", on => Format(i => i.Bold = on));
        _italic = Ui.Toggle(new TextBlock { Text = "I", FontStyle = FontStyles.Italic, FontSize = 14, FontFamily = new FontFamily("Georgia") }, "斜體（Ctrl+I）", on => Format(i => i.Italic = on));
        _bullets = Ui.Toggle(Ui.Glyph("", 14), "項目符號", on => Format(i => i.Bullets = on));
        var color = PaletteButton("", "字型色彩", c => Format(i => i.Color = c is { } col ? Ui.ToHex(col) : null), "跟著佈景主題");
        var fill = PaletteButton("", "形狀填滿", c => Format(i => i.Fill = c is { } col ? Ui.ToHex(col) : null), "跟著佈景主題");

        var newSlide = Ui.Big("", "新增投影片", () => { }, "新增投影片（Ctrl+M）");
        newSlide.ContextMenu = Menu(("標題投影片", () => AddSlide(SlideLayout.Title)), ("標題及內容", () => AddSlide(SlideLayout.TitleContent)),
            ("只有標題", () => AddSlide(SlideLayout.TitleOnly)), ("空白", () => AddSlide(SlideLayout.Blank)));
        OpenOnClick(newSlide);

        AddTab("常用",
            Ui.Group("投影片", Ui.Row(newSlide, VStack(Ui.Small("", "複製投影片", DuplicateSlide), Ui.Small("", "刪除投影片", DeleteSlide)))),
            Ui.Group("字型",
                Ui.Row(_font, _size, Ui.Small("A+", "放大字型", () => Grow(1), text: true), Ui.Small("A-", "縮小字型", () => Grow(-1), text: true)),
                Ui.Row(_bold, _italic, color, fill)),
            Ui.Group("段落",
                Ui.Row(Ui.Small("", "靠左對齊", () => Format(i => i.Align = HAlign.Left)), Ui.Small("", "置中", () => Format(i => i.Align = HAlign.Center)),
                    Ui.Small("", "靠右對齊", () => Format(i => i.Align = HAlign.Right)), _bullets),
                Ui.Row(Ui.Small("頂", "文字靠上", () => Format(i => i.Anchor = VAnchor.Top), text: true), Ui.Small("中", "文字置中", () => Format(i => i.Anchor = VAnchor.Middle), text: true),
                    Ui.Small("底", "文字靠下", () => Format(i => i.Anchor = VAnchor.Bottom), text: true))),
            Ui.Group("排列",
                Ui.Row(VStack(TextButton("移到最上層", () => Reorder(true)), TextButton("移到最下層", () => Reorder(false))),
                    Ui.Big("", "刪除物件", DeleteItem, "刪除選取的物件（Delete）"))));

        AddTab("插入",
            Ui.Group("投影片", Ui.Row(Ui.Big("", "新增投影片", () => AddSlide(SlideLayout.TitleContent)))),
            Ui.Group("文字", Ui.Row(Ui.Big("", "文字方塊", AddTextBox))),
            Ui.Group("圖例", Ui.Row(Ui.Big("", "圖片", InsertPicture, "插入圖片（也可以直接拖進來或貼上）"))),
            Ui.Group("圖案", Ui.Row(Ui.Big("", "矩形", () => AddShape("rect")), Ui.Big("", "圓角矩形", () => AddShape("roundRect")), Ui.Big("", "橢圓", () => AddShape("ellipse")))));

        var themeButtons = DeckTheme.All.Select(ThemeButton).Cast<UIElement>().ToArray();
        var background = PaletteButton("", "這張的背景色", c => { PushUndo(); CurrentSlide.Background = c is { } col ? Ui.ToHex(col) : null; Rerender(); RefreshThumb(_current); Dirty = true; }, "跟著佈景主題");
        AddTab("設計",
            Ui.Group("佈景主題", Ui.Row(themeButtons)),
            Ui.Group("背景", Ui.Row(background, TextButton("全部套用", ApplyBackgroundToAll))),
            Ui.Group("投影片大小", Ui.Row(TextButton("寬螢幕 16:9", () => Resize(1280, 720)), TextButton("標準 4:3", () => Resize(960, 720)))));

        AddTab("投影片放映",
            Ui.Group("開始放映", Ui.Row(Ui.Big("", "從頭開始", () => StartShow(0), "從第一張開始（F5）"), Ui.Big("", "從目前投影片", () => StartShow(_current), "從這一張開始（Shift+F5）"))));

        AddTab("檔案", FileGroup());

        Rebuild();
        Dirty = false;
    }

    private Slide CurrentSlide => _deck.Slides[_current];

    // ═══════════════════════ 檔案 ═══════════════════════

    protected override string OpenFilter => "PowerPoint 簡報|*.pptx";
    protected override string SaveFilter => "PowerPoint 簡報|*.pptx";
    protected override string DefaultExtension => ".pptx";

    public override void Load(string path)
    {
        _deck = PptxReader.Read(path);
        _undo.Clear();
        _redo.Clear();
        _current = 0;
        _selected = null;
        Rebuild();
    }

    protected override void Write(string path)
    {
        CommitText();
        PptxWriter.Write(_deck, path);
    }

    protected override void NewDocument() => WenfangHub.New(WenfangHub.Kind.Slides);

    protected override void Print()
    {
        CommitText();
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var doc = new FixedDocument();
        double pw = dlg.PrintableAreaWidth, ph = dlg.PrintableAreaHeight;
        bool landscape = pw > ph;
        foreach (var slide in _deck.Slides)
        {
            var page = new FixedPage { Width = pw, Height = ph };
            double w = pw - 96, h = w * _deck.Height / _deck.Width;
            if (h > ph - 96) { h = ph - 96; w = h * _deck.Width / _deck.Height; }
            var view = new Viewbox { Width = w, Height = h, Child = SlideView.Render(_deck, slide, false) };
            FixedPage.SetLeft(view, (pw - w) / 2);
            FixedPage.SetTop(view, landscape ? (ph - h) / 2 : 48);
            page.Children.Add(view);
            page.Measure(new Size(pw, ph));
            page.Arrange(new Rect(0, 0, pw, ph));
            page.UpdateLayout();
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }
        dlg.PrintDocument(doc.DocumentPaginator, "畫卷");
    }

    // ═══════════════════════ 復原 ═══════════════════════

    private void PushUndo()
    {
        _undo.Add(_deck.Clone());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        Dirty = true;
    }

    private void Restore(List<Deck> from, List<Deck> to)
    {
        CommitText();
        if (from.Count == 0) return;
        to.Add(_deck.Clone());
        _deck = from[^1];
        from.RemoveAt(from.Count - 1);
        _current = Math.Clamp(_current, 0, _deck.Slides.Count - 1);
        _selected = null;
        Rebuild();
        Dirty = true;
    }

    protected override void Undo() => Restore(_undo, _redo);
    protected override void Redo() => Restore(_redo, _undo);

    // ═══════════════════════ 畫面 ═══════════════════════

    /// <summary>整個重建：縮圖列＋目前投影片。</summary>
    private void Rebuild()
    {
        _syncing = true;
        _thumbs.Items.Clear();
        for (int i = 0; i < _deck.Slides.Count; i++) _thumbs.Items.Add(ThumbItem(i));
        _thumbs.SelectedIndex = _current;
        _syncing = false;
        Rerender();
    }

    private FrameworkElement ThumbItem(int i)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), Foreground = Ui.Secondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 0, 0) });
        var frame = new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xEC, 0xE6, 0xD8)), BorderThickness = new Thickness(1), Child = SlideView.Thumbnail(_deck, _deck.Slides[i], 168) };
        Grid.SetColumn(frame, 1);
        grid.Children.Add(frame);
        return grid;
    }

    private void RefreshThumb(int i)
    {
        if (i < 0 || i >= _thumbs.Items.Count) return;
        _syncing = true;
        _thumbs.Items[i] = ThumbItem(i);
        _thumbs.SelectedIndex = _current;
        _syncing = false;
    }

    /// <summary>重畫中間的投影片。</summary>
    private void Rerender()
    {
        _elements.Clear();
        _stageRoot.Children.Clear();
        _stageRoot.Width = _deck.Width;
        _stageRoot.Height = _deck.Height;
        _stageRoot.Children.Add(SlideView.Render(_deck, CurrentSlide, editing: true, _elements));
        _overlay.Width = _deck.Width;
        _overlay.Height = _deck.Height;
        _stageRoot.Children.Add(_overlay);
        if (_selected is not null && !CurrentSlide.Items.Contains(_selected)) _selected = null;
        UpdateAdorner();
        Status.Text = $"投影片 {_current + 1} / {_deck.Slides.Count}";
    }

    private void GoTo(int index)
    {
        CommitText();
        _current = Math.Clamp(index, 0, _deck.Slides.Count - 1);
        _selected = null;
        Rerender();
        _syncing = true;
        _thumbs.SelectedIndex = _current;
        _thumbs.ScrollIntoView(_thumbs.SelectedItem);
        _syncing = false;
        SyncToolbar();
    }

    private void Select(SlideItem? item)
    {
        _selected = item;
        UpdateAdorner();
        SyncToolbar();
    }

    private void UpdateAdorner()
    {
        bool on = _selected is not null;
        _selRect.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var h in _handles) h.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_selected is not { } s) return;
        Canvas.SetLeft(_selRect, s.X - 2);
        Canvas.SetTop(_selRect, s.Y - 2);
        _selRect.Width = s.W + 4;
        _selRect.Height = s.H + 4;
        (double X, double Y)[] corners = [(s.X, s.Y), (s.X + s.W, s.Y), (s.X, s.Y + s.H), (s.X + s.W, s.Y + s.H)];
        for (int i = 0; i < 4; i++)
        {
            Canvas.SetLeft(_handles[i], corners[i].X - 7);
            Canvas.SetTop(_handles[i], corners[i].Y - 7);
        }
    }

    private void SyncToolbar()
    {
        _syncing = true;
        if (_selected is { } s)
        {
            _size.Text = s.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
            _font.Text = _deck.FontOf(s);
            _bold.IsChecked = s.Bold;
            _italic.IsChecked = s.Italic;
            _bullets.IsChecked = s.Bullets;
        }
        _syncing = false;
    }

    // ═══════════════════════ 滑鼠 ═══════════════════════

    private SlideItem? HitItem(Point p)
    {
        for (int i = CurrentSlide.Items.Count - 1; i >= 0; i--)
        {
            var it = CurrentSlide.Items[i];
            if (p.X >= it.X && p.X <= it.X + it.W && p.Y >= it.Y && p.Y <= it.Y + it.H) return it;
        }
        return null;
    }

    private void StageDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb || IsInside(e.OriginalSource as DependencyObject, _editor)) return;
        CommitText();
        var p = e.GetPosition(_stageRoot);
        var hit = HitItem(p);
        Select(hit);
        Focus();
        if (hit is null) return;
        if (e.ClickCount == 2 && hit.Kind != ItemKind.Image) { BeginText(hit); e.Handled = true; return; }
        _dragItem = hit;
        _dragStart = p;
        _dragOrigin = (hit.X, hit.Y);
        _dragMoved = false;
        _stageRoot.CaptureMouse();
        e.Handled = true;
    }

    private void StageMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is not { } it || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_stageRoot);
        double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
        if (!_dragMoved) { PushUndo(); _dragMoved = true; }
        it.X = Snap(_dragOrigin.X + dx, it.W, _deck.Width);
        it.Y = Snap(_dragOrigin.Y + dy, it.H, _deck.Height);
        if (_elements.TryGetValue(it, out var el)) SlideView.Place(el, it);
        UpdateAdorner();
    }

    /// <summary>靠近置中或邊緣時吸附（像 PowerPoint 的智慧型參考線，簡化版）。</summary>
    private static double Snap(double pos, double size, double total)
    {
        double center = (total - size) / 2;
        if (Math.Abs(pos - center) < 6) return center;
        if (Math.Abs(pos) < 6) return 0;
        if (Math.Abs(pos + size - total) < 6) return total - size;
        return Math.Round(pos);
    }

    private void StageUp()
    {
        _stageRoot.ReleaseMouseCapture();
        if (_dragItem is not null && _dragMoved) RefreshThumb(_current);
        _dragItem = null;
    }

    private void ResizeBy(int corner, double dx, double dy)
    {
        if (_selected is not { } s) return;
        const double min = 16;
        bool left = corner is 0 or 2, top = corner is 0 or 1;
        if (left) { double nw = Math.Max(min, s.W - dx); s.X += s.W - nw; s.W = nw; } else s.W = Math.Max(min, s.W + dx);
        if (top) { double nh = Math.Max(min, s.H - dy); s.Y += s.H - nh; s.H = nh; } else s.H = Math.Max(min, s.H + dy);
        if (_elements.TryGetValue(s, out var el)) { SlideView.Place(el, s); if (s.Kind == ItemKind.Shape) Rerender(); }
        UpdateAdorner();
    }

    private static bool IsInside(DependencyObject? d, DependencyObject target)
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == target) return true;
        return false;
    }

    // ═══════════════════════ 打字 ═══════════════════════

    private void BeginText(SlideItem item)
    {
        _selected = item;
        UpdateAdorner();
        _editor.Text = item.Text;
        _editor.FontSize = item.FontSize * 96 / 72;
        _editor.FontFamily = new FontFamily(_deck.FontOf(item) + ", Microsoft JhengHei");
        _editor.FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        _editor.Foreground = SlideView.Brush(_deck.ColorOf(item));
        _editor.CaretBrush = _editor.Foreground;
        _editor.TextAlignment = item.Align switch { HAlign.Center => TextAlignment.Center, HAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
        _editor.Padding = new Thickness(SlideView.InsetX - 2, SlideView.InsetY, SlideView.InsetX - 2, SlideView.InsetY);
        Canvas.SetLeft(_editor, item.X);
        Canvas.SetTop(_editor, item.Y);
        _editor.Width = item.W;
        _editor.MinHeight = item.H;
        _editor.Tag = item;
        _editor.Visibility = Visibility.Visible;
        if (_elements.TryGetValue(item, out var el)) el.Visibility = Visibility.Hidden;
        _editor.Focus();
        _editor.CaretIndex = _editor.Text.Length;
    }

    private void CommitText()
    {
        if (_editor.Visibility != Visibility.Visible || _editor.Tag is not SlideItem item) return;
        _editor.Visibility = Visibility.Collapsed;
        _editor.Tag = null;
        string text = _editor.Text.Replace("\r\n", "\n");
        if (text != item.Text)
        {
            PushUndo();
            // PushUndo 複製的是改之前的狀態；item 仍是目前投影片上的那一個
            item.Text = text;
        }
        Rerender();
        RefreshThumb(_current);
    }

    // ═══════════════════════ 格式 ═══════════════════════

    private void Format(Action<SlideItem> apply)
    {
        if (_syncing || _selected is not { } s) return;
        CommitText();
        PushUndo();
        apply(s);
        Rerender();
        Select(s);
        RefreshThumb(_current);
    }

    private void SetSize(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt) && pt is >= 4 and <= 400)
            Format(i => i.FontSize = pt);
    }

    private void Grow(int dir)
    {
        if (_selected is not { } s) return;
        double[] steps = [10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 72, 96, 120];
        double next = dir > 0 ? steps.FirstOrDefault(x => x > s.FontSize, s.FontSize + 8) : steps.LastOrDefault(x => x < s.FontSize, Math.Max(4, s.FontSize - 2));
        Format(i => i.FontSize = next);
    }

    private void Reorder(bool front)
    {
        if (_selected is not { } s) return;
        PushUndo();
        CurrentSlide.Items.Remove(s);
        if (front) CurrentSlide.Items.Add(s); else CurrentSlide.Items.Insert(0, s);
        Rerender();
        Select(s);
        RefreshThumb(_current);
    }

    private void DeleteItem()
    {
        if (_selected is not { } s) return;
        PushUndo();
        CurrentSlide.Items.Remove(s);
        _selected = null;
        Rerender();
        RefreshThumb(_current);
    }

    private Button ThemeButton(DeckTheme t)
    {
        var swatch = new Border
        {
            Width = 64, Height = 38, Background = SlideView.Brush(t.Background), BorderBrush = SlideView.Brush(t.Accent), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3),
            Child = new TextBlock { Text = "Aa 標題", Foreground = SlideView.Brush(t.Title), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var stack = new StackPanel();
        stack.Children.Add(swatch);
        stack.Children.Add(new TextBlock { Text = t.Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
        var b = new Button { Content = stack, Padding = new Thickness(4), ToolTip = $"套用「{t.Name}」佈景主題", Style = (Style)Ui.Styles["WfButton"] };
        b.Click += (_, _) => { PushUndo(); _deck.Theme = t; Rebuild(); };
        return b;
    }

    private void ApplyBackgroundToAll()
    {
        PushUndo();
        string? bg = CurrentSlide.Background;
        foreach (var s in _deck.Slides) s.Background = bg;
        Rebuild();
    }

    /// <summary>切換 16:9／4:3：物件依寬度等比例縮放。</summary>
    private void Resize(double w, double h)
    {
        if (Math.Abs(_deck.Width - w) < 1 && Math.Abs(_deck.Height - h) < 1) return;
        PushUndo();
        double kx = w / _deck.Width, ky = h / _deck.Height, k = Math.Min(kx, ky);
        foreach (var s in _deck.Slides)
            foreach (var it in s.Items)
            {
                it.X *= kx; it.Y *= ky; it.W *= kx; it.H *= ky;
                if (it.Kind != ItemKind.Image) it.FontSize = Math.Round(it.FontSize * k, 1);
            }
        _deck.Width = w;
        _deck.Height = h;
        Rebuild();
    }

    // ═══════════════════════ 投影片 ═══════════════════════

    private void AddSlide(SlideLayout layout)
    {
        CommitText();
        PushUndo();
        _deck.Slides.Insert(_current + 1, _deck.NewSlide(layout));
        _current++;
        _selected = null;
        Rebuild();
    }

    private void DuplicateSlide()
    {
        CommitText();
        PushUndo();
        _deck.Slides.Insert(_current + 1, CurrentSlide.Clone());
        _current++;
        Rebuild();
    }

    private void DeleteSlide()
    {
        CommitText();
        PushUndo();
        _deck.Slides.RemoveAt(_current);
        if (_deck.Slides.Count == 0) _deck.Slides.Add(_deck.NewSlide(SlideLayout.Title));
        _current = Math.Min(_current, _deck.Slides.Count - 1);
        _selected = null;
        Rebuild();
    }

    private void MoveSlide(int delta)
    {
        int to = _current + delta;
        if (to < 0 || to >= _deck.Slides.Count) return;
        PushUndo();
        (_deck.Slides[_current], _deck.Slides[to]) = (_deck.Slides[to], _deck.Slides[_current]);
        _current = to;
        Rebuild();
    }

    // ═══════════════════════ 插入 ═══════════════════════

    private void AddItem(SlideItem item)
    {
        CommitText();
        PushUndo();
        CurrentSlide.Items.Add(item);
        Rerender();
        Select(item);
        RefreshThumb(_current);
    }

    private void AddTextBox()
    {
        var item = new SlideItem { Kind = ItemKind.Text, X = _deck.Width / 2 - 240, Y = _deck.Height / 2 - 40, W = 480, H = 80, FontSize = 24 };
        AddItem(item);
        BeginText(item);
    }

    private void AddShape(string geometry) =>
        AddItem(new SlideItem { Kind = ItemKind.Shape, Geometry = geometry, X = _deck.Width / 2 - 160, Y = _deck.Height / 2 - 100, W = 320, H = 200, FontSize = 24, Align = HAlign.Center, Anchor = VAnchor.Middle, Color = "FFFFFF" });

    private void InsertPicture()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff", Title = "插入圖片" };
        if (dlg.ShowDialog(this) == true) InsertImage(File.ReadAllBytes(dlg.FileName), Path.GetExtension(dlg.FileName));
    }

    private void InsertImage(byte[] bytes, string ext, Point? at = null)
    {
        var bmp = SlideView.Bitmap(bytes);
        if (bmp is null) { MessageBox.Show(this, "這個圖片格式讀不出來。", "畫卷"); return; }
        double w = bmp.PixelWidth, h = bmp.PixelHeight;
        double k = Math.Min(1, Math.Min(_deck.Width * 0.7 / w, _deck.Height * 0.7 / h));
        w *= k; h *= k;
        var p = at ?? new Point(_deck.Width / 2, _deck.Height / 2);
        AddItem(new SlideItem { Kind = ItemKind.Image, ImageBytes = bytes, ImageExtension = ext.ToLowerInvariant(), W = w, H = h, X = p.X - w / 2, Y = p.Y - h / 2 });
    }

    private static readonly string[] ImageExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff"];

    private void StageDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var images = files.Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
        if (images.Count == 0) return;
        var at = e.GetPosition(_stageRoot);
        foreach (var f in images) InsertImage(File.ReadAllBytes(f), Path.GetExtension(f), at);
        e.Handled = true;
    }

    private void Paste()
    {
        try
        {
            if (_clip is { } c && !Clipboard.ContainsImage() && !Clipboard.ContainsFileDropList())
            {
                var copy = c.Clone();
                copy.X += 20; copy.Y += 20;
                _clip = copy.Clone();
                AddItem(copy);
                return;
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img));
                using var ms = new MemoryStream();
                enc.Save(ms);
                InsertImage(ms.ToArray(), ".png");
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                foreach (string? f in Clipboard.GetFileDropList())
                    if (f is not null && ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())) InsertImage(File.ReadAllBytes(f), Path.GetExtension(f));
                return;
            }
            if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText().Trim();
                if (t.Length > 0) AddItem(new SlideItem { Kind = ItemKind.Text, Text = t, X = 120, Y = 200, W = _deck.Width - 240, H = 200, FontSize = 24 });
            }
        }
        catch (Exception) { /* 剪貼簿被其他程式占用 */ }
    }

    // ═══════════════════════ 放映 ═══════════════════════

    private void StartShow(int from)
    {
        CommitText();
        new SlideShowWindow(_deck.Clone(), from, this).Show();
    }

    // ═══════════════════════ 鍵盤 ═══════════════════════

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool typing = _editor.IsKeyboardFocused || Keyboard.FocusedElement is TextBox or ComboBox || (Keyboard.FocusedElement as DependencyObject) is { } d && IsInside(d, _size);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.F5) { StartShow(shift ? _current : 0); e.Handled = true; return; }
        if (ctrl && e.Key == Key.M) { AddSlide(SlideLayout.TitleContent); e.Handled = true; return; }
        if (!typing)
        {
            bool handled = true;
            switch (e.Key)
            {
                case Key.Z when ctrl: Undo(); break;
                case Key.Y when ctrl: Redo(); break;
                case Key.C when ctrl && _selected is not null: _clip = _selected.Clone(); break;
                case Key.X when ctrl && _selected is not null: _clip = _selected.Clone(); DeleteItem(); break;
                case Key.V when ctrl: Paste(); break;
                case Key.D when ctrl && _selected is not null: { var c = _selected.Clone(); c.X += 20; c.Y += 20; AddItem(c); } break;
                case Key.B when ctrl && _selected is not null: Format(i => i.Bold = !i.Bold); break;
                case Key.I when ctrl && _selected is not null: Format(i => i.Italic = !i.Italic); break;
                case Key.Delete when _selected is not null && !_thumbs.IsKeyboardFocusWithin: DeleteItem(); break;
                case Key.Enter or Key.F2 when _selected is { Kind: not ItemKind.Image } s: BeginText(s); break;
                case Key.Escape when _selected is not null: Select(null); break;
                case Key.Left or Key.Right or Key.Up or Key.Down when _selected is { } s:
                    double step = shift ? 10 : 1;
                    PushUndo();
                    s.X += e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                    s.Y += e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                    if (_elements.TryGetValue(s, out var el)) SlideView.Place(el, s);
                    UpdateAdorner();
                    RefreshThumb(_current);
                    break;
                case Key.PageDown or Key.Down: GoTo(_current + 1); break;
                case Key.PageUp or Key.Up: GoTo(_current - 1); break;
                default: handled = false; break;
            }
            if (handled) { e.Handled = true; return; }
        }
        base.OnPreviewKeyDown(e);
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
        var b = new Button { Content = new TextBlock { Text = text, FontSize = 12 }, Padding = new Thickness(8, 3, 8, 3), Style = (Style)Ui.Styles["WfButton"] };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button PaletteButton(string glyph, string tip, Action<Color?> pick, string clear)
    {
        var b = Ui.Small(glyph, tip, () => { });
        b.ContextMenu = Ui.Palette(pick, clear);
        OpenOnClick(b);
        return b;
    }

    private static void OpenOnClick(Button b) => b.Click += (_, _) => { b.ContextMenu.PlacementTarget = b; b.ContextMenu.Placement = PlacementMode.Bottom; b.ContextMenu.IsOpen = true; };

    private static ContextMenu Menu(params (string Label, Action Click)[] items)
    {
        var m = new ContextMenu();
        foreach (var (label, click) in items)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => click();
            m.Items.Add(mi);
        }
        return m;
    }
}
