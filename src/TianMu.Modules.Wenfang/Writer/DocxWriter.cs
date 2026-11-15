using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Wpf = System.Windows.Documents;

namespace TianMu.Modules.Wenfang.Writer;

/// <summary>
/// 把墨書的文件（WPF FlowDocument）存成 Word 的 .docx：
/// 段落（樣式、對齊、縮排、分頁）、文字格式（字型、字級、粗斜底線刪除線、顏色、螢光）、
/// 項目符號與編號、表格、圖片、紙張大小與邊界。
/// </summary>
public static class DocxWriter
{
    private const int BulletNumId = 1, DecimalNumId = 2;

    public static void Write(Wpf.FlowDocument doc, PageSetup setup, string path)
    {
        // 先寫到暫存檔，成功才換上去，避免存到一半壞掉
        string temp = path + ".tmp";
        using (var package = WordprocessingDocument.Create(temp, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body());
            AddStyles(main, doc);
            AddNumbering(main);
            var ctx = new Ctx(main, setup.Width - 2 * setup.Margin);
            var body = main.Document.Body!;
            foreach (var block in doc.Blocks) WriteBlock(body, block, ctx, 0);
            body.Append(new W.SectionProperties(
                new W.PageSize { Width = (UInt32Value)(uint)PageSetup.ToTwips(setup.Width), Height = (UInt32Value)(uint)PageSetup.ToTwips(setup.Height),
                    Orient = setup.Width > setup.Height ? W.PageOrientationValues.Landscape : W.PageOrientationValues.Portrait },
                new W.PageMargin { Top = PageSetup.ToTwips(setup.Margin), Bottom = PageSetup.ToTwips(setup.Margin),
                    Left = (UInt32Value)(uint)PageSetup.ToTwips(setup.Margin), Right = (UInt32Value)(uint)PageSetup.ToTwips(setup.Margin),
                    Header = 720, Footer = 720, Gutter = 0 }));
            main.Document.Save();
        }
        File.Move(temp, path, overwrite: true);
    }

    private sealed class Ctx(MainDocumentPart main, double contentWidth)
    {
        public MainDocumentPart Main { get; } = main;
        public double ContentWidth { get; } = contentWidth;
        public uint NextImageId = 1;
    }

    // ═══════════════════════ 區塊 ═══════════════════════

    private static void WriteBlock(OpenXmlElement parent, Wpf.Block block, Ctx ctx, int listLevel)
    {
        switch (block)
        {
            case Wpf.Paragraph p:
                parent.Append(Paragraph(p, ctx, null, listLevel));
                break;
            case Wpf.List list:
                WriteList(parent, list, ctx, listLevel);
                break;
            case Wpf.Table table:
                parent.Append(Table(table, ctx));
                break;
            case Wpf.BlockUIContainer { Child: System.Windows.Controls.Image image }:
                parent.Append(new W.Paragraph(new W.Run(Drawing(image, ctx))));
                break;
            case Wpf.Section section:
                foreach (var b in section.Blocks) WriteBlock(parent, b, ctx, listLevel);
                break;
        }
    }

    private static void WriteList(OpenXmlElement parent, Wpf.List list, Ctx ctx, int level)
    {
        bool numbered = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
            or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
        int numId = numbered ? DecimalNumId : BulletNumId;
        foreach (var item in list.ListItems)
        {
            foreach (var b in item.Blocks)
            {
                if (b is Wpf.Paragraph p) parent.Append(Paragraph(p, ctx, (numId, Math.Min(level, 8)), level));
                else if (b is Wpf.List inner) WriteList(parent, inner, ctx, level + 1);
                else WriteBlock(parent, b, ctx, level);
            }
        }
    }

