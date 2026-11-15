using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;

namespace TianMu.Modules.Wenfang.Sheet;

/// <summary>
/// 算籌的格線：自己畫（只畫看得到的格子，大檔案也順），像 Excel 一樣
/// 點選、拖曳選取、方向鍵、直接打字輸入、F2 編輯、Ctrl+C/X/V、拖欄線調寬度；
/// 打公式時點其他格子會把位址填進公式。
/// </summary>
internal sealed class SheetGrid : Grid
{
    public const double HeaderW = 48, HeaderH = 24;
    private static readonly Brush GridLine = Frozen(Color.FromRgb(0xE2, 0xDF, 0xD8));
    private static readonly Brush HeaderBg = Frozen(Color.FromRgb(0xF3, 0xF0, 0xEA));
    private static readonly Brush HeaderSel = Frozen(Color.FromRgb(0xF0, 0xDC, 0xA8));
    private static readonly Brush SelFill = Frozen(Color.FromArgb(0x2E, 0xC8, 0xA8, 0x69));
    private static readonly Pen SelPen = FrozenPen(Color.FromRgb(0xA8, 0x86, 0x3E), 2);
    private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0xE2, 0xDF, 0xD8), 1);
    private static readonly Pen HeaderPen = FrozenPen(Color.FromRgb(0xCF, 0xCA, 0xC0), 1);

    private readonly SheetCanvas _canvas;
    private readonly ScrollBar _v = new() { Orientation = Orientation.Vertical, SmallChange = 1 };
    private readonly ScrollBar _h = new() { Orientation = Orientation.Horizontal, SmallChange = 1 };
    private readonly Canvas _overlay = new() { ClipToBounds = true };
    private readonly TextBox _editor = new() { BorderThickness = new Thickness(2), Padding = new Thickness(2, 0, 2, 0), Visibility = Visibility.Collapsed,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0x86, 0x3E)), Background = Brushes.White, Foreground = Brushes.Black, VerticalContentAlignment = VerticalAlignment.Center };

    private IXLWorksheet _ws;
    private int _firstRow = 1, _firstCol = 1;
    private (int Row, int Col) _active = (1, 1), _anchor = (1, 1);
    private bool _dragging, _editing, _picking;
    private int _resizeCol;
    private double _resizeStartX, _resizeStartW;
    private int _pickStart;

    /// <summary>選取範圍改變（公式列要更新）。</summary>
    public event Action? SelectionChanged;

    /// <summary>即將改動內容（外框先存一份復原用的快照）。</summary>
    public event Action? Changing;

    /// <summary>內容改動了。</summary>
    public event Action? Changed;

    public SheetGrid(IXLWorksheet ws)
    {
        _ws = ws;
        Focusable = true;
        FocusVisualStyle = null;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _canvas = new SheetCanvas(this);
        Children.Add(_canvas);
        Children.Add(_overlay);
        _overlay.Children.Add(_editor);
        SetColumn(_v, 1);
        Children.Add(_v);
        SetRow(_h, 1);
        Children.Add(_h);
        _v.ValueChanged += (_, _) => { _firstRow = Math.Max(1, (int)_v.Value + 1); Redraw(); };
        _h.ValueChanged += (_, _) => { _firstCol = Math.Max(1, (int)_h.Value + 1); Redraw(); };
        SizeChanged += (_, _) => UpdateScrollBars();

        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => { _dragging = false; _resizeCol = 0; _canvas.ReleaseMouseCapture(); };
        _canvas.MouseWheel += OnWheel;
        _editor.PreviewKeyDown += OnEditorKey;
        _editor.LostKeyboardFocus += (_, e) => { if (_editing && e.NewFocus is not null && !IsAncestorOf((DependencyObject)e.NewFocus)) CommitEdit(); };
        UpdateScrollBars();
    }

    // ═══════════════════════ 屬性 ═══════════════════════

    public IXLWorksheet Sheet
    {
        get => _ws;
        set
        {
            CancelEdit();
            _ws = value;
            _firstRow = _firstCol = 1;
            _v.Value = _h.Value = 0;
            Select(1, 1);
            UpdateScrollBars();
        }
    }

    private double _zoom = 1;
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.5, 3); UpdateScrollBars(); Redraw(); }
    }

    public (int Row, int Col) Active => _active;

    /// <summary>目前選取的範圍（左上、右下）。</summary>
    public (int R1, int C1, int R2, int C2) Selection =>
        (Math.Min(_active.Row, _anchor.Row), Math.Min(_active.Col, _anchor.Col), Math.Max(_active.Row, _anchor.Row), Math.Max(_active.Col, _anchor.Col));

    public IXLRange SelectedRange
    {
        get { var (r1, c1, r2, c2) = Selection; return _ws.Range(r1, c1, r2, c2); }
    }

    public IXLCell ActiveCell => _ws.Cell(_active.Row, _active.Col);

    public string SelectionAddress
    {
        get
        {
            var (r1, c1, r2, c2) = Selection;
            return r1 == r2 && c1 == c2 ? CellInput.Address(r1, c1) : $"{CellInput.Address(r1, c1)}:{CellInput.Address(r2, c2)}";
        }
    }

    // ═══════════════════════ 尺寸 ═══════════════════════

    /// <summary>欄寬：Excel 用「字元數」，約 7 像素一字再加 5。</summary>
    public double ColWidth(int c)
    {
        var col = _ws.Column(c);
        return col.IsHidden ? 0 : Math.Max(8, Math.Round(col.Width * 7 + 5));
    }

    /// <summary>列高：Excel 用「點」，換成像素。</summary>
    public double RowHeight(int r)
    {
        var row = _ws.Row(r);
        return row.IsHidden ? 0 : Math.Max(8, row.Height * 96 / 72);
    }

    private int LastRowUsed => _ws.LastRowUsed(XLCellsUsedOptions.All)?.RowNumber() ?? 1;
    private int LastColUsed => _ws.LastColumnUsed(XLCellsUsedOptions.All)?.ColumnNumber() ?? 1;

    private Size Logical => new(Math.Max(0, _canvas.ActualWidth / _zoom), Math.Max(0, _canvas.ActualHeight / _zoom));

    private int VisibleRows()
    {
        double y = HeaderH, h = Logical.Height;
        int n = 0;
        for (int r = _firstRow; y < h && r <= 1048576; r++, n++) y += RowHeight(r);
        return Math.Max(1, n - 1);
    }

    private int VisibleCols()
    {
        double x = HeaderW, w = Logical.Width;
        int n = 0;
        for (int c = _firstCol; x < w && c <= 16384; c++, n++) x += ColWidth(c);
        return Math.Max(1, n - 1);
    }

    private void UpdateScrollBars()
    {
        int rows = Math.Max(LastRowUsed, _active.Row) + 60, cols = Math.Max(LastColUsed, _active.Col) + 20;
        _v.Maximum = Math.Max(rows, 200);
        _h.Maximum = Math.Max(cols, 40);
        _v.ViewportSize = _v.LargeChange = VisibleRows();
        _h.ViewportSize = _h.LargeChange = VisibleCols();
        Redraw();
    }

    public void Redraw()
    {
        _canvas.InvalidateVisual();
        if (_editing) PlaceEditor();
    }

    // ═══════════════════════ 繪製 ═══════════════════════

    internal void Paint(DrawingContext dc)
    {
        var size = Logical;
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, size.Width, size.Height));

        // 看得到的欄、列
        var cols = new List<(int C, double X, double W)>();
        for (double x = HeaderW; x < size.Width; )
        {
            int c = _firstCol + cols.Count;
            if (c > 16384) break;
            double w = ColWidth(c);
            cols.Add((c, x, w));
            x += w;
        }
        var rows = new List<(int R, double Y, double H)>();
        for (double y = HeaderH; y < size.Height; )
        {
            int r = _firstRow + rows.Count;
            if (r > 1048576) break;
            double h = RowHeight(r);
            rows.Add((r, y, h));
            y += h;
        }
        if (cols.Count == 0 || rows.Count == 0) { dc.Pop(); return; }
        var colAt = cols.ToDictionary(c => c.C);
        var rowAt = rows.ToDictionary(r => r.R);
        int r1 = rows[0].R, r2 = rows[^1].R, c1 = cols[0].C, c2 = cols[^1].C;

        var used = _ws.Range(r1, c1, r2, c2).CellsUsed(XLCellsUsedOptions.All).ToList();
        var filled = new HashSet<(int, int)>();

        // 底色
        foreach (var cell in used)
        {
            var fill = cell.Style.Fill;
            if (fill.PatternType == XLFillPatternValues.None) continue;
            if (ToWpf(fill.BackgroundColor) is not { } fc) continue;
            var rc = CellRect(cell.Address.RowNumber, cell.Address.ColumnNumber, colAt, rowAt);
            if (rc is { } rr) dc.DrawRectangle(new SolidColorBrush(fc), null, rr);
        }

        // 格線
        if (_ws.ShowGridLines)
        {
            foreach (var (_, x, w) in cols) dc.DrawLine(GridPen, new Point(x + w - 0.5, HeaderH), new Point(x + w - 0.5, size.Height));
            foreach (var (_, y, h) in rows) dc.DrawLine(GridPen, new Point(HeaderW, y + h - 0.5), new Point(size.Width, y + h - 0.5));
        }

        // 合併儲存格：蓋掉中間的格線
        var mergedFirst = new Dictionary<(int, int), Rect>();
        var mergedHidden = new HashSet<(int, int)>();
        foreach (var mr in _ws.MergedRanges)
        {
            int mr1 = mr.RangeAddress.FirstAddress.RowNumber, mc1 = mr.RangeAddress.FirstAddress.ColumnNumber;
            int mr2 = mr.RangeAddress.LastAddress.RowNumber, mc2 = mr.RangeAddress.LastAddress.ColumnNumber;
            if (mr2 < r1 || mr1 > r2 || mc2 < c1 || mc1 > c2) continue;
            var rect = RangeRect(mr1, mc1, mr2, mc2, colAt, rowAt, cols, rows);
            var bg = ToWpf(_ws.Cell(mr1, mc1).Style.Fill.BackgroundColor) is { } mf && _ws.Cell(mr1, mc1).Style.Fill.PatternType != XLFillPatternValues.None
                ? new SolidColorBrush(mf) : Brushes.White;
            dc.DrawRectangle(bg, GridPen, rect);
            mergedFirst[(mr1, mc1)] = rect;
            for (int r = mr1; r <= mr2; r++) for (int c = mc1; c <= mc2; c++) if (r != mr1 || c != mc1) mergedHidden.Add((r, c));
        }

        // 框線
        foreach (var cell in used) DrawBorders(dc, cell, colAt, rowAt);

        // 文字
        var usedSet = used.Where(c => !c.Value.IsBlank || c.HasFormula).Select(c => (c.Address.RowNumber, c.Address.ColumnNumber)).ToHashSet();
        foreach (var cell in used)
        {
            int r = cell.Address.RowNumber, c = cell.Address.ColumnNumber;
            if (mergedHidden.Contains((r, c)) || !usedSet.Contains((r, c))) continue;
            if (!colAt.TryGetValue(c, out var col) || !rowAt.TryGetValue(r, out var row)) continue;
            string text = CellInput.Display(cell);
            if (text.Length == 0) continue;
            var rect = mergedFirst.TryGetValue((r, c), out var mrect) ? mrect : new Rect(col.X, row.Y, col.W, row.H);
            DrawText(dc, cell, text, rect, cols, usedSet, size.Width);
        }

        // 選取
        var (s1, t1, s2, t2) = Selection;
        var selRect = RangeRect(s1, t1, s2, t2, colAt, rowAt, cols, rows);
        if (!selRect.IsEmpty)
        {
            dc.DrawRectangle(SelFill, null, selRect);
            if (CellRect(_active.Row, _active.Col, colAt, rowAt) is { } ar) dc.DrawRectangle(Brushes.Transparent, null, ar);
            dc.DrawRectangle(null, SelPen, selRect);
        }

        // 欄名、列號（固定在上方與左側）
        dc.DrawRectangle(HeaderBg, null, new Rect(0, 0, size.Width, HeaderH));
        dc.DrawRectangle(HeaderBg, null, new Rect(0, 0, HeaderW, size.Height));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var (c, x, w) in cols)
        {
            if (c >= t1 && c <= t2) dc.DrawRectangle(HeaderSel, null, new Rect(x, 0, w, HeaderH));
            dc.DrawLine(HeaderPen, new Point(x + w - 0.5, 0), new Point(x + w - 0.5, HeaderH));
            var ft = Text(CellInput.ColumnName(c), 11.5, Brushes.DimGray, dpi);
            if (w > 6) dc.DrawText(ft, new Point(x + (w - ft.Width) / 2, (HeaderH - ft.Height) / 2));
        }
        foreach (var (r, y, h) in rows)
        {
            if (r >= s1 && r <= s2) dc.DrawRectangle(HeaderSel, null, new Rect(0, y, HeaderW, h));
            dc.DrawLine(HeaderPen, new Point(0, y + h - 0.5), new Point(HeaderW, y + h - 0.5));
            var ft = Text(r.ToString(CultureInfo.InvariantCulture), 11.5, Brushes.DimGray, dpi);
            if (h > 6) dc.DrawText(ft, new Point((HeaderW - ft.Width) / 2, y + (h - ft.Height) / 2));
        }
        dc.DrawLine(HeaderPen, new Point(0, HeaderH - 0.5), new Point(size.Width, HeaderH - 0.5));
        dc.DrawLine(HeaderPen, new Point(HeaderW - 0.5, 0), new Point(HeaderW - 0.5, size.Height));
        dc.DrawRectangle(HeaderBg, HeaderPen, new Rect(0, 0, HeaderW, HeaderH));
        dc.Pop();
    }

    private static Rect? CellRect(int r, int c, Dictionary<int, (int C, double X, double W)> colAt, Dictionary<int, (int R, double Y, double H)> rowAt) =>
        colAt.TryGetValue(c, out var col) && rowAt.TryGetValue(r, out var row) ? new Rect(col.X, row.Y, col.W, row.H) : null;

    /// <summary>範圍在畫面上的矩形（只算看得到的部分；完全看不到時回傳空矩形）。</summary>
    private static Rect RangeRect(int r1, int c1, int r2, int c2, Dictionary<int, (int C, double X, double W)> colAt, Dictionary<int, (int R, double Y, double H)> rowAt,
        List<(int C, double X, double W)> cols, List<(int R, double Y, double H)> rows)
    {
        int vc1 = Math.Max(c1, cols[0].C), vc2 = Math.Min(c2, cols[^1].C), vr1 = Math.Max(r1, rows[0].R), vr2 = Math.Min(r2, rows[^1].R);
        if (vc1 > vc2 || vr1 > vr2) return Rect.Empty;
        double x1 = colAt[vc1].X, x2 = colAt[vc2].X + colAt[vc2].W, y1 = rowAt[vr1].Y, y2 = rowAt[vr2].Y + rowAt[vr2].H;
        return new Rect(new Point(x1, y1), new Point(x2, y2));
    }

    private void DrawText(DrawingContext dc, IXLCell cell, string text, Rect rect, List<(int C, double X, double W)> cols, HashSet<(int, int)> usedSet, double viewW)
    {
        var font = cell.Style.Font;
        double px = Math.Max(6, font.FontSize * 96 / 72);
        var brush = ToWpf(font.FontColor) is { } fc ? new SolidColorBrush(fc) : Brushes.Black;
        var ft = new FormattedText(text.Replace('\n', ' '), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(string.IsNullOrWhiteSpace(font.FontName) ? "Microsoft JhengHei" : font.FontName),
                font.Italic ? FontStyles.Italic : FontStyles.Normal, font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            px, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (font.Underline != XLFontUnderlineValues.None) ft.SetTextDecorations(TextDecorations.Underline);
        if (font.Strikethrough) ft.SetTextDecorations(TextDecorations.Strikethrough);

        var h = cell.Style.Alignment.Horizontal;
        var v = cell.Value;
        bool numeric = v.IsNumber || v.IsDateTime || v.IsTimeSpan;
        bool right = h == XLAlignmentHorizontalValues.Right || (h == XLAlignmentHorizontalValues.General && numeric);
        bool center = h is XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous || (h == XLAlignmentHorizontalValues.General && (v.IsBoolean || v.IsError));

        // 數字放不下：像 Excel 一樣顯示 ###
        if (numeric && ft.Width > rect.Width - 4)
        {
            ft = new FormattedText(new string('#', Math.Max(1, (int)((rect.Width - 4) / (px * 0.6)))), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), px, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        // 靠左的文字可以延伸到右邊的空格子
        var clip = rect;
        if (!numeric && !right && !center && ft.Width > rect.Width - 4)
        {
            int r = cell.Address.RowNumber;
            double xEnd = rect.Right;
            foreach (var (c, x, w) in cols)
            {
                if (x < rect.Right - 0.5) continue;
                if (usedSet.Contains((r, c)) || xEnd - rect.Left >= ft.Width + 6) break;
                xEnd = x + w;
            }
            clip = new Rect(rect.Left, rect.Top, Math.Min(xEnd, viewW) - rect.Left, rect.Height);
        }

        double tx = right ? rect.Right - ft.Width - 4 : center ? rect.Left + (rect.Width - ft.Width) / 2 : rect.Left + 4;
        var va = cell.Style.Alignment.Vertical;
        double ty = va == XLAlignmentVerticalValues.Top ? rect.Top + 2
            : va == XLAlignmentVerticalValues.Center ? rect.Top + (rect.Height - ft.Height) / 2
            : rect.Bottom - ft.Height - 2;
        dc.PushClip(new RectangleGeometry(clip));
        dc.DrawText(ft, new Point(tx, ty));
        dc.Pop();
    }

    private static void DrawBorders(DrawingContext dc, IXLCell cell, Dictionary<int, (int C, double X, double W)> colAt, Dictionary<int, (int R, double Y, double H)> rowAt)
    {
        if (CellRect(cell.Address.RowNumber, cell.Address.ColumnNumber, colAt, rowAt) is not { } r) return;
        var b = cell.Style.Border;
        void Line(XLBorderStyleValues style, XLColor color, Point a, Point z)
        {
            if (style == XLBorderStyleValues.None) return;
            double t = style is XLBorderStyleValues.Thick or XLBorderStyleValues.Double ? 2.5 : style == XLBorderStyleValues.Medium ? 1.8 : 1;
            dc.DrawLine(new Pen(new SolidColorBrush(ToWpf(color) ?? Colors.Black), t), a, z);
        }
        Line(b.TopBorder, b.TopBorderColor, r.TopLeft, r.TopRight);
        Line(b.BottomBorder, b.BottomBorderColor, r.BottomLeft, r.BottomRight);
        Line(b.LeftBorder, b.LeftBorderColor, r.TopLeft, r.BottomLeft);
        Line(b.RightBorder, b.RightBorderColor, r.TopRight, r.BottomRight);
    }

    private static FormattedText Text(string s, double size, Brush brush, double dpi) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi);

    /// <summary>ClosedXML 的顏色換成 WPF 的；佈景主題色、透明、讀不出來的回傳 null。</summary>
    public static Color? ToWpf(XLColor? c)
    {
        if (c is null) return null;
        try
        {
            if (c.ColorType == XLColorType.Theme) return null;
            var d = c.Color;
            return d.A == 0 ? null : Color.FromArgb(d.A, d.R, d.G, d.B);
        }
        catch (Exception) { return null; }
    }

    public static XLColor FromWpf(Color c) => XLColor.FromArgb(c.A, c.R, c.G, c.B);

    // ═══════════════════════ 點選與捲動 ═══════════════════════

    private (int Row, int Col, bool ColHeader, bool RowHeader) HitTest(Point p)
    {
        p = new Point(p.X / _zoom, p.Y / _zoom);
        int col = _firstCol, row = _firstRow;
        if (p.X >= HeaderW)
            for (double x = HeaderW; ; col++) { double w = ColWidth(col); if (p.X < x + w || col >= 16384) break; x += w; }
        if (p.Y >= HeaderH)
            for (double y = HeaderH; ; row++) { double h = RowHeight(row); if (p.Y < y + h || row >= 1048576) break; y += h; }
        return (row, col, p.Y < HeaderH, p.X < HeaderW);
    }

    /// <summary>滑鼠在欄名的分隔線上（可以拖曳調整欄寬）：回傳那一欄，否則 0。</summary>
    private int ColumnEdgeAt(Point p)
    {
        p = new Point(p.X / _zoom, p.Y / _zoom);
        if (p.Y > HeaderH) return 0;
        double x = HeaderW;
        for (int c = _firstCol; x < Logical.Width; c++)
        {
            x += ColWidth(c);
            if (Math.Abs(p.X - x) <= 4) return c;
        }
        return 0;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        Focus();

        // 打公式時點格子：把位址填進公式
        if (_editing && IsPickingPoint())
        {
            var hit = HitTest(pos);
            if (!hit.ColHeader && !hit.RowHeader)
            {
                _picking = true;
                _pickStart = _editor.CaretIndex;
                _pickAnchor = (hit.Row, hit.Col);
                InsertReference(hit.Row, hit.Col, hit.Row, hit.Col);
                _canvas.CaptureMouse();
                e.Handled = true;
                return;
            }
        }
        if (_editing) CommitEdit();

        int edge = ColumnEdgeAt(pos);
        if (edge > 0)
        {
            if (e.ClickCount == 2) { AutoFit(edge); return; }
            _resizeCol = edge;
            _resizeStartX = pos.X / _zoom;
            _resizeStartW = ColWidth(edge);
            _canvas.CaptureMouse();
            return;
        }

        var (row, col, colHeader, rowHeader) = HitTest(pos);
        if (colHeader && rowHeader) { SelectAll(); return; }
        // 點欄名／列號：選整欄／整列（只延伸到資料或畫面所及，避免一次選一百萬格）
        if (colHeader) { _anchor = (Math.Max(LastRowUsed, VisibleRows() + _firstRow), col); _active = (1, col); Notify(); return; }
        if (rowHeader) { _anchor = (row, Math.Max(LastColUsed, VisibleCols() + _firstCol)); _active = (row, 1); Notify(); return; }

        if (e.ClickCount == 2) { Select(row, col); BeginEdit(null); return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _active = (row, col);
        else _active = _anchor = (row, col);
        _dragging = true;
        _canvas.CaptureMouse();
        Notify();
    }

    private (int Row, int Col) _pickAnchor;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        if (_resizeCol > 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            double w = Math.Max(8, _resizeStartW + pos.X / _zoom - _resizeStartX);
            _ws.Column(_resizeCol).Width = Math.Max(0.5, (w - 5) / 7);
            Redraw();
            Changed?.Invoke();
            return;
        }
        _canvas.Cursor = ColumnEdgeAt(pos) > 0 ? Cursors.SizeWE : Cursors.Arrow;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var hit = HitTest(pos);
        if (_picking)
        {
            InsertReference(Math.Min(_pickAnchor.Row, hit.Row), Math.Min(_pickAnchor.Col, hit.Col), Math.Max(_pickAnchor.Row, hit.Row), Math.Max(_pickAnchor.Col, hit.Col));
            return;
        }
        if (!_dragging || hit.ColHeader || hit.RowHeader) return;
        if (_active != (hit.Row, hit.Col)) { _active = (hit.Row, hit.Col); Notify(); }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Zoom += e.Delta > 0 ? 0.1 : -0.1; e.Handled = true; return; }
        int lines = e.Delta > 0 ? -3 : 3;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _h.Value = Math.Clamp(_h.Value + lines, 0, _h.Maximum);
        else _v.Value = Math.Clamp(_v.Value + lines, 0, _v.Maximum);
        e.Handled = true;
    }

    public void Select(int row, int col, bool extend = false)
    {
        row = Math.Clamp(row, 1, 1048576);
        col = Math.Clamp(col, 1, 16384);
        _active = (row, col);
        if (!extend) _anchor = _active;
        EnsureVisible(row, col);
        Notify();
    }

    public void SelectAll()
    {
        _anchor = (1, 1);
        _active = (Math.Max(LastRowUsed, 1), Math.Max(LastColUsed, 1));
        Notify();
    }

    private void Notify()
    {
        Redraw();
        SelectionChanged?.Invoke();
    }

    private void EnsureVisible(int row, int col)
    {
        if (row < _firstRow) _v.Value = row - 1;
        else if (row >= _firstRow + VisibleRows()) _v.Value = Math.Max(0, row - VisibleRows());
        if (col < _firstCol) _h.Value = col - 1;
        else if (col >= _firstCol + VisibleCols()) _h.Value = Math.Max(0, col - VisibleCols());
        if (_v.Value > _v.Maximum || _h.Value > _h.Maximum) UpdateScrollBars();
    }

    // ═══════════════════════ 鍵盤 ═══════════════════════

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_editing) { base.OnPreviewKeyDown(e); return; }
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var (r, c) = _active;
        switch (e.Key)
        {
            case Key.Up: Move(ctrl ? Jump(r, c, -1, 0) : (r - 1, c), shift); break;
            case Key.Down: Move(ctrl ? Jump(r, c, 1, 0) : (r + 1, c), shift); break;
            case Key.Left: Move(ctrl ? Jump(r, c, 0, -1) : (r, c - 1), shift); break;
            case Key.Right: Move(ctrl ? Jump(r, c, 0, 1) : (r, c + 1), shift); break;
            case Key.Enter: Move((r + (shift ? -1 : 1), c), false); break;
            case Key.Tab: Move((r, c + (shift ? -1 : 1)), false); break;
            case Key.PageDown: Move((r + VisibleRows(), c), shift); break;
            case Key.PageUp: Move((r - VisibleRows(), c), shift); break;
            case Key.Home: Move(ctrl ? (1, 1) : (r, 1), shift); break;
            case Key.End when ctrl: Move((LastRowUsed, LastColUsed), shift); break;
            case Key.F2: BeginEdit(null); break;
            case Key.Delete: ClearContents(); break;
            case Key.Back: BeginEdit(""); break;
            case Key.A when ctrl: SelectAll(); break;
            case Key.C when ctrl: Copy(cut: false); break;
            case Key.X when ctrl: Copy(cut: true); break;
            case Key.V when ctrl: Paste(); break;
            default: base.OnPreviewKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void Move((int Row, int Col) to, bool extend) => Select(to.Row, to.Col, extend);

    /// <summary>Ctrl+方向鍵：跳到資料區的邊緣（像 Excel）。</summary>
    private (int, int) Jump(int r, int c, int dr, int dc)
    {
        bool Filled(int rr, int cc) => rr >= 1 && cc >= 1 && rr <= 1048576 && cc <= 16384 && !_ws.Cell(rr, cc).IsEmpty();
        int limitR = dr > 0 ? LastRowUsed + 1 : 1, limitC = dc > 0 ? LastColUsed + 1 : 1;
        bool here = Filled(r, c), next = Filled(r + dr, c + dc);
        int nr = r, nc = c;
        if (here && next)
        {
            while (Filled(nr + dr, nc + dc)) { nr += dr; nc += dc; }
        }
        else
        {
            nr += dr; nc += dc;
            while (!Filled(nr, nc) && (dr == 0 || (dr > 0 ? nr < limitR : nr > 1)) && (dc == 0 || (dc > 0 ? nc < limitC : nc > 1))) { nr += dr; nc += dc; }
        }
        return (Math.Max(1, nr), Math.Max(1, nc));
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (!_editing && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0])) { BeginEdit(e.Text); e.Handled = true; return; }
        base.OnTextInput(e);
    }

    // ═══════════════════════ 編輯 ═══════════════════════

    public bool IsEditing => _editing;

    public void BeginEdit(string? initial)
    {
        _editing = true;
        var cell = ActiveCell;
        _editor.Text = initial ?? CellInput.Raw(cell);
        _editor.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(cell.Style.Font.FontName) ? "Microsoft JhengHei" : cell.Style.Font.FontName);
        _editor.Visibility = Visibility.Visible;
        PlaceEditor();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text.Length;
    }

    private void PlaceEditor()
    {
        double x = HeaderW, y = HeaderH;
        for (int c = _firstCol; c < _active.Col; c++) x += ColWidth(c);
        for (int r = _firstRow; r < _active.Row; r++) y += RowHeight(r);
        double w = Math.Max(ColWidth(_active.Col), 90), h = Math.Max(RowHeight(_active.Row), 22);
        Canvas.SetLeft(_editor, (x - 1) * _zoom);
        Canvas.SetTop(_editor, (y - 1) * _zoom);
        _editor.MinWidth = (w + 2) * _zoom;
        _editor.Height = (h + 2) * _zoom;
        _editor.FontSize = Math.Max(8, ActiveCell.Style.Font.FontSize * 96 / 72) * _zoom;
    }

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            int i = _editor.CaretIndex;
            _editor.Text = _editor.Text.Insert(i, "\n");
            _editor.CaretIndex = i + 1;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { CommitEdit(); Move((_active.Row + (shift ? -1 : 1), _active.Col), false); Focus(); e.Handled = true; }
        else if (e.Key == Key.Tab) { CommitEdit(); Move((_active.Row, _active.Col + (shift ? -1 : 1)), false); Focus(); e.Handled = true; }
        _picking = false;
    }

    /// <summary>游標停在公式的運算子後面（=、+、(、, …）時，點格子就是要把位址填進去。</summary>
    private bool IsPickingPoint()
    {
        string t = _editor.Text;
        if (!t.StartsWith('=')) return false;
        int i = _editor.CaretIndex;
        if (_picking) return true;
        return i > 0 && "=+-*/(,:<>&^;".Contains(t[i - 1]);
    }

    private void InsertReference(int r1, int c1, int r2, int c2)
    {
        string addr = r1 == r2 && c1 == c2 ? CellInput.Address(r1, c1) : $"{CellInput.Address(r1, c1)}:{CellInput.Address(r2, c2)}";
        string t = _editor.Text;
        int end = _editor.CaretIndex;
        string text = t[.._pickStart] + addr + t[end..];
        _editor.Text = text;
        _editor.CaretIndex = _pickStart + addr.Length;
        _editor.Focus();
    }

    public void CommitEdit()
    {
        if (!_editing) return;
        _editing = false;
        _picking = false;
        _editor.Visibility = Visibility.Collapsed;
        string text = _editor.Text;
        if (text == CellInput.Raw(ActiveCell)) { Focus(); Redraw(); return; }
        Edit(() => CellInput.Apply(ActiveCell, text));
        Focus();
    }

    public void CancelEdit()
    {
        if (!_editing) return;
        _editing = false;
        _picking = false;
        _editor.Visibility = Visibility.Collapsed;
        Focus();
        Redraw();
    }

    /// <summary>所有改動都走這裡：先讓外框存快照、改、重算公式、重畫。</summary>
    public void Edit(Action change)
    {
        Changing?.Invoke();
        try { change(); }
        catch (Exception e) { MessageBox.Show(Window.GetWindow(this), $"做不到：{e.Message}", "算籌"); }
        Recalculate();
        UpdateScrollBars();
        Changed?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void Recalculate()
    {
        foreach (var ws in _ws.Workbook.Worksheets)
        {
            try { ws.RecalculateAllFormulas(); }
            catch (Exception) { /* 有不支援的函數時，那一格會顯示錯誤，不影響其他格 */ }
        }
    }

    public void ClearContents() => Edit(() => SelectedRange.Clear(XLClearOptions.Contents));

    public void AutoFit(int col)
    {
        Edit(() =>
        {
            try { _ws.Column(col).AdjustToContents(); }
            catch (Exception)
            {
                // 量字型失敗（例如系統沒有那個字型）：用字數估
                int longest = _ws.Column(col).CellsUsed().Select(c => CellInput.Display(c)).Select(s => s.Sum(ch => ch >= 0x2E80 ? 2 : 1)).DefaultIfEmpty(8).Max();
                _ws.Column(col).Width = Math.Clamp(longest + 2, 4, 80);
            }
        });
    }

    // ═══════════════════════ 剪貼簿 ═══════════════════════

    private (IXLWorksheet Sheet, string Address, bool Cut, string Text)? _clip;

    public void Copy(bool cut)
    {
        var (r1, c1, r2, c2) = Selection;
        var sb = new StringBuilder();
        for (int r = r1; r <= r2; r++)
        {
            for (int c = c1; c <= c2; c++)
            {
                if (c > c1) sb.Append('\t');
                sb.Append(CellInput.Display(_ws.Cell(r, c)).Replace('\t', ' ').Replace("\r", "").Replace('\n', ' '));
            }
            sb.Append("\r\n");
        }
        string text = sb.ToString();
        try { Clipboard.SetText(text); } catch (Exception) { return; }
        _clip = (_ws, SelectedRange.RangeAddress.ToString()!, cut, text);
    }

    public void Paste()
    {
        string? text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (Exception) { text = null; }
        if (text is null) return;
        var dest = ActiveCell;

        // 從算籌自己複製來的：用 ClosedXML 搬（公式的相對位址會跟著調整、格式也一起來）
        if (_clip is { } clip && clip.Text == text && clip.Sheet.Workbook == _ws.Workbook)
        {
            Edit(() =>
            {
                var src = clip.Sheet.Range(clip.Address);
                src.CopyTo(dest);
                if (clip.Cut)
                {
                    int dr1 = dest.Address.RowNumber, dc1 = dest.Address.ColumnNumber;
                    int dr2 = dr1 + src.RowCount() - 1, dc2 = dc1 + src.ColumnCount() - 1;
                    foreach (var cell in src.Cells())
                    {
                        int r = cell.Address.RowNumber, c = cell.Address.ColumnNumber;
                        bool overlap = cell.Worksheet == dest.Worksheet && r >= dr1 && r <= dr2 && c >= dc1 && c <= dc2;
                        if (!overlap) cell.Clear();
                    }
                    _clip = null;
                }
            });
            return;
        }

        // 從別的程式（例如 Excel、網頁）複製來的：以 Tab 分欄、換行分列
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Edit(() =>
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var cells = lines[i].Split('\t');
                for (int j = 0; j < cells.Length; j++)
                    CellInput.Apply(_ws.Cell(dest.Address.RowNumber + i, dest.Address.ColumnNumber + j), cells[j]);
            }
        });
        var (r0, c0) = _active;
        _anchor = (r0, c0);
        _active = (r0 + lines.Length - 1, c0 + lines.Max(l => l.Split('\t').Length) - 1);
        Notify();
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FrozenPen(Color c, double t) { var p = new Pen(new SolidColorBrush(c), t); p.Freeze(); return p; }

    /// <summary>真正畫格子的元素：把繪製交回給 SheetGrid。</summary>
    private sealed class SheetCanvas(SheetGrid owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) => owner.Paint(dc);
    }
}
