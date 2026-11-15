using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Documents;
using ClosedXML.Excel;
using TianMu.Modules.Wenfang.Shared;
using TianMu.Modules.Wenfang.Sheet;
using TianMu.Modules.Wenfang.Slides;
using TianMu.Modules.Wenfang.Writer;

namespace TianMu.Tests;

public class WenfangSheetTests
{
    [Theory]
    [InlineData("123", CellInput.Kind.Number, 123)]
    [InlineData("1,234.5", CellInput.Kind.Number, 1234.5)]
    [InlineData("NT$1,200", CellInput.Kind.Number, 1200)]
    [InlineData("15%", CellInput.Kind.Percent, 0.15)]
    [InlineData("-3e2", CellInput.Kind.Number, -300)]
    public void Numbers_are_recognised(string input, CellInput.Kind kind, double value)
    {
        var p = CellInput.Parse(input);
        Assert.Equal(kind, p.Kind);
        Assert.Equal(value, p.Number, 9);
    }

    [Fact]
    public void Formulas_text_dates_and_booleans()
    {
        Assert.Equal(CellInput.Kind.Formula, CellInput.Parse("=SUM(A1:A3)").Kind);
        Assert.Equal("SUM(A1:A3)", CellInput.Parse("=SUM(A1:A3)").Text);
        Assert.Equal(CellInput.Kind.Text, CellInput.Parse("'0912").Kind);
        Assert.Equal("0912", CellInput.Parse("'0912").Text);
        Assert.Equal(CellInput.Kind.Text, CellInput.Parse("天幕").Kind);
        Assert.Equal(CellInput.Kind.Boolean, CellInput.Parse("true").Kind);
        Assert.Equal(CellInput.Kind.Empty, CellInput.Parse("").Kind);
        var d = CellInput.Parse("2026/11/15");
        Assert.Equal(CellInput.Kind.Date, d.Kind);
        Assert.Equal(new DateTime(2026, 11, 15), d.Date);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(703, "AAA")]
    [InlineData(16384, "XFD")]
    public void Column_names(int col, string name)
    {
        Assert.Equal(name, CellInput.ColumnName(col));
        Assert.Equal((5, col), CellInput.ParseAddress(name + "5"));
    }

    [Fact]
    public void Addresses_parse_case_insensitively_and_reject_junk()
    {
        Assert.Equal((12, 2), CellInput.ParseAddress("b12"));
        Assert.Equal((1, 1), CellInput.ParseAddress(" $A$1 "));
        Assert.Null(CellInput.ParseAddress("12B"));
        Assert.Null(CellInput.ParseAddress("A0"));
        Assert.Null(CellInput.ParseAddress(""));
    }

    [Theory]
    [InlineData("General", 1, "0.0")]
    [InlineData("0.00", -1, "0.0")]
    [InlineData("0.0", -1, "0")]
    [InlineData("#,##0", 2, "#,##0.00")]
    [InlineData("0%", 1, "0.0%")]
    public void Decimal_buttons(string format, int delta, string expected) =>
        Assert.Equal(expected, CellInput.AdjustDecimals(format, delta));

