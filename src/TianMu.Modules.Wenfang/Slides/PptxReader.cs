using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace TianMu.Modules.Wenfang.Slides;

/// <summary>
/// 讀 PowerPoint 檔（.pptx）：文字方塊、形狀、圖片、背景色。
/// 位置沒寫在投影片上的（沿用版面配置的預留位置），就到版面配置與母片找。
/// 動畫、圖表、SmartArt、表格這類進階物件目前不讀。
/// </summary>
public static class PptxReader
{
    public static Deck Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(stream);
    }

    public static Deck Read(Stream stream)
    {
        using var doc = PresentationDocument.Open(stream, false);
        var pres = doc.PresentationPart ?? throw new InvalidDataException("這不是 PowerPoint 簡報檔。");
        var deck = new Deck { Theme = DeckTheme.Plain };
        if (pres.Presentation?.SlideSize is { } size && size.Cx?.Value > 0 && size.Cy?.Value > 0)
        {
            deck.Width = size.Cx.Value / (double)PptxWriter.EmuPerPx;
            deck.Height = size.Cy.Value / (double)PptxWriter.EmuPerPx;
        }

        var ids = pres.Presentation?.SlideIdList?.Elements<P.SlideId>() ?? [];
        foreach (var sid in ids)
        {
            if (sid.RelationshipId?.Value is not { } rel || pres.GetPartById(rel) is not SlidePart part) continue;
            deck.Slides.Add(ReadSlide(part));
        }
        if (deck.Slides.Count == 0) deck.Slides.Add(deck.NewSlide(SlideLayout.Title));
        return deck;
    }

    private static Slide ReadSlide(SlidePart part)
    {
        var slide = new Slide { Background = BackgroundOf(part) };
        var tree = part.Slide?.CommonSlideData?.ShapeTree;
        if (tree is null) return slide;
        foreach (var el in tree.ChildElements) ReadElement(el, part, slide, 0, 0);
        return slide;
    }

    private static void ReadElement(OpenXmlElement el, SlidePart part, Slide slide, double dx, double dy)
    {
        switch (el)
        {
            case P.Shape sp when ReadShape(sp, part) is { } item:
                item.X += dx; item.Y += dy;
                slide.Items.Add(item);
                break;
            case P.Picture pic when ReadPicture(pic, part) is { } item:
                item.X += dx; item.Y += dy;
                slide.Items.Add(item);
                break;
            case P.GroupShape group:
                // 群組：子物件的座標以 chOff 為基準，換算回投影片座標（不處理群組縮放）
                var xfrm = group.GroupShapeProperties?.GetFirstChild<A.TransformGroup>();
                double gx = Px(xfrm?.Offset?.X) - Px(xfrm?.ChildOffset?.X), gy = Px(xfrm?.Offset?.Y) - Px(xfrm?.ChildOffset?.Y);
                foreach (var child in group.ChildElements) ReadElement(child, part, slide, dx + gx, dy + gy);
                break;
        }
    }

    private static double Px(Int64Value? emu) => emu?.Value is { } v ? v / (double)PptxWriter.EmuPerPx : 0;

    private static SlideItem? ReadShape(P.Shape sp, SlidePart part)
    {
        var ph = sp.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
        var role = RoleOf(ph);
        // 頁尾、頁碼、日期這類預留位置不讀
        if (ph?.Type?.Value is { } t && (t == P.PlaceholderValues.SlideNumber || t == P.PlaceholderValues.Footer || t == P.PlaceholderValues.DateAndTime)) return null;

        var xfrm = sp.ShapeProperties?.Transform2D ?? InheritedTransform(ph, part);
        if (xfrm?.Offset is null || xfrm.Extents is null) return null;

        string text = TextOf(sp.TextBody);
        string? geom = sp.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.InnerText;
        string? fill = sp.ShapeProperties?.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value;
        bool isShape = ph is null && fill is not null;
        if (!isShape && text.Length == 0 && ph is null) return null;

        var item = new SlideItem
        {
            Kind = isShape ? ItemKind.Shape : ItemKind.Text,
            Role = role,
            X = Px(xfrm.Offset.X), Y = Px(xfrm.Offset.Y),
            W = xfrm.Extents.Cx?.Value / (double)PptxWriter.EmuPerPx ?? 100,
            H = xfrm.Extents.Cy?.Value / (double)PptxWriter.EmuPerPx ?? 50,
            Text = text,
            FontSize = role == ItemRole.Title ? 40 : role == ItemRole.Subtitle ? 24 : 18,
            Bold = role == ItemRole.Title,
            Fill = isShape ? fill : null,
            Geometry = geom switch { "ellipse" => "ellipse", "roundRect" => "roundRect", _ => "rect" },
            Bullets = role == ItemRole.Body,
            Anchor = role == ItemRole.Title ? VAnchor.Middle : VAnchor.Top,
        };
        if (isShape) { item.Align = HAlign.Center; item.Anchor = VAnchor.Middle; }

        var body = sp.TextBody;
        var anchor = body?.BodyProperties?.Anchor?.Value;
        if (anchor is { } an)
            item.Anchor = an == A.TextAnchoringTypeValues.Center ? VAnchor.Middle : an == A.TextAnchoringTypeValues.Bottom ? VAnchor.Bottom : VAnchor.Top;

        // 第一個有字的 run 決定整個方塊的格式
        var run = body?.Descendants<A.Run>().FirstOrDefault(r => !string.IsNullOrEmpty(r.Text?.Text));
        var rpr = run?.RunProperties;
        if (rpr?.FontSize?.Value is { } sz) item.FontSize = sz / 100.0;
        if (rpr?.Bold?.Value is { } b) item.Bold = b;
        if (rpr?.Italic?.Value is { } i) item.Italic = i;
        if (rpr?.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value is { } color) item.Color = color;
        if (rpr?.GetFirstChild<A.EastAsianFont>()?.Typeface?.Value is { } ea && !ea.StartsWith('+')) item.Font = ea;
        else if (rpr?.GetFirstChild<A.LatinFont>()?.Typeface?.Value is { } latin && !latin.StartsWith('+')) item.Font = latin;

        var ppr = body?.Elements<A.Paragraph>().FirstOrDefault()?.ParagraphProperties;
        if (ppr?.Alignment?.Value is { } al)
            item.Align = al == A.TextAlignmentTypeValues.Center ? HAlign.Center : al == A.TextAlignmentTypeValues.Right ? HAlign.Right : HAlign.Left;
        else if (role is ItemRole.Subtitle) item.Align = HAlign.Center;
        if (body?.Descendants<A.CharacterBullet>().Any() == true || body?.Descendants<A.AutoNumberedBullet>().Any() == true) item.Bullets = true;
        else if (body?.Descendants<A.NoBullet>().Any() == true) item.Bullets = false;
        if (role is ItemRole.None && body?.Descendants<A.CharacterBullet>().Any() != true) item.Bullets = false;
        return item;
    }

    private static SlideItem? ReadPicture(P.Picture pic, SlidePart part)
    {
        var xfrm = pic.ShapeProperties?.Transform2D;
        if (xfrm?.Offset is null || xfrm.Extents is null) return null;
        if (pic.BlipFill?.Blip?.Embed?.Value is not { } rid) return null;
        if (part.GetPartById(rid) is not ImagePart ip) return null;
        using var s = ip.GetStream();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        string ext = ip.ContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tif",
            "image/png" => ".png",
            _ => "",
        };
        if (ext.Length == 0) return null; // EMF、WMF、SVG 先不支援
        return new SlideItem
        {
            Kind = ItemKind.Image,
            X = Px(xfrm.Offset.X), Y = Px(xfrm.Offset.Y),
            W = xfrm.Extents.Cx?.Value / (double)PptxWriter.EmuPerPx ?? 100,
            H = xfrm.Extents.Cy?.Value / (double)PptxWriter.EmuPerPx ?? 100,
            ImageBytes = ms.ToArray(), ImageExtension = ext,
        };
    }

    private static ItemRole RoleOf(P.PlaceholderShape? ph)
    {
        if (ph is null) return ItemRole.None;
        var t = ph.Type?.Value;
        if (t == P.PlaceholderValues.Title || t == P.PlaceholderValues.CenteredTitle) return ItemRole.Title;
        if (t == P.PlaceholderValues.SubTitle) return ItemRole.Subtitle;
        return ItemRole.Body;
    }

    private static string TextOf(P.TextBody? body)
    {
        if (body is null) return "";
        var lines = body.Elements<A.Paragraph>().Select(p => string.Concat(p.Elements().Select(e => e switch
        {
            A.Run r => r.Text?.Text ?? "",
            A.Field f => f.Text?.Text ?? "",
            A.Break => "\v",
            _ => "",
        }))).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines).Replace('\v', ' ');
    }

    /// <summary>預留位置沒有自己的位置時，依類型／編號到版面配置、母片找。</summary>
    private static A.Transform2D? InheritedTransform(P.PlaceholderShape? ph, SlidePart part)
    {
        if (ph is null) return null;
        var layout = part.SlideLayoutPart;
        var fromLayout = Find(layout?.SlideLayout?.CommonSlideData?.ShapeTree, ph);
        if (fromLayout is not null) return fromLayout;
        return Find(layout?.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, ph);
    }

    private static A.Transform2D? Find(P.ShapeTree? tree, P.PlaceholderShape ph)
    {
        if (tree is null) return null;
        var shapes = tree.Descendants<P.Shape>().Select(s => (Shape: s, Ph: s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape))
            .Where(x => x.Ph is not null && x.Shape.ShapeProperties?.Transform2D is not null).ToList();
        var type = ph.Type?.Value ?? P.PlaceholderValues.Body;
        bool IsTitle(P.PlaceholderValues v) => v == P.PlaceholderValues.Title || v == P.PlaceholderValues.CenteredTitle;
        var match = shapes.FirstOrDefault(x => ph.Index?.Value is { } idx && x.Ph!.Index?.Value == idx)
            .Shape ?? shapes.FirstOrDefault(x => (x.Ph!.Type?.Value ?? P.PlaceholderValues.Body) == type).Shape
            ?? (IsTitle(type) ? shapes.FirstOrDefault(x => IsTitle(x.Ph!.Type?.Value ?? P.PlaceholderValues.Body)).Shape : null)
            ?? (!IsTitle(type) ? shapes.FirstOrDefault(x => (x.Ph!.Type?.Value ?? P.PlaceholderValues.Body) == P.PlaceholderValues.Body).Shape : null);
        return match?.ShapeProperties?.Transform2D;
    }

    private static string? BackgroundOf(SlidePart part)
    {
        static string? Solid(P.Background? bg) => bg?.BackgroundProperties?.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value;
        return Solid(part.Slide?.CommonSlideData?.Background)
            ?? Solid(part.SlideLayoutPart?.SlideLayout?.CommonSlideData?.Background)
            ?? Solid(part.SlideLayoutPart?.SlideMasterPart?.SlideMaster?.CommonSlideData?.Background);
    }
}