    private static W.Paragraph Paragraph(Wpf.Paragraph p, Ctx ctx, (int NumId, int Level)? numbering, int listLevel)
    {
        var props = new W.ParagraphProperties();
        if (p.Tag is string style && style.StartsWith("Heading")) props.Append(new W.ParagraphStyleId { Val = style });
        if (numbering is { } n)
            props.Append(new W.NumberingProperties(new W.NumberingLevelReference { Val = n.Level }, new W.NumberingId { Val = n.NumId }));
        props.Append(new W.SpacingBetweenLines
        {
            Before = Twips(p.Margin.Top),
            After = Twips(p.Margin.Bottom),
        });
        if (numbering is null && (p.Margin.Left > 0.5 || Math.Abs(p.TextIndent) > 0.5))
        {
            var ind = new W.Indentation { Left = Twips(p.Margin.Left) };
            if (p.TextIndent > 0) ind.FirstLine = Twips(p.TextIndent);
            else if (p.TextIndent < 0) ind.Hanging = Twips(-p.TextIndent);
            props.Append(ind);
        }
        var jc = p.TextAlignment switch
        {
            TextAlignment.Center => W.JustificationValues.Center,
            TextAlignment.Right => W.JustificationValues.Right,
            TextAlignment.Justify => W.JustificationValues.Both,
            _ => (W.JustificationValues?)null,
        };
        if (jc is { } j) props.Append(new W.Justification { Val = j });

        var para = new W.Paragraph(props);
        if (p.BreakPageBefore) para.Append(new W.Run(new W.Break { Type = W.BreakValues.Page }));
        foreach (var inline in p.Inlines) WriteInline(para, inline, ctx);
        return para;
    }

    private static string Twips(double px) => PageSetup.ToTwips(Math.Max(0, px)).ToString();

    // ═══════════════════════ 文字 ═══════════════════════

    private static void WriteInline(OpenXmlElement para, Wpf.Inline inline, Ctx ctx)
    {
        switch (inline)
        {
            case Wpf.Run run when run.Text.Length > 0:
                para.Append(TextRun(run, run.Text));
                break;
            case Wpf.LineBreak br:
                para.Append(new W.Run(RunProps(br), new W.Break()));
                break;
            case Wpf.InlineUIContainer { Child: System.Windows.Controls.Image image }:
                para.Append(new W.Run(Drawing(image, ctx)));
                break;
            case Wpf.Span span:   // Bold、Italic、Underline、Hyperlink 都是 Span
                foreach (var child in span.Inlines) WriteInline(para, child, ctx);
                break;
        }
    }