    [Fact]
    public void Input_goes_into_cells_with_the_right_format_and_formulas_calculate()
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add("工作表1");
        CellInput.Apply(ws.Cell(1, 1), "10");
        CellInput.Apply(ws.Cell(2, 1), "20%");
        CellInput.Apply(ws.Cell(3, 1), "=A1*A2");
        CellInput.Apply(ws.Cell(4, 1), "2026/1/2");
        ws.RecalculateAllFormulas();
        Assert.Equal(2, ws.Cell(3, 1).Value.GetNumber(), 9);
        Assert.Equal("0%", ws.Cell(2, 1).Style.NumberFormat.Format);
        Assert.Equal("20%", CellInput.Raw(ws.Cell(2, 1)));
        Assert.Equal("=A1*A2", CellInput.Raw(ws.Cell(3, 1)));
        Assert.Equal("2026/1/2", CellInput.Raw(ws.Cell(4, 1)));
        Assert.Equal("A1:A2", SheetWindow.SumRange(ws, 3, 1) is { } r ? r : "");
    }

    [Fact]
    public void Xlsx_round_trip_keeps_values_formulas_and_styles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wenfang-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var book = new XLWorkbook())
            {
                var ws = book.Worksheets.Add("帳");
                CellInput.Apply(ws.Cell("A1"), "品項");
                CellInput.Apply(ws.Cell("A2"), "3");
                CellInput.Apply(ws.Cell("A3"), "4");
                CellInput.Apply(ws.Cell("A4"), "=SUM(A2:A3)");
                ws.Cell("A1").Style.Font.Bold = true;
                book.SaveAs(path);
            }
            using var back = new XLWorkbook(path);
            var s = back.Worksheet("帳");
            s.RecalculateAllFormulas();
            Assert.Equal("品項", s.Cell("A1").GetText());
            Assert.True(s.Cell("A1").Style.Font.Bold);
            Assert.Equal("SUM(A2:A3)", s.Cell("A4").FormulaA1);
            Assert.Equal(7, s.Cell("A4").Value.GetNumber(), 9);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Csv_handles_quotes_commas_and_newlines()
    {
        const string text = "名稱,說明\r\n\"甲,乙\",\"他說「好」\"\"嗯\"\"\"\r\n丙,\"兩\n行\"\r\n";
        var rows = Csv.Parse(text);
        Assert.Equal(3, rows.Count);
        Assert.Equal("甲,乙", rows[1][0]);
        Assert.Equal("他說「好」\"嗯\"", rows[1][1]);
        Assert.Equal("兩\n行", rows[2][1]);
        Assert.Equal(text, Csv.Format(rows));
        Assert.Equal(';', Csv.DetectSeparator("a;b;c\n1;2;3"));
        Assert.Equal('\t', Csv.DetectSeparator("a\tb\n1\t2"));
    }

    [Fact]
    public void Csv_import_does_not_turn_text_into_formulas()
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add("x");
        SheetWindow.FillFromCsv(ws, "a,b\n1,=1+1\n");
        Assert.Equal(1, ws.Cell(2, 1).Value.GetNumber());
        Assert.False(ws.Cell(2, 2).HasFormula);
        Assert.Equal("=1+1", ws.Cell(2, 2).GetText());
        Assert.Equal("a,b\r\n1,=1+1\r\n", SheetWindow.ToCsv(ws));
    }

    [Fact]
    public void Sheet_names_follow_excel_rules()
    {
        Assert.Equal("第一季", SheetWindow.SafeSheetName("第一季"));
        Assert.Equal("ab", SheetWindow.SafeSheetName("a/b"));
        Assert.Equal(31, SheetWindow.SafeSheetName(new string('字', 40)).Length);
        Assert.Equal("工作表1", SheetWindow.SafeSheetName("[]"));
    }
}

public class WenfangSlidesTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Pptx_round_trip_keeps_text_layout_images_and_backgrounds()
    {
        var deck = Deck.CreateDefault();
        deck.Slides[0].Items[0].Text = "天幕島 & 文房";
        deck.Slides[0].Items[1].Text = "第一行\n第二行";
        var content = deck.NewSlide(SlideLayout.TitleContent);
        content.Items[0].Text = "重點";
        content.Items[1].Text = "甲\n乙";
        content.Background = "112233";
        content.Items.Add(new SlideItem { Kind = ItemKind.Image, X = 100, Y = 120, W = 200, H = 100, ImageBytes = Png, ImageExtension = ".png" });
        content.Items.Add(new SlideItem { Kind = ItemKind.Shape, Geometry = "ellipse", X = 900, Y = 400, W = 200, H = 150, Fill = "C0412F", Text = "印", Color = "FFFFFF", FontSize = 30 });
        deck.Slides.Add(content);

        using var ms = new MemoryStream();
        PptxWriter.Write(deck, ms);
        ms.Position = 0;
        var back = PptxReader.Read(ms);

        Assert.Equal(1280, back.Width, 3);
        Assert.Equal(720, back.Height, 3);
        Assert.Equal(2, back.Slides.Count);
        var title = back.Slides[0].Items[0];
        Assert.Equal(ItemRole.Title, title.Role);
        Assert.Equal("天幕島 & 文房", title.Text);
        Assert.Equal(48, title.FontSize, 3);
        Assert.True(title.Bold);
        Assert.Equal(HAlign.Center, title.Align);
        Assert.Equal(deck.Slides[0].Items[0].X, title.X, 1);
        Assert.Equal(deck.Slides[0].Items[0].W, title.W, 1);
        Assert.Equal("第一行\n第二行", back.Slides[0].Items[1].Text);
        Assert.Equal(DeckTheme.Xuanzhi.Background, back.Slides[0].Background);

        var s2 = back.Slides[1];
        Assert.Equal("112233", s2.Background);
        Assert.True(s2.Items[1].Bullets);
        Assert.Equal("甲\n乙", s2.Items[1].Text);
        var img = Assert.Single(s2.Items, i => i.Kind == ItemKind.Image);
        Assert.Equal(Png, img.ImageBytes);
        Assert.Equal(200, img.W, 1);
        var shape = Assert.Single(s2.Items, i => i.Kind == ItemKind.Shape);
        Assert.Equal("ellipse", shape.Geometry);
        Assert.Equal("C0412F", shape.Fill);
        Assert.Equal("印", shape.Text);
    }

    [Fact]
    public void Empty_placeholders_survive_and_new_slides_scale_to_4_3()
    {
        var deck = new Deck { Width = 960, Height = 720 };
        var s = deck.NewSlide(SlideLayout.TitleContent);
        Assert.Equal(70 * 0.75, s.Items[0].X, 3);
        deck.Slides.Add(s);
        using var ms = new MemoryStream();
        PptxWriter.Write(deck, ms);
        ms.Position = 0;
        var back = PptxReader.Read(ms);
        Assert.Equal(960, back.Width, 3);
        Assert.Equal(2, back.Slides[0].Items.Count);
        Assert.All(back.Slides[0].Items, i => Assert.Equal("", i.Text));
    }

    [Fact]
    public void Clone_is_deep_enough_for_undo()
    {
        var deck = Deck.CreateDefault();
        var copy = deck.Clone();
        copy.Slides[0].Items[0].Text = "改了";
        copy.Slides.Add(new Slide());
        Assert.Equal("", deck.Slides[0].Items[0].Text);
        Assert.Single(deck.Slides);
    }
}

