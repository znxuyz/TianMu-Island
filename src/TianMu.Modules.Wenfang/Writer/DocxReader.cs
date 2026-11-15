using System.IO;
using System.Windows;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Wpf = System.Windows.Documents;

namespace TianMu.Modules.Wenfang.Writer;

/// <summary>
/// 把 Word 的 .docx 讀成墨書的文件：段落與標題、文字格式、項目符號與編號、表格、圖片、紙張與邊界。
/// Word 有、墨書沒有的東西（頁首頁尾、註腳、文字方塊、追蹤修訂……）會略過，但文字盡量保留。
/// </summary>
public static class DocxReader
{
    public static (Wpf.FlowDocument Doc, PageSetup Setup) Read(string path)
    {
        using var package = WordprocessingDocument.Open(path, false);
        var main = package.MainDocumentPart ?? throw new InvalidDataException("這不是有效的 Word 文件");
        var body = main.Document?.Body ?? throw new InvalidDataException("這不是有效的 Word 文件");
        var ctx = new Ctx(main);

        var doc = new Wpf.FlowDocument
        {
            FontFamily = new FontFamily(ctx.DefaultFont ?? WriterWindow.DefaultFont),
            FontSize = WriterWindow.PtToPx(ctx.DefaultSizePt ?? WriterWindow.DefaultSizePt),
            Foreground = Brushes.Black,
        };
        var setup = Setup(body.Elements<W.SectionProperties>().LastOrDefault() ?? body.Descendants<W.SectionProperties>().LastOrDefault());
        ctx.ContentWidth = setup.Width - 2 * setup.Margin;

        ReadBlocks(body.ChildElements, doc.Blocks, ctx);
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Wpf.Paragraph());
        return (doc, setup);
    }

    private sealed class Ctx
    {
        public Ctx(MainDocumentPart main)
        {
            Main = main;
            var styles = main.StyleDefinitionsPart?.Styles;
            if (styles is not null)
            {
                foreach (var s in styles.Elements<W.Style>())
                    if (s.StyleId?.Value is { } id) StyleNames[id] = s.StyleName?.Val?.Value?.ToLowerInvariant() ?? id.ToLowerInvariant();
                var rpr = styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
                if (rpr?.FontSize?.Val?.Value is { } sz && double.TryParse(sz, out double half)) DefaultSizePt = half / 2;
                DefaultFont = rpr?.RunFonts?.EastAsia?.Value ?? rpr?.RunFonts?.Ascii?.Value;
            }
        }

        public MainDocumentPart Main { get; }
        public Dictionary<string, string> StyleNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double? DefaultSizePt { get; }
        public string? DefaultFont { get; }
        public double ContentWidth { get; set; } = 600;
        public bool PendingPageBreak { get; set; }
    }

    // ═══════════════════════ 紙張 ═══════════════════════

    private static PageSetup Setup(W.SectionProperties? sect)
    {
        var size = sect?.GetFirstChild<W.PageSize>();
        var margin = sect?.GetFirstChild<W.PageMargin>();
        double w = size?.Width?.Value is { } sw ? PageSetup.FromTwips(sw) : PageSetup.A4.Width;
        double h = size?.Height?.Value is { } sh ? PageSetup.FromTwips(sh) : PageSetup.A4.Height;
        double m = margin?.Left?.Value is { } ml ? PageSetup.FromTwips(ml) : PageSetup.A4.Margin;
        return new PageSetup(w, h, Math.Clamp(m, 0, Math.Min(w, h) / 3));
    }

    // ═══════════════════════ 區塊 ═══════════════════════

    private static void ReadBlocks(IEnumerable<OpenXmlElement> elements, Wpf.BlockCollection target, Ctx ctx)
    {
        // 連續的編號段落要收進同一個清單；巢狀層級用堆疊記住
        var lists = new List<(Wpf.List List, int NumId)>();
        foreach (var el in elements)
        {
            switch (el)
            {
                case W.Paragraph wp:
                {
                    var numbering = Numbering(wp, ctx);
                    var para = Paragraph(wp, ctx);
                    if (numbering is { } n)
                    {
                        AddToList(target, lists, para, n.NumId, n.Level, n.Bullet);
                    }
                    else
                    {
                        lists.Clear();
                        target.Add(para);
                    }
                    break;
                }
                case W.Table wt:
                    lists.Clear();
                    target.Add(Table(wt, ctx));
                    break;
                case W.SdtBlock sdt when sdt.SdtContentBlock is { } content:
                    ReadBlocks(content.ChildElements, target, ctx);
                    break;
            }
        }
    }

    private static void AddToList(Wpf.BlockCollection target, List<(Wpf.List List, int NumId)> lists, Wpf.Paragraph para, int numId, int level, bool bullet)
    {
        level = Math.Clamp(level, 0, 8);
        // 回到較淺的層級：把較深的清單收掉
        while (lists.Count > level + 1) lists.RemoveAt(lists.Count - 1);
        if (lists.Count == level + 1 && lists[level].NumId != numId && level == 0) lists.Clear();

        while (lists.Count <= level)
        {
            var list = new Wpf.List
            {
                MarkerStyle = bullet ? (lists.Count % 2 == 0 ? TextMarkerStyle.Disc : TextMarkerStyle.Circle) : TextMarkerStyle.Decimal,
                Margin = new Thickness(0), Padding = new Thickness(24, 0, 0, 0),
            };
            if (lists.Count == 0) target.Add(list);
            else
            {
                var parent = lists[^1].List;
                if (parent.ListItems.LastListItem is { } last) last.Blocks.Add(list);
                else { var item = new Wpf.ListItem(); item.Blocks.Add(list); parent.ListItems.Add(item); }
            }
            lists.Add((list, numId));
        }
        lists[level].List.ListItems.Add(new Wpf.ListItem(para));
    }

    /// <summary>段落的編號：(numId, 層級, 是否為項目符號)；不是編號段落回傳 null。</summary>
    private static (int NumId, int Level, bool Bullet)? Numbering(W.Paragraph p, Ctx ctx)
    {
        var numPr = p.ParagraphProperties?.NumberingProperties;
        int? numId = numPr?.NumberingId?.Val?.Value;
        if (numId is null or 0) return null;
        int level = numPr?.NumberingLevelReference?.Val?.Value ?? 0;
        return (numId.Value, level, IsBullet(ctx, numId.Value, level));
    }

    private static bool IsBullet(Ctx ctx, int numId, int level)
    {
        var numbering = ctx.Main.NumberingDefinitionsPart?.Numbering;
        if (numbering is null) return true;
        var inst = numbering.Elements<W.NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        int? absId = inst?.AbstractNumId?.Val?.Value;
        var abs = numbering.Elements<W.AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == absId);
        var lvl = abs?.Elements<W.Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        var fmt = lvl?.NumberingFormat?.Val;
        return fmt is null || fmt.Value == W.NumberFormatValues.Bullet || fmt.Value == W.NumberFormatValues.None;
    }

    private static Wpf.Paragraph Paragraph(W.Paragraph wp, Ctx ctx)
    {
        var p = new Wpf.Paragraph { Margin = new Thickness(0) };
        var pPr = wp.ParagraphProperties;

        // 標題樣式（中文版 Word 的樣式 Id 常是「1」「2」，看樣式名稱「heading 1」才準）
        string? styleId = pPr?.ParagraphStyleId?.Val?.Value;
        var heading = HeadingOf(styleId, ctx);
        if (heading != WriterWindow.ParagraphStyle.Normal)
        {
            var (size, weight) = WriterWindow.StyleLook(heading);
            p.Tag = heading.ToString();
            p.FontSize = size;
            p.FontWeight = weight;
            p.Margin = new Thickness(0, WriterWindow.PtToPx(8), 0, WriterWindow.PtToPx(4));
        }

        if (pPr?.Justification?.Val is { } jc)
        {
            var v = jc.Value;
            p.TextAlignment = v == W.JustificationValues.Center ? TextAlignment.Center
                : v == W.JustificationValues.Right || v == W.JustificationValues.End ? TextAlignment.Right
                : v == W.JustificationValues.Both || v == W.JustificationValues.Distribute ? TextAlignment.Justify
                : TextAlignment.Left;
        }
        if (pPr?.Indentation is { } ind)
        {
            double left = Twips(ind.Left?.Value ?? ind.Start?.Value);
            p.Margin = new Thickness(left, p.Margin.Top, 0, p.Margin.Bottom);
            if (Twips(ind.FirstLine?.Value) is > 0 and var first) p.TextIndent = first;
            else if (Twips(ind.Hanging?.Value) is > 0 and var hang) p.TextIndent = -hang;
        }
        if (pPr?.SpacingBetweenLines is { } sp)
            p.Margin = new Thickness(p.Margin.Left, Twips(sp.Before?.Value), 0, Twips(sp.After?.Value));
        if (pPr?.PageBreakBefore is not null || ctx.PendingPageBreak)
        {
            WriterWindow.MarkPageBreak(p);
            ctx.PendingPageBreak = false;
        }

        foreach (var run in RunsOf(wp)) ReadRun(run, p, ctx);
        return p;
    }

    private static WriterWindow.ParagraphStyle HeadingOf(string? styleId, Ctx ctx)
    {
        if (styleId is null) return WriterWindow.ParagraphStyle.Normal;
        string name = ctx.StyleNames.TryGetValue(styleId, out var n) ? n : styleId.ToLowerInvariant();
        name = name.Replace(" ", "");
        if (name is "heading1" or "title") return WriterWindow.ParagraphStyle.Heading1;
        if (name == "heading2" || name == "subtitle") return WriterWindow.ParagraphStyle.Heading2;
        if (name.StartsWith("heading") && name.Length > 7) return WriterWindow.ParagraphStyle.Heading3;
        return WriterWindow.ParagraphStyle.Normal;
    }

    private static double Twips(string? value) => double.TryParse(value, out double t) ? PageSetup.FromTwips(t) : 0;

    /// <summary>段落裡依序出現的文字片段（包含超連結、插入的修訂裡的；略過文字方塊裡的，避免重複）。</summary>
    private static IEnumerable<W.Run> RunsOf(W.Paragraph wp) =>
        wp.Descendants<W.Run>().Where(r => !r.Ancestors<W.TextBoxContent>().Any() && !r.Ancestors<W.DeletedRun>().Any());

    // ═══════════════════════ 文字 ═══════════════════════

    private static void ReadRun(W.Run wr, Wpf.Paragraph p, Ctx ctx)
    {
        var props = wr.RunProperties;
        foreach (var child in wr.ChildElements)
        {
            switch (child)
            {
                case W.Text t when t.Text.Length > 0:
                    p.Inlines.Add(Styled(new Wpf.Run(t.Text), props));
                    break;
                case W.TabChar:
                    p.Inlines.Add(Styled(new Wpf.Run("\t"), props));
                    break;
                case W.Break br when br.Type?.Value == W.BreakValues.Page:
                    // Word 的分頁符號在段落中間：之後的內容從新的一頁開始
                    ctx.PendingPageBreak = true;
                    break;
                case W.Break:
                case W.CarriageReturn:
                    p.Inlines.Add(new Wpf.LineBreak());
                    break;
                case W.Drawing drawing when Image(drawing, ctx) is { } image:
                    p.Inlines.Add(new Wpf.InlineUIContainer(image));
                    break;
            }
        }
    }

    private static Wpf.Run Styled(Wpf.Run run, W.RunProperties? rp)
    {
        if (rp is null) return run;
        if (On(rp.Bold)) run.FontWeight = FontWeights.Bold;
        if (On(rp.Italic)) run.FontStyle = FontStyles.Italic;
        var deco = new TextDecorationCollection();
        if (rp.Underline?.Val is { } u && u.Value != W.UnderlineValues.None) deco.Add(TextDecorations.Underline[0]);
        else if (rp.Underline is not null && rp.Underline.Val is null) deco.Add(TextDecorations.Underline[0]);
        if (On(rp.Strike) || On(rp.DoubleStrike)) deco.Add(TextDecorations.Strikethrough[0]);
        if (deco.Count > 0) run.TextDecorations = deco;
        if (rp.FontSize?.Val?.Value is { } sz && double.TryParse(sz, out double half) && half > 0)
            run.FontSize = WriterWindow.PtToPx(half / 2);
        string? font = rp.RunFonts?.EastAsia?.Value ?? rp.RunFonts?.Ascii?.Value ?? rp.RunFonts?.HighAnsi?.Value;
        if (!string.IsNullOrWhiteSpace(font)) run.FontFamily = new FontFamily(font);
        if (Shared.Ui.FromHex(rp.Color?.Val?.Value) is { } color) run.Foreground = new SolidColorBrush(color);
        if (rp.Highlight?.Val is { } hl && HighlightColor(hl.Value) is { } hc) run.Background = new SolidColorBrush(hc);
        else if (Shared.Ui.FromHex(rp.Shading?.Fill?.Value) is { } fill) run.Background = new SolidColorBrush(fill);
        return run;
    }

    private static bool On(OnOffType? v) => v is not null && (v.Val is null || v.Val.Value);

    private static Color? HighlightColor(W.HighlightColorValues v)
    {
        if (v == W.HighlightColorValues.Yellow) return Colors.Yellow;
        if (v == W.HighlightColorValues.Green) return Colors.Lime;
        if (v == W.HighlightColorValues.Cyan) return Colors.Cyan;
        if (v == W.HighlightColorValues.Magenta) return Colors.Magenta;
        if (v == W.HighlightColorValues.Red) return Colors.Red;
        if (v == W.HighlightColorValues.Blue) return Colors.Blue;
        if (v == W.HighlightColorValues.LightGray) return Colors.LightGray;
        if (v == W.HighlightColorValues.DarkGray) return Colors.DarkGray;
        return null;
    }

    // ═══════════════════════ 圖片 ═══════════════════════

    private static System.Windows.Controls.Image? Image(W.Drawing drawing, Ctx ctx)
    {
        try
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value is not { } rid || ctx.Main.GetPartById(rid) is not ImagePart part) return null;
            byte[] bytes;
            using (var s = part.GetStream())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            Size? size = extent?.Cx?.Value is long cx && extent.Cy?.Value is long cy && cx > 0 && cy > 0
                ? new Size(PageSetup.FromEmu(cx), PageSetup.FromEmu(cy)) : null;
            string ext = part.ContentType switch { "image/jpeg" => "jpeg", "image/gif" => "gif", "image/bmp" => "bmp", _ => "png" };
            return WriterWindow.MakeImage(bytes, ext, size, ctx.ContentWidth);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;   // 讀不懂的圖片格式（例如 EMF）就略過
        }
    }

    // ═══════════════════════ 表格 ═══════════════════════

    private static Wpf.Table Table(W.Table wt, Ctx ctx)
    {
        var table = new Wpf.Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        var grid = wt.GetFirstChild<W.TableGrid>()?.Elements<W.GridColumn>().ToList() ?? [];
        foreach (var gc in grid)
            table.Columns.Add(new Wpf.TableColumn { Width = double.TryParse(gc.Width?.Value, out double tw) && tw > 0 ? new GridLength(PageSetup.FromTwips(tw)) : GridLength.Auto });
        var group = new Wpf.TableRowGroup();
        foreach (var wr in wt.Elements<W.TableRow>())
        {
            var row = new Wpf.TableRow();
            foreach (var wc in wr.Elements<W.TableCell>())
            {
                // 垂直合併的下半部：Word 留空格子；墨書不支援跨列，就留一格空的
                var cell = new Wpf.TableCell { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Padding = new Thickness(5, 3, 5, 3) };
                if (wc.TableCellProperties?.GridSpan?.Val?.Value is int span && span > 1) cell.ColumnSpan = span;
                if (Shared.Ui.FromHex(wc.TableCellProperties?.Shading?.Fill?.Value) is { } fill) cell.Background = new SolidColorBrush(fill);
                ReadBlocks(wc.ChildElements, cell.Blocks, ctx);
                if (cell.Blocks.Count == 0) cell.Blocks.Add(new Wpf.Paragraph());
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        if (table.Columns.Count == 0)
        {
            int cols = group.Rows.Select(r => r.Cells.Sum(c => Math.Max(1, c.ColumnSpan))).DefaultIfEmpty(1).Max();
            for (int i = 0; i < cols; i++) table.Columns.Add(new Wpf.TableColumn { Width = new GridLength(ctx.ContentWidth / cols) });
        }
        return table;
    }
}