    private static W.Run TextRun(Wpf.TextElement el, string text)
    {
        var run = new W.Run(RunProps(el));
        var parts = text.Split('\t');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) run.Append(new W.TabChar());
            if (parts[i].Length > 0) run.Append(new W.Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    /// <summary>文字格式：用 WPF 算好的實際值（含從外層繼承來的），所以巢狀的粗體、斜體都不會漏。</summary>
    internal static W.RunProperties RunProps(Wpf.TextElement el)
    {
        var rp = new W.RunProperties();
        string font = FirstFamily(el.FontFamily);
        rp.Append(new W.RunFonts { Ascii = font, HighAnsi = font, EastAsia = font, ComplexScript = font });
        if (el.FontWeight >= FontWeights.SemiBold) rp.Append(new W.Bold());
        if (el.FontStyle == FontStyles.Italic) rp.Append(new W.Italic());
        var deco = Decorations(el);
        if (deco.Any(d => d.Location == TextDecorationLocation.Strikethrough)) rp.Append(new W.Strike());
        if (el.Foreground is SolidColorBrush fg && fg.Color != Colors.Black)
            rp.Append(new W.Color { Val = Shared.Ui.ToHex(fg.Color) });
        rp.Append(new W.FontSize { Val = HalfPoints(el.FontSize) });
        rp.Append(new W.FontSizeComplexScript { Val = HalfPoints(el.FontSize) });
        if (deco.Any(d => d.Location == TextDecorationLocation.Underline)) rp.Append(new W.Underline { Val = W.UnderlineValues.Single });
        if (Highlight(el) is { } bg)
            rp.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = Shared.Ui.ToHex(bg) });
        return rp;
    }

    public static string HalfPoints(double px) => ((int)Math.Round(WriterWindow.PxToPt(px) * 2)).ToString();

    private static string FirstFamily(FontFamily f) => f.Source.Split(',')[0].Trim();

    /// <summary>底線、刪除線不會繼承，要往外層一路收集。</summary>
    private static List<TextDecoration> Decorations(Wpf.TextElement el)
    {
        var list = new List<TextDecoration>();
        for (DependencyObject? d = el; d is Wpf.TextElement te; d = te.Parent)
        {
            if (te is Wpf.Inline inl && inl.TextDecorations is { } td) list.AddRange(td);
            if (te is Wpf.Paragraph para && para.TextDecorations is { } pd) list.AddRange(pd);
            if (te is Wpf.Block) break;
        }
        return list;
    }

    /// <summary>螢光（背景色）也不會繼承。</summary>
    private static Color? Highlight(Wpf.TextElement el)
    {
        for (DependencyObject? d = el; d is Wpf.TextElement te; d = te.Parent)
        {
            if (te is Wpf.Block) break;
            if (te.Background is SolidColorBrush b && b.Color.A > 0) return b.Color;
        }
        return null;
    }

    // ═══════════════════════ 表格 ═══════════════════════

    private static W.Table Table(Wpf.Table table, Ctx ctx)
    {
        int cols = Math.Max(table.Columns.Count, table.RowGroups.SelectMany(g => g.Rows).Select(r => r.Cells.Sum(c => Math.Max(1, c.ColumnSpan))).DefaultIfEmpty(1).Max());
        var widths = Enumerable.Range(0, cols).Select(i =>
            i < table.Columns.Count && table.Columns[i].Width.IsAbsolute ? table.Columns[i].Width.Value : ctx.ContentWidth / cols).ToList();

        var border = new Func<OpenXmlElement>[]
        {
            () => new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "808080" },
            () => new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "808080" },
            () => new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = "808080" },
            () => new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = "808080" },
            () => new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = "808080" },
            () => new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = "808080" },
        };
        var t = new W.Table(new W.TableProperties(
            new W.TableWidth { Width = "0", Type = W.TableWidthUnitValues.Auto },
            new W.TableBorders(border.Select(b => b()))));
        t.Append(new W.TableGrid(widths.Select(w => new W.GridColumn { Width = Twips(w) })));
        foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
        {
            var tr = new W.TableRow();
            int col = 0;
            foreach (var cell in row.Cells)
            {
                int span = Math.Max(1, cell.ColumnSpan);
                double w = widths.Skip(col).Take(span).Sum();
                var tcp = new W.TableCellProperties(new W.TableCellWidth { Width = Twips(w), Type = W.TableWidthUnitValues.Dxa });
                if (span > 1) tcp.Append(new W.GridSpan { Val = span });
                var tc = new W.TableCell(tcp);
                foreach (var b in cell.Blocks) WriteBlock(tc, b, ctx, 0);
                if (!tc.Elements<W.Paragraph>().Any() && !tc.Elements<W.Table>().Any()) tc.Append(new W.Paragraph());
                if (tc.LastChild is W.Table) tc.Append(new W.Paragraph());   // Word 規定儲存格最後要是段落
                tr.Append(tc);
                col += span;
            }
            if (!tr.Elements<W.TableCell>().Any()) tr.Append(new W.TableCell(new W.Paragraph()));
            t.Append(tr);
        }
        return t;
    }

    // ═══════════════════════ 圖片 ═══════════════════════

    private static W.Drawing Drawing(System.Windows.Controls.Image image, Ctx ctx)
    {
        var (bytes, ext) = ImageBytes(image);
        var type = ext switch
        {
            "jpg" or "jpeg" => ImagePartType.Jpeg,
            "gif" => ImagePartType.Gif,
            "bmp" => ImagePartType.Bmp,
            _ => ImagePartType.Png,
        };
        var part = ctx.Main.AddImagePart(type);
        using (var ms = new MemoryStream(bytes)) part.FeedData(ms);
        string rid = ctx.Main.GetIdOfPart(part);

        double w = double.IsNaN(image.Width) ? image.ActualWidth : image.Width;
        double h = double.IsNaN(image.Height) ? image.ActualHeight : image.Height;
        if (w <= 0 || h <= 0)
        {
            var src = image.Source as BitmapSource;
            w = src?.PixelWidth ?? 100;
            h = src?.PixelHeight ?? 100;
        }
        long cx = PageSetup.ToEmu(w), cy = PageSetup.ToEmu(h);
        uint id = ctx.NextImageId++;
        string name = $"圖片 {id}";

        return new W.Drawing(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = id, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = name }, new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = rid }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }

    /// <summary>圖片的原始檔案；沒有的話（例如從剪貼簿貼上）就轉成 PNG。</summary>
    private static (byte[] Bytes, string Ext) ImageBytes(System.Windows.Controls.Image image)
    {
        if (image.Tag is ImageData data) return (data.Bytes, data.Extension);
        if (image.Source is BitmapSource bmp)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (ms.ToArray(), "png");
        }
        return ([], "png");
    }

    // ═══════════════════════ 樣式與編號 ═══════════════════════

    private static void AddStyles(MainDocumentPart main, Wpf.FlowDocument doc)
    {
        string font = FirstFamily(doc.FontFamily);
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new W.Styles(
            new W.DocDefaults(
                new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                    new W.RunFonts { Ascii = font, HighAnsi = font, EastAsia = font, ComplexScript = font },
                    new W.FontSize { Val = HalfPoints(doc.FontSize) }, new W.FontSizeComplexScript { Val = HalfPoints(doc.FontSize) },
                    new W.Languages { Val = "en-US", EastAsia = "zh-TW" })),
                new W.ParagraphPropertiesDefault(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto })),
            new W.Style(new W.StyleName { Val = "Normal" }, new W.PrimaryStyle()) { Type = W.StyleValues.Paragraph, StyleId = "Normal", Default = true });
        foreach (var (id, level) in new[] { ("Heading1", 1), ("Heading2", 2), ("Heading3", 3) })
        {
            var (size, _) = WriterWindow.StyleLook((WriterWindow.ParagraphStyle)level);
            styles.Append(new W.Style(
                new W.StyleName { Val = $"heading {level}" }, new W.BasedOn { Val = "Normal" }, new W.NextParagraphStyle { Val = "Normal" }, new W.PrimaryStyle(),
                new W.StyleParagraphProperties(new W.KeepNext(), new W.SpacingBetweenLines { Before = "240", After = "80" }, new W.OutlineLevel { Val = level - 1 }),
                new W.StyleRunProperties(new W.Bold(), new W.FontSize { Val = HalfPoints(size) }, new W.FontSizeComplexScript { Val = HalfPoints(size) }))
            { Type = W.StyleValues.Paragraph, StyleId = id });
        }
        part.Styles = styles;
        part.Styles.Save();
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        string[] bullets = ["•", "◦", "▪"];
        var bullet = new W.AbstractNum { AbstractNumberId = 1 };
        var dec = new W.AbstractNum { AbstractNumberId = 2 };
        for (int lvl = 0; lvl < 9; lvl++)
        {
            int indent = 480 * (lvl + 1);
            bullet.Append(new W.Level(
                new W.StartNumberingValue { Val = 1 },
                new W.NumberingFormat { Val = W.NumberFormatValues.Bullet },
                new W.LevelText { Val = bullets[lvl % 3] },
                new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                new W.PreviousParagraphProperties(new W.Indentation { Left = indent.ToString(), Hanging = "360" }))
            { LevelIndex = lvl });
            dec.Append(new W.Level(
                new W.StartNumberingValue { Val = 1 },
                new W.NumberingFormat { Val = lvl % 3 == 1 ? W.NumberFormatValues.LowerLetter : lvl % 3 == 2 ? W.NumberFormatValues.LowerRoman : W.NumberFormatValues.Decimal },
                new W.LevelText { Val = $"%{lvl + 1}." },
                new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                new W.PreviousParagraphProperties(new W.Indentation { Left = indent.ToString(), Hanging = "360" }))
            { LevelIndex = lvl });
        }
        part.Numbering = new W.Numbering(bullet, dec,
            new W.NumberingInstance(new W.AbstractNumId { Val = 1 }) { NumberID = BulletNumId },
            new W.NumberingInstance(new W.AbstractNumId { Val = 2 }) { NumberID = DecimalNumId });
        part.Numbering.Save();
    }
}
