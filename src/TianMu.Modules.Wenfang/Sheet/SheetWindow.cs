using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;

namespace TianMu.Modules.Wenfang.Sheet;

/// <summary>
/// 算籌（試算表）：像 Excel 的功能區、公式列、底部工作表分頁；
/// 讀寫 .xlsx（ClosedXML）與 .csv。
/// </summary>
internal sealed class SheetWindow : EditorWindow
{
    private const int MaxUndo = 30;

    private XLWorkbook _book;
    private readonly SheetGrid _grid;
    private readonly TextBox _nameBox = new() { Width = 90, Height = 24, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), ToolTip = "名稱方塊：輸入位址（例如 B12）按 Enter 跳過去" };
    private readonly TextBox _content = new() { Height = 24, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0), ToolTip = "儲存格內容；公式以 = 開頭" };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _summary = new() { FontSize = 12, Foreground = Ui.Secondary, Margin = new Thickness(0, 0, 12, 0) };
    private readonly List<byte[]> _undo = [], _redo = [];
    private readonly ComboBox _font, _size, _format;
    private readonly ToggleButton _bold, _italic, _underline, _wrap;
    private readonly TextBox _find = new() { Width = 120, Height = 24, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2), ToolTip = "要找的文字" };
    private bool _syncing;

    public SheetWindow() : base("籌", "算籌")
    {
        _book = NewBook();
        _grid = new SheetGrid(_book.Worksheet(1));
        _grid.SelectionChanged += SyncSelection;
        _grid.Changing += PushUndo;
        _grid.Changed += () => Dirty = true;

        // ── 公式列＋格線＋分頁 ──
        var body = new Grid { Background = Brushes.White };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF5, 0xF0)), Height = 32 };
        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { JumpTo(_nameBox.Text); e.Handled = true; } };
        _content.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { string t = _content.Text; _grid.Edit(() => CellInput.Apply(_grid.ActiveCell, t)); _grid.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { SyncSelection(); _grid.Focus(); e.Handled = true; }
        };
        DockPanel.SetDock(_nameBox, Dock.Left);
        bar.Children.Add(_nameBox);
        var fx = new TextBlock { Text = "fx", FontStyle = FontStyles.Italic, FontFamily = new FontFamily("Georgia"), FontSize = 14, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
        DockPanel.SetDock(fx, Dock.Left);
        bar.Children.Add(fx);
        bar.Children.Add(_content);
        body.Children.Add(new Border { Child = bar, BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xD8, 0xCE)), BorderThickness = new Thickness(0, 0, 0, 1) });

        Grid.SetRow(_grid, 1);
        body.Children.Add(_grid);

        var tabBar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEA, 0xE2)), Height = 30 };
        var add = new Button { Content = new TextBlock { Text = "＋", FontSize = 14, Foreground = Brushes.DimGray }, Width = 30, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "新增工作表", Cursor = Cursors.Hand };
        add.Click += (_, _) => AddSheet();
        DockPanel.SetDock(add, Dock.Left);
        tabBar.Children.Add(add);
        tabBar.Children.Add(new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(tabBar, 2);
        body.Children.Add(tabBar);
        SetBody(body);

        // ── 功能區 ──
        _font = Ui.Combo(130, Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCulture).Distinct(), editable: true);
        _font.SelectionChanged += (_, _) => { if (!_syncing && _font.SelectedItem is string f) ApplyStyle(s => s.Font.FontName = f); };
        _font.KeyDown += (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_font.Text)) { string f = _font.Text; ApplyStyle(s => s.Font.FontName = f); } };
        _size = Ui.Combo(54, ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"], editable: true);
        _size.SelectionChanged += (_, _) => { if (!_syncing && _size.SelectedItem is string s) SetSize(s); };
        _size.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetSize(_size.Text); };

        _bold = Ui.Toggle(new TextBlock { Text = "B", FontWeight = FontWeights.Bold, FontSize = 14 }, "粗體（Ctrl+B）", on => ApplyStyle(s => s.Font.Bold = on));
        _italic = Ui.Toggle(new TextBlock { Text = "I", FontStyle = FontStyles.Italic, FontSize = 14, FontFamily = new FontFamily("Georgia") }, "斜體（Ctrl+I）", on => ApplyStyle(s => s.Font.Italic = on));
        _underline = Ui.Toggle(new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline, FontSize = 14 }, "底線（Ctrl+U）",
            on => ApplyStyle(s => s.Font.Underline = on ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None));
        _wrap = Ui.Toggle(Ui.Glyph("", 14), "自動換行", on => ApplyStyle(s => s.Alignment.WrapText = on));

        var fontColor = PaletteButton("", "字型色彩", c => ApplyStyle(s => s.Font.FontColor = c is { } col ? SheetGrid.FromWpf(col) : XLColor.Black));
        var fill = PaletteButton("", "填滿色彩", c => ApplyStyle(s =>
        {
            if (c is { } col) { s.Fill.PatternType = XLFillPatternValues.Solid; s.Fill.BackgroundColor = SheetGrid.FromWpf(col); }
            else s.Fill.PatternType = XLFillPatternValues.None;
        }), "無填滿");

        var borders = Ui.Small("", "框線", () => { });
        borders.ContextMenu = Menu(
            ("所有框線", () => ApplyStyle(s => { s.Border.OutsideBorder = XLBorderStyleValues.Thin; s.Border.InsideBorder = XLBorderStyleValues.Thin; })),
            ("外框線", () => ApplyStyle(s => s.Border.OutsideBorder = XLBorderStyleValues.Thin)),
            ("粗外框線", () => ApplyStyle(s => s.Border.OutsideBorder = XLBorderStyleValues.Medium)),
            ("下框線", () => ApplyStyle(s => s.Border.BottomBorder = XLBorderStyleValues.Thin)),
            ("雙底框線", () => ApplyStyle(s => s.Border.BottomBorder = XLBorderStyleValues.Double)),
            ("無框線", () => ApplyStyle(s => { s.Border.OutsideBorder = XLBorderStyleValues.None; s.Border.InsideBorder = XLBorderStyleValues.None; })));
        OpenOnClick(borders);

        _format = Ui.Combo(110, CellInput.Formats.Select(f => f.Label));
        _format.SelectionChanged += (_, _) =>
        {
            if (_syncing || _format.SelectedIndex < 0) return;
            string fmt = CellInput.Formats[_format.SelectedIndex].Format;
            ApplyStyle(s => s.NumberFormat.Format = fmt);
        };

        var functions = Ui.Big("", "函數", () => { }, "插入常用函數");
        functions.ContextMenu = Menu(
            ("SUM　加總", () => StartFormula("SUM")), ("AVERAGE　平均", () => StartFormula("AVERAGE")),
            ("COUNT　計數", () => StartFormula("COUNT")), ("MAX　最大值", () => StartFormula("MAX")), ("MIN　最小值", () => StartFormula("MIN")),
            ("IF　如果", () => StartFormula("IF")), ("VLOOKUP　查表", () => StartFormula("VLOOKUP")), ("ROUND　四捨五入", () => StartFormula("ROUND")),
            ("TODAY　今天", () => { _grid.BeginEdit("=TODAY()"); }), ("CONCAT　串接文字", () => StartFormula("CONCAT")));
        OpenOnClick(functions);

        _find.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } };

        AddTab("常用",
            Ui.Group("剪貼簿", Ui.Row(Ui.Big("", "貼上", () => _grid.Paste(), "貼上（Ctrl+V）"),
                VStack(Ui.Small("", "剪下（Ctrl+X）", () => _grid.Copy(cut: true)), Ui.Small("", "複製（Ctrl+C）", () => _grid.Copy(cut: false))))),
            Ui.Group("字型",
                Ui.Row(_font, _size, Ui.Small("A+", "放大字型", () => Grow(1), text: true), Ui.Small("A-", "縮小字型", () => Grow(-1), text: true)),
                Ui.Row(_bold, _italic, _underline, borders, fill, fontColor)),
            Ui.Group("對齊方式",
                Ui.Row(Ui.Small("", "靠左對齊", () => ApplyStyle(s => s.Alignment.Horizontal = XLAlignmentHorizontalValues.Left)),
                    Ui.Small("", "置中", () => ApplyStyle(s => s.Alignment.Horizontal = XLAlignmentHorizontalValues.Center)),
                    Ui.Small("", "靠右對齊", () => ApplyStyle(s => s.Alignment.Horizontal = XLAlignmentHorizontalValues.Right)), _wrap),
                Ui.Row(Ui.Small("頂", "靠上", () => ApplyStyle(s => s.Alignment.Vertical = XLAlignmentVerticalValues.Top), text: true),
                    Ui.Small("中", "垂直置中", () => ApplyStyle(s => s.Alignment.Vertical = XLAlignmentVerticalValues.Center), text: true),
                    Ui.Small("底", "靠下", () => ApplyStyle(s => s.Alignment.Vertical = XLAlignmentVerticalValues.Bottom), text: true),
                    TextButton("跨欄置中", MergeCenter))),
            Ui.Group("數值",
                Ui.Row(_format),
                Ui.Row(Ui.Small("$", "貨幣", () => ApplyStyle(s => s.NumberFormat.Format = "\"NT$\"#,##0"), text: true),
                    Ui.Small("%", "百分比", () => ApplyStyle(s => s.NumberFormat.Format = "0%"), text: true),
                    Ui.Small(",", "千分位", () => ApplyStyle(s => s.NumberFormat.Format = "#,##0"), text: true),
                    Ui.Small(".0+", "增加小數位數", () => Decimals(1), text: true), Ui.Small(".0-", "減少小數位數", () => Decimals(-1), text: true))),
            Ui.Group("儲存格",
                Ui.Row(VStack(TextButton("插入列", () => InsertRows()), TextButton("刪除列", DeleteRows)),
                    VStack(TextButton("插入欄", () => InsertColumns()), TextButton("刪除欄", DeleteColumns)),
                    VStack(TextButton("最適欄寬", AutoFitSelection), TextButton("清除", ClearAll)))),
            Ui.Group("編輯",
                Ui.Row(Ui.Big("", "自動加總", AutoSum, "自動加總（Alt+=）"),
                    Ui.Big("", "排序", () => { }, "排序") is var sort ? WithSortMenu(sort) : null!,
                    VStack(_find, TextButton("找下一個", FindNext)))));

        AddTab("插入",
            Ui.Group("函數", Ui.Row(functions, Ui.Big("", "加總", AutoSum))),
            Ui.Group("日期時間", Ui.Row(
                Ui.Big("", "今天", () => _grid.Edit(() => { _grid.ActiveCell.Value = DateTime.Today; _grid.ActiveCell.Style.NumberFormat.Format = "yyyy/m/d"; }), "填入今天日期（Ctrl+;）"),
                Ui.Big("", "現在", () => _grid.Edit(() => { _grid.ActiveCell.Value = DateTime.Now; _grid.ActiveCell.Style.NumberFormat.Format = "yyyy/m/d h:mm"; }), "填入現在時間"))));

        AddTab("資料",
            Ui.Group("排序", Ui.Row(Ui.Big("", "從小到大", () => Sort(XLSortOrder.Ascending)), Ui.Big("", "從大到小", () => Sort(XLSortOrder.Descending)))),
            Ui.Group("計算", Ui.Row(Ui.Big("", "重新計算", () => { _grid.Recalculate(); _grid.Redraw(); SyncSelection(); }, "重新計算所有公式（F9）"))));

        AddTab("檢視",
            Ui.Group("縮放", Ui.Row(Ui.Big("", "縮小", () => SetZoom(_grid.Zoom - 0.1)), Ui.Big("", "放大", () => SetZoom(_grid.Zoom + 0.1)),
                Ui.Big("", "100%", () => SetZoom(1)))),
            Ui.Group("顯示", Ui.Row(Checked(Ui.Toggle(new TextBlock { Text = "格線", FontSize = 12 }, "顯示格線", on => { _grid.Sheet.ShowGridLines = on; _grid.Redraw(); })))));

        AddTab("檔案", FileGroup());

        StatusRight.Children.Add(_summary);
        RebuildTabs();
        SyncSelection();
        Dirty = false;
        Loaded += (_, _) => _grid.Focus();
    }

    // ═══════════════════════ 檔案 ═══════════════════════

    protected override string OpenFilter => "試算表|*.xlsx;*.xlsm;*.csv|Excel 活頁簿|*.xlsx;*.xlsm|CSV（逗號分隔）|*.csv";
    protected override string SaveFilter => "Excel 活頁簿|*.xlsx|CSV UTF-8（逗號分隔，只存目前工作表）|*.csv";
    protected override string DefaultExtension => ".xlsx";
    protected override bool IsWritableFormat(string path) => Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);

    private static XLWorkbook NewBook()
    {
        var book = new XLWorkbook();
        book.Worksheets.Add("工作表1");
        book.Style.Font.FontName = "Microsoft JhengHei";
        book.Style.Font.FontSize = 12;
        return book;
    }

    public override void Load(string path)
    {
        XLWorkbook book;
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            book = NewBook();
            book.Worksheet(1).Name = SafeSheetName(Path.GetFileNameWithoutExtension(path));
            FillFromCsv(book.Worksheet(1), ReadText(path));
        }
        else
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            book = new XLWorkbook(stream);
        }
        SetBook(book, 0);
        _undo.Clear();
        _redo.Clear();
        _grid.Recalculate();
        _grid.Redraw();
    }

    internal static void FillFromCsv(IXLWorksheet ws, string text)
    {
        var rows = Csv.Parse(text, Csv.DetectSeparator(text));
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Count; c++)
                if (rows[r][c].Length > 0) CellInput.Apply(ws.Cell(r + 1, c + 1), rows[r][c].StartsWith('=') ? "'" + rows[r][c] : rows[r][c]);
    }

    internal static string ToCsv(IXLWorksheet ws)
    {
        var used = ws.RangeUsed(XLCellsUsedOptions.Contents);
        if (used is null) return "";
        int r2 = used.RangeAddress.LastAddress.RowNumber, c2 = used.RangeAddress.LastAddress.ColumnNumber;
        var rows = new List<List<string>>();
        for (int r = 1; r <= r2; r++)
        {
            var row = new List<string>();
            for (int c = 1; c <= c2; c++) row.Add(CellInput.Display(ws.Cell(r, c)));
            rows.Add(row);
        }
        return Csv.Format(rows);
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(950).GetString(bytes);
        }
    }

    protected override void Write(string path)
    {
        _grid.CommitEdit();
        string tmp = path + ".tmp";
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(tmp, ToCsv(_grid.Sheet), new UTF8Encoding(true));
        else
            _book.SaveAs(tmp);
        File.Move(tmp, path, overwrite: true);
    }

    protected override void NewDocument() => WenfangHub.New(WenfangHub.Kind.Sheet);

    protected override void Print()
    {
        _grid.CommitEdit();
        var ws = _grid.Sheet;
        var used = ws.RangeUsed(XLCellsUsedOptions.Contents);
        if (used is null) { MessageBox.Show(this, "這張工作表是空的。", "算籌"); return; }
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;

        int r2 = used.RangeAddress.LastAddress.RowNumber, c2 = used.RangeAddress.LastAddress.ColumnNumber;
        var doc = new FlowDocument { FontFamily = new FontFamily("Microsoft JhengHei"), FontSize = 10, PagePadding = new Thickness(48), ColumnWidth = double.MaxValue };
        doc.PageWidth = dlg.PrintableAreaWidth;
        doc.PageHeight = dlg.PrintableAreaHeight;
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        for (int c = 1; c <= c2; c++) table.Columns.Add(new TableColumn { Width = new GridLength(_grid.ColWidth(c)) });
        var group = new TableRowGroup();
        for (int r = 1; r <= r2; r++)
        {
            var row = new TableRow();
            for (int c = 1; c <= c2; c++)
            {
                var cell = ws.Cell(r, c);
                var p = new Paragraph(new Run(CellInput.Display(cell))) { Margin = new Thickness(2) };
                if (cell.Style.Font.Bold) p.FontWeight = FontWeights.Bold;
                if (cell.Value.IsNumber) p.TextAlignment = TextAlignment.Right;
                row.Cells.Add(new TableCell(p) { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5) });
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        doc.Blocks.Add(table);
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"算籌 — {ws.Name}");
    }

    // ═══════════════════════ 復原 ═══════════════════════

    private byte[] Snapshot()
    {
        using var ms = new MemoryStream();
        _book.SaveAs(ms);
        return ms.ToArray();
    }

    private void PushUndo()
    {
        try
        {
            _undo.Add(Snapshot());
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
        }
        catch (Exception) { /* 存不了快照就只是不能復原這一步 */ }
    }

    private void Restore(List<byte[]> from, List<byte[]> to)
    {
        _grid.CommitEdit();
        if (from.Count == 0) return;
        int sheet = _grid.Sheet.Position - 1;
        var (row, col) = _grid.Active;
        try { to.Add(Snapshot()); } catch (Exception) { }
        var data = from[^1];
        from.RemoveAt(from.Count - 1);
        SetBook(new XLWorkbook(new MemoryStream(data)), sheet);
        _grid.Select(row, col);
        Dirty = true;
    }

    protected override void Undo() => Restore(_undo, _redo);
    protected override void Redo() => Restore(_redo, _undo);

    private void SetBook(XLWorkbook book, int sheetIndex)
    {
        var old = _book;
        _book = book;
        _grid.Sheet = book.Worksheet(Math.Clamp(sheetIndex, 0, book.Worksheets.Count - 1) + 1);
        if (!ReferenceEquals(old, book)) old.Dispose();
        RebuildTabs();
        SyncSelection();
    }

    // ═══════════════════════ 工作表分頁 ═══════════════════════

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var ws in _book.Worksheets.OrderBy(w => w.Position))
        {
            bool active = ws == _grid.Sheet;
            var tab = new Button
            {
                Content = new TextBlock { Text = ws.Name, FontSize = 12.5, FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal, Foreground = active ? new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x1E)) : Brushes.DimGray },
                Padding = new Thickness(14, 3, 14, 3), Cursor = Cursors.Hand, BorderThickness = new Thickness(0, active ? 0 : 0, 1, active ? 3 : 0),
                Background = active ? Brushes.White : Brushes.Transparent, BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x69)) : new SolidColorBrush(Color.FromRgb(0xD6, 0xD0, 0xC4)),
                ToolTip = "按兩下改名；右鍵有更多選項",
            };
            var sheet = ws;
            tab.Click += (_, _) => SwitchTo(sheet);
            tab.MouseDoubleClick += (_, _) => RenameSheet(sheet);
            tab.ContextMenu = Menu(("重新命名", () => RenameSheet(sheet)), ("刪除", () => DeleteSheet(sheet)),
                ("左移", () => MoveSheet(sheet, -1)), ("右移", () => MoveSheet(sheet, 1)));
            _tabs.Children.Add(tab);
        }
    }

    private void SwitchTo(IXLWorksheet ws)
    {
        _grid.CommitEdit();
        if (ws == _grid.Sheet) return;
        _grid.Sheet = ws;
        RebuildTabs();
        SyncSelection();
    }

    private void AddSheet()
    {
        int n = _book.Worksheets.Count + 1;
        while (_book.Worksheets.Contains($"工作表{n}")) n++;
        PushUndo();
        var ws = _book.Worksheets.Add($"工作表{n}");
        Dirty = true;
        SwitchTo(ws);
    }

    private void RenameSheet(IXLWorksheet ws)
    {
        string? name = Prompt("重新命名工作表", ws.Name);
        if (string.IsNullOrWhiteSpace(name) || name == ws.Name) return;
        name = SafeSheetName(name);
        if (_book.Worksheets.Contains(name)) { MessageBox.Show(this, $"已經有叫「{name}」的工作表了。", "算籌"); return; }
        PushUndo();
        ws.Name = name;
        Dirty = true;
        RebuildTabs();
    }

    private void DeleteSheet(IXLWorksheet ws)
    {
        if (_book.Worksheets.Count <= 1) { MessageBox.Show(this, "活頁簿至少要留一張工作表。", "算籌"); return; }
        if (MessageBox.Show(this, $"確定刪除「{ws.Name}」？（可以按復原救回來）", "算籌", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        PushUndo();
        int pos = ws.Position;
        bool wasActive = ws == _grid.Sheet;
        ws.Delete();
        if (wasActive) _grid.Sheet = _book.Worksheet(Math.Min(pos, _book.Worksheets.Count));
        Dirty = true;
        RebuildTabs();
        SyncSelection();
    }

    private void MoveSheet(IXLWorksheet ws, int delta)
    {
        int to = ws.Position + delta;
        if (to < 1 || to > _book.Worksheets.Count) return;
        PushUndo();
        ws.Position = to;
        Dirty = true;
        RebuildTabs();
    }

    /// <summary>Excel 工作表名稱：最多 31 字，不能有 : \ / ? * [ ]。</summary>
    internal static string SafeSheetName(string name)
    {
        var s = new string(name.Where(ch => ":\\/?*[]".IndexOf(ch) < 0).ToArray()).Trim('\'').Trim();
        if (s.Length == 0) s = "工作表1";
        return s.Length > 31 ? s[..31] : s;
    }

    // ═══════════════════════ 格式 ═══════════════════════

    private void ApplyStyle(Action<IXLStyle> apply)
    {
        if (_syncing) return;
        _grid.CommitEdit();
        _grid.Edit(() => apply(_grid.SelectedRange.Style));
        _grid.Focus();
    }

    private void SetSize(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt) && pt is >= 1 and <= 409)
            ApplyStyle(s => s.Font.FontSize = pt);
    }

    private void Grow(int dir)
    {
        double[] steps = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];
        double cur = _grid.ActiveCell.Style.Font.FontSize;
        double next = dir > 0 ? steps.FirstOrDefault(s => s > cur, cur + 4) : steps.LastOrDefault(s => s < cur, Math.Max(1, cur - 1));
        ApplyStyle(s => s.Font.FontSize = next);
    }

    private void Decimals(int delta)
    {
        string fmt = CellInput.AdjustDecimals(_grid.ActiveCell.Style.NumberFormat.Format, delta);
        ApplyStyle(s => s.NumberFormat.Format = fmt);
    }

    private void MergeCenter()
    {
        var range = _grid.SelectedRange;
        if (range.IsMerged()) { _grid.Edit(() => range.Unmerge()); return; }
        if (range.RowCount() == 1 && range.ColumnCount() == 1) return;
        int filled = range.CellsUsed(XLCellsUsedOptions.Contents).Count();
        if (filled > 1 && MessageBox.Show(this, "合併後只會保留左上角的值，其他會被清掉。要繼續嗎？", "算籌", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _grid.Edit(() =>
        {
            range.Merge();
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        });
    }

    // ═══════════════════════ 列與欄 ═══════════════════════

    private void InsertRows()
    {
        var (r1, _, r2, _) = _grid.Selection;
        _grid.Edit(() => _grid.Sheet.Row(r1).InsertRowsAbove(r2 - r1 + 1));
    }

    private void DeleteRows()
    {
        var (r1, _, r2, _) = _grid.Selection;
        _grid.Edit(() => _grid.Sheet.Rows(r1, r2).Delete());
    }

    private void InsertColumns()
    {
        var (_, c1, _, c2) = _grid.Selection;
        _grid.Edit(() => _grid.Sheet.Column(c1).InsertColumnsBefore(c2 - c1 + 1));
    }

    private void DeleteColumns()
    {
        var (_, c1, _, c2) = _grid.Selection;
        _grid.Edit(() => _grid.Sheet.Columns(c1, c2).Delete());
    }

    private void AutoFitSelection()
    {
        var (_, c1, _, c2) = _grid.Selection;
        for (int c = c1; c <= c2; c++) _grid.AutoFit(c);
    }

    private void ClearAll() => _grid.Edit(() => _grid.SelectedRange.Clear(XLClearOptions.All));

    // ═══════════════════════ 公式與資料 ═══════════════════════

    private void StartFormula(string fn)
    {
        _grid.CommitEdit();
        _grid.BeginEdit($"={fn}(");
    }

    /// <summary>自動加總：往上找連續的數字，沒有就往左找（和 Excel 一樣）。</summary>
    private void AutoSum()
    {
        _grid.CommitEdit();
        var (r, c) = _grid.Active;
        var ws = _grid.Sheet;
        string? range = SumRange(ws, r, c);
        _grid.BeginEdit(range is null ? "=SUM(" : $"=SUM({range})");
    }

    internal static string? SumRange(IXLWorksheet ws, int r, int c)
    {
        bool Num(int rr, int cc) => rr >= 1 && cc >= 1 && ws.Cell(rr, cc) is var x && (x.Value.IsNumber || (x.HasFormula && x.CachedValue.IsNumber));
        int top = r - 1;
        while (Num(top, c)) top--;
        if (top < r - 1) return $"{CellInput.Address(top + 1, c)}:{CellInput.Address(r - 1, c)}";
        int left = c - 1;
        while (Num(r, left)) left--;
        if (left < c - 1) return $"{CellInput.Address(r, left + 1)}:{CellInput.Address(r, c - 1)}";
        return null;
    }

    private Button WithSortMenu(Button b)
    {
        b.ContextMenu = Menu(("從小到大（A→Z）", () => Sort(XLSortOrder.Ascending)), ("從大到小（Z→A）", () => Sort(XLSortOrder.Descending)));
        OpenOnClick(b);
        return b;
    }

    /// <summary>依目前這一欄排序：選了範圍就排範圍，否則排游標所在的整塊資料；第一列像標題就不動它。</summary>
    private void Sort(XLSortOrder order)
    {
        _grid.CommitEdit();
        var (r1, c1, r2, c2) = _grid.Selection;
        IXLRange range = r1 == r2 && c1 == c2 ? _grid.ActiveCell.CurrentRegion : _grid.SelectedRange;
        if (range.RowCount() < 2) return;
        if (LooksLikeHeader(range)) range = range.Range(2, 1, range.RowCount(), range.ColumnCount());
        int col = _grid.Active.Col - range.RangeAddress.FirstAddress.ColumnNumber + 1;
        col = Math.Clamp(col, 1, range.ColumnCount());
        _grid.Edit(() => range.Sort(col, order, false, true));
    }

    private static bool LooksLikeHeader(IXLRange range)
    {
        if (range.RowCount() < 2) return false;
        var first = range.Row(1).Cells().ToList();
        var second = range.Row(2).Cells().ToList();
        bool allText = first.All(c => c.Value.IsText || c.Value.IsBlank) && first.Any(c => c.Value.IsText);
        bool secondHasNonText = second.Any(c => c.Value.IsNumber || c.Value.IsDateTime || c.HasFormula);
        bool bold = first.Any(c => c.Style.Font.Bold);
        return allText && (secondHasNonText || bold);
    }

    private void FindNext()
    {
        string q = _find.Text.Trim();
        if (q.Length == 0) { _find.Focus(); return; }
        var ws = _grid.Sheet;
        var cells = ws.CellsUsed(XLCellsUsedOptions.Contents).OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber).ToList();
        var (ar, ac) = _grid.Active;
        bool Match(IXLCell c) => CellInput.Display(c).Contains(q, StringComparison.CurrentCultureIgnoreCase) || (c.HasFormula && c.FormulaA1.Contains(q, StringComparison.OrdinalIgnoreCase));
        var hit = cells.FirstOrDefault(c => (c.Address.RowNumber > ar || (c.Address.RowNumber == ar && c.Address.ColumnNumber > ac)) && Match(c)) ?? cells.FirstOrDefault(Match);
        if (hit is null) { Status.Text = $"找不到「{q}」"; return; }
        _grid.Select(hit.Address.RowNumber, hit.Address.ColumnNumber);
        Status.Text = $"找到：{hit.Address}";
    }

    private void JumpTo(string text)
    {
        var parts = text.Trim().Split(':');
        if (CellInput.ParseAddress(parts[0]) is not { } a) { Status.Text = "看不懂這個位址"; return; }
        _grid.Select(a.Row, a.Col);
        if (parts.Length > 1 && CellInput.ParseAddress(parts[1]) is { } b) _grid.Select(b.Row, b.Col, extend: true);
        _grid.Focus();
    }

    // ═══════════════════════ 同步 ═══════════════════════

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var cell = _grid.ActiveCell;
            _nameBox.Text = _grid.SelectionAddress;
            if (!_content.IsKeyboardFocused) _content.Text = CellInput.Raw(cell);
            var font = cell.Style.Font;
            _font.Text = font.FontName;
            _size.Text = font.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
            _bold.IsChecked = font.Bold;
            _italic.IsChecked = font.Italic;
            _underline.IsChecked = font.Underline != XLFontUnderlineValues.None;
            _wrap.IsChecked = cell.Style.Alignment.WrapText;
            string fmt = cell.Style.NumberFormat.Format;
            int idx = Array.FindIndex(CellInput.Formats, f => f.Format == (string.IsNullOrEmpty(fmt) ? "General" : fmt));
            _format.SelectedIndex = idx;
            UpdateSummary();
        }
        finally { _syncing = false; }
    }

    /// <summary>狀態列：選取範圍的平均、個數、加總（和 Excel 右下角一樣）。</summary>
    private void UpdateSummary()
    {
        var (r1, c1, r2, c2) = _grid.Selection;
        if (r1 == r2 && c1 == c2) { _summary.Text = ""; return; }
        var used = _grid.SelectedRange.CellsUsed(XLCellsUsedOptions.Contents).ToList();
        var nums = new List<double>();
        foreach (var c in used)
        {
            var v = c.HasFormula ? c.CachedValue : c.Value;
            if (v.IsNumber) nums.Add(v.GetNumber());
        }
        int count = used.Count(c => !c.Value.IsBlank || c.HasFormula);
        _summary.Text = nums.Count == 0 ? $"項目個數：{count}"
            : $"平均值：{nums.Average():#,##0.##}　項目個數：{count}　加總：{nums.Sum():#,##0.##}";
    }

    private void SetZoom(double z)
    {
        _grid.Zoom = z;
        Status.Text = $"縮放 {_grid.Zoom:P0}";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool gridHasFocus = _grid.IsKeyboardFocusWithin && !_grid.IsEditing;
        if (gridHasFocus && ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(); e.Handled = true; return;
                case Key.Y: Redo(); e.Handled = true; return;
                case Key.B: ApplyStyle(s => s.Font.Bold = !_grid.ActiveCell.Style.Font.Bold); SyncSelection(); e.Handled = true; return;
                case Key.I: ApplyStyle(s => s.Font.Italic = !_grid.ActiveCell.Style.Font.Italic); SyncSelection(); e.Handled = true; return;
                case Key.U:
                    bool on = _grid.ActiveCell.Style.Font.Underline == XLFontUnderlineValues.None;
                    ApplyStyle(s => s.Font.Underline = on ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None); SyncSelection(); e.Handled = true; return;
                case Key.OemSemicolon: _grid.Edit(() => { _grid.ActiveCell.Value = DateTime.Today; _grid.ActiveCell.Style.NumberFormat.Format = "yyyy/m/d"; }); e.Handled = true; return;
            }
        }
        if (gridHasFocus && e.Key == Key.F9) { _grid.Recalculate(); _grid.Redraw(); e.Handled = true; return; }
        if (gridHasFocus && Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.OemPlus) { AutoSum(); e.Handled = true; return; }
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
        var b = new Button { Content = new TextBlock { Text = text, FontSize = 12 }, Padding = new Thickness(8, 3, 8, 3), Style = (System.Windows.Style)Ui.Styles["WfButton"] };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button PaletteButton(string glyph, string tip, Action<Color?> pick, string clear = "自動")
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

    private static ToggleButton Checked(ToggleButton t) { t.IsChecked = true; t.Width = double.NaN; t.Padding = new Thickness(8, 0, 8, 0); return t; }

    private string? Prompt(string title, string initial)
    {
        var box = new TextBox { Text = initial, FontSize = 14, Margin = new Thickness(0, 0, 0, 12), MinWidth = 240 };
        var ok = new Button { Content = "確定", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var w = new Window { Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
