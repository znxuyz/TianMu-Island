using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace TianMu.Modules.Wenfang.Slides;

/// <summary>
/// 把畫卷存成 PowerPoint 檔（.pptx）：一個母片、一個空白版面配置、一個佈景主題，
/// 每個物件都寫明位置與格式，PowerPoint、Keynote、LibreOffice 都打得開。
/// </summary>
public static class PptxWriter
{
    public const long EmuPerPx = 9525;
    private const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    public static void Write(Deck deck, string path)
    {
        string tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
            Write(deck, stream);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Write(Deck deck, Stream stream)
    {
        using var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        var pres = doc.AddPresentationPart();
        var master = pres.AddNewPart<SlideMasterPart>("rId1");
        var theme = master.AddNewPart<ThemePart>("rId2");
        Feed(theme, ThemeXml(deck.Theme));
        pres.AddPart(theme, "rId2");
        var layout = master.AddNewPart<SlideLayoutPart>("rId1");
        layout.AddPart(master, "rId1");
        Feed(layout, LayoutXml);
        Feed(master, MasterXml(deck.Theme));

        var ids = new StringBuilder();
        for (int i = 0; i < deck.Slides.Count; i++)
        {
            string rel = $"rId{10 + i}";
            var part = pres.AddNewPart<SlidePart>(rel);
            part.AddPart(layout, "rId1");
            Feed(part, SlideXml(deck, deck.Slides[i], part));
            ids.Append(CultureInfo.InvariantCulture, $"<p:sldId id=\"{256 + i}\" r:id=\"{rel}\"/>");
        }

        long cx = Emu(deck.Width), cy = Emu(deck.Height);
        Feed(pres, $"<p:presentation {Ns} saveSubsetFonts=\"1\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
            (deck.Slides.Count > 0 ? $"<p:sldIdLst>{ids}</p:sldIdLst>" : "") +
            $"<p:sldSz cx=\"{cx}\" cy=\"{cy}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");

        doc.PackageProperties.Creator = "天幕島 畫卷";
        doc.PackageProperties.Created = DateTime.UtcNow;
    }

    public static long Emu(double px) => (long)Math.Round(px * EmuPerPx);

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var ms = new MemoryStream(new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(ms);
    }

    private static string X(string? s) => SecurityElement.Escape(s ?? "") ?? "";

    private static string SlideXml(Deck deck, Slide slide, SlidePart part)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<p:sld {Ns}><p:cSld>");
        sb.Append(CultureInfo.InvariantCulture, $"<p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"{deck.BackgroundOf(slide)}\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>");
        sb.Append("<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>");
        int id = 2, img = 0;
        foreach (var item in slide.Items)
        {
            string xfrm = $"<a:xfrm><a:off x=\"{Emu(item.X)}\" y=\"{Emu(item.Y)}\"/><a:ext cx=\"{Emu(Math.Max(1, item.W))}\" cy=\"{Emu(Math.Max(1, item.H))}\"/></a:xfrm>";
            if (item.Kind == ItemKind.Image && item.ImageBytes is { Length: > 0 } bytes)
            {
                string rel = $"rIdImg{++img}";
                var ip = part.AddImagePart(PartType(item.ImageExtension), rel);
                using (var ms = new MemoryStream(bytes)) ip.FeedData(ms);
                sb.Append(CultureInfo.InvariantCulture, $"<p:pic><p:nvPicPr><p:cNvPr id=\"{id}\" name=\"圖片 {id}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>");
                sb.Append(CultureInfo.InvariantCulture, $"<p:blipFill><a:blip r:embed=\"{rel}\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>");
                sb.Append(CultureInfo.InvariantCulture, $"<p:spPr>{xfrm}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
            }
            else if (item.Kind != ItemKind.Image)
            {
                bool shape = item.Kind == ItemKind.Shape;
                string name = item.Role switch { ItemRole.Title => "標題", ItemRole.Subtitle => "副標題", ItemRole.Body => "內容", _ => shape ? "形狀" : "文字方塊" };
                string ph = item.Role switch
                {
                    ItemRole.Title => "<p:ph type=\"title\"/>",
                    ItemRole.Subtitle => "<p:ph type=\"subTitle\" idx=\"1\"/>",
                    ItemRole.Body => "<p:ph idx=\"1\"/>",
                    _ => "",
                };
                sb.Append(CultureInfo.InvariantCulture, $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name} {id}\"/><p:cNvSpPr{(shape ? "" : " txBox=\"1\"")}/><p:nvPr>{ph}</p:nvPr></p:nvSpPr>");
                string geom = shape ? item.Geometry : "rect";
                string fill = shape ? $"<a:solidFill><a:srgbClr val=\"{deck.FillOf(item)}\"/></a:solidFill><a:ln><a:noFill/></a:ln>" : "<a:noFill/>";
                sb.Append(CultureInfo.InvariantCulture, $"<p:spPr>{xfrm}<a:prstGeom prst=\"{geom}\"><a:avLst/></a:prstGeom>{fill}</p:spPr>");
                sb.Append(TextBody(deck, item));
                sb.Append("</p:sp>");
            }
            id++;
        }
        sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
        return sb.ToString();
    }