public class WenfangWriterTests
{
    [Fact]
    public void Word_count_counts_chinese_characters_and_english_words()
    {
        var (chars, words) = WriterWindow.Count("天幕 island 2");
        Assert.Equal(9, chars);
        Assert.Equal(2, words);
        Assert.Equal((0, 0), WriterWindow.Count("  \n "));
    }

    [Fact]
    public void Recent_files_put_the_latest_first_without_duplicates()
    {
        var list = RecentFiles.Merge(["a.docx", "B.xlsx", "c.pptx"], "b.xlsx");
        Assert.Equal(["b.xlsx", "a.docx", "c.pptx"], list);
        var many = RecentFiles.Merge(Enumerable.Range(0, 20).Select(i => $"{i}.docx"), "new.docx");
        Assert.Equal(RecentFiles.Limit, many.Count);
        Assert.Equal("new.docx", many[0]);
    }

    [Fact]
    public void Hub_knows_which_app_opens_which_file()
    {
        Assert.Equal(WenfangHub.Kind.Writer, WenfangHub.KindOf("報告.DOCX"));
        Assert.Equal(WenfangHub.Kind.Sheet, WenfangHub.KindOf(@"C:\帳\2026.xlsx"));
        Assert.Equal(WenfangHub.Kind.Sheet, WenfangHub.KindOf("名單.csv"));
        Assert.Equal(WenfangHub.Kind.Slides, WenfangHub.KindOf("簡報.pptx"));
        Assert.False(WenfangHub.CanOpen("相片.jpg"));
    }

    [Fact]
    public void Docx_round_trip_keeps_headings_formatting_lists_and_tables()
    {
        RunOnSta(() =>
        {
            var doc = new FlowDocument();
            var h = new Paragraph(new Run("第一章"));
            WriterWindow.SetStyle(h, WriterWindow.ParagraphStyle.Heading1);
            doc.Blocks.Add(h);
            doc.Blocks.Add(new Paragraph(new Bold(new Run("粗體"))) { TextAlignment = System.Windows.TextAlignment.Center });
            var list = new List { MarkerStyle = System.Windows.TextMarkerStyle.Disc };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("項目一"))));
            list.ListItems.Add(new ListItem(new Paragraph(new Run("項目二"))));
            doc.Blocks.Add(list);
            var table = WriterWindow.NewTable(2, 2, 600);
            ((Paragraph)table.RowGroups[0].Rows[1].Cells[1].Blocks.FirstBlock).Inlines.Add(new Run("右下"));
            doc.Blocks.Add(table);

            string path = Path.Combine(Path.GetTempPath(), $"wenfang-{Guid.NewGuid():N}.docx");
            try
            {
                DocxWriter.Write(doc, PageSetup.A4, path);
                var (back, setup) = DocxReader.Read(path);
                Assert.Equal(PageSetup.A4.Width, setup.Width, 0);
                var blocks = back.Blocks.ToList();
                var heading = Assert.IsType<Paragraph>(blocks[0]);
                Assert.Equal("Heading1", heading.Tag);
                Assert.Equal("第一章", new TextRange(heading.ContentStart, heading.ContentEnd).Text);
                var bold = Assert.IsType<Paragraph>(blocks[1]);
                Assert.Equal(System.Windows.TextAlignment.Center, bold.TextAlignment);
                var run = bold.Inlines.FirstInline is Span sp ? sp.Inlines.FirstInline : bold.Inlines.FirstInline;
                Assert.Equal(System.Windows.FontWeights.Bold, run!.FontWeight);
                var backList = Assert.IsType<List>(blocks[2]);
                Assert.Equal(2, backList.ListItems.Count);
                var backTable = Assert.IsType<Table>(blocks[3]);
                var cell = backTable.RowGroups[0].Rows[1].Cells[1];
                Assert.Equal("右下", new TextRange(cell.ContentStart, cell.ContentEnd).Text);
            }
            finally { File.Delete(path); }
        });
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