    private static string TextBody(Deck deck, SlideItem item)
    {
        string anchor = item.Anchor switch { VAnchor.Middle => "ctr", VAnchor.Bottom => "b", _ => "t" };
        string algn = item.Align switch { HAlign.Center => "ctr", HAlign.Right => "r", _ => "l" };
        int sz = (int)Math.Round(item.FontSize * 100);
        string font = X(deck.FontOf(item));
        string rpr = $"lang=\"zh-TW\" altLang=\"en-US\" sz=\"{sz}\" b=\"{(item.Bold ? 1 : 0)}\" i=\"{(item.Italic ? 1 : 0)}\" dirty=\"0\"";
        string fill = $"<a:solidFill><a:srgbClr val=\"{deck.ColorOf(item)}\"/></a:solidFill><a:latin typeface=\"{font}\"/><a:ea typeface=\"{font}\"/>";
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\" anchor=\"{anchor}\"><a:normAutofit/></a:bodyPr><a:lstStyle/>");
        foreach (var line in item.Text.Replace("\r\n", "\n").Split('\n'))
        {
            string bullet = item.Bullets && line.Length > 0
                ? "<a:pPr marL=\"342900\" indent=\"-342900\" algn=\"" + algn + "\"><a:lnSpc><a:spcPct val=\"110000\"/></a:lnSpc><a:spcBef><a:spcPts val=\"600\"/></a:spcBef><a:buFont typeface=\"Arial\"/><a:buChar char=\"•\"/></a:pPr>"
                : $"<a:pPr algn=\"{algn}\"><a:buNone/></a:pPr>";
            sb.Append("<a:p>").Append(bullet);
            if (line.Length > 0) sb.Append(CultureInfo.InvariantCulture, $"<a:r><a:rPr {rpr}>{fill}</a:rPr><a:t>{X(line)}</a:t></a:r>");
            sb.Append(CultureInfo.InvariantCulture, $"<a:endParaRPr {rpr}>{fill}</a:endParaRPr></a:p>");
        }
        sb.Append("</p:txBody>");
        return sb.ToString();
    }

    private static PartTypeInfo PartType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ImagePartType.Jpeg,
        ".gif" => ImagePartType.Gif,
        ".bmp" => ImagePartType.Bmp,
        ".tif" or ".tiff" => ImagePartType.Tiff,
        _ => ImagePartType.Png,
    };

    public static string ContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        _ => "image/png",
    };

    private const string LayoutXml =
        "<p:sldLayout " + Ns + " type=\"blank\" preserve=\"1\"><p:cSld name=\"空白\"><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld>" +
        "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";

    private static string MasterXml(DeckTheme t) =>
        "<p:sldMaster " + Ns + "><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"" + t.Background + "\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>" +
        "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld>" +
        "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
        "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
        "<p:txStyles><p:titleStyle><a:lvl1pPr><a:defRPr sz=\"4000\"><a:solidFill><a:srgbClr val=\"" + t.Title + "\"/></a:solidFill></a:defRPr></a:lvl1pPr></p:titleStyle>" +
        "<p:bodyStyle><a:lvl1pPr><a:defRPr sz=\"2400\"><a:solidFill><a:srgbClr val=\"" + t.Text + "\"/></a:solidFill></a:defRPr></a:lvl1pPr></p:bodyStyle>" +
        "<p:otherStyle><a:lvl1pPr><a:defRPr sz=\"1800\"/></a:lvl1pPr></p:otherStyle></p:txStyles></p:sldMaster>";

    private static string ThemeXml(DeckTheme t)
    {
        string font = X(t.BodyFont), title = X(t.TitleFont);
        string Fill(string c) => $"<a:solidFill><a:srgbClr val=\"{c}\"/></a:solidFill>";
        string Ln(int w) => $"<a:ln w=\"{w}\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>";
        return "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"" + X(t.Name) + "\"><a:themeElements>" +
            "<a:clrScheme name=\"" + X(t.Name) + "\">" +
            $"<a:dk1><a:srgbClr val=\"{t.Text}\"/></a:dk1><a:lt1><a:srgbClr val=\"{t.Background}\"/></a:lt1>" +
            $"<a:dk2><a:srgbClr val=\"{t.Title}\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
            $"<a:accent1><a:srgbClr val=\"{t.Accent}\"/></a:accent1><a:accent2><a:srgbClr val=\"C0412F\"/></a:accent2><a:accent3><a:srgbClr val=\"4F8A7D\"/></a:accent3>" +
            "<a:accent4><a:srgbClr val=\"2F6DB5\"/></a:accent4><a:accent5><a:srgbClr val=\"7A5C9E\"/></a:accent5><a:accent6><a:srgbClr val=\"D08A2E\"/></a:accent6>" +
            "<a:hlink><a:srgbClr val=\"2F6DB5\"/></a:hlink><a:folHlink><a:srgbClr val=\"7A5C9E\"/></a:folHlink></a:clrScheme>" +
            "<a:fontScheme name=\"" + X(t.Name) + "\">" +
            $"<a:majorFont><a:latin typeface=\"{title}\"/><a:ea typeface=\"{title}\"/><a:cs typeface=\"\"/></a:majorFont>" +
            $"<a:minorFont><a:latin typeface=\"{font}\"/><a:ea typeface=\"{font}\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
            "<a:fmtScheme name=\"天幕\"><a:fillStyleLst>" + Fill("FFFFFF") + Fill("EEEEEE") + Fill("DDDDDD") + "</a:fillStyleLst>" +
            "<a:lnStyleLst>" + Ln(6350) + Ln(12700) + Ln(19050) + "</a:lnStyleLst>" +
            "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
            "<a:bgFillStyleLst>" + Fill("FFFFFF") + Fill("EEEEEE") + Fill("DDDDDD") + "</a:bgFillStyleLst></a:fmtScheme>" +
            "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
    }
}
