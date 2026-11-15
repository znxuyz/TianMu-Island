namespace TianMu.Modules.Wenfang.Slides;

public enum ItemKind { Text, Image, Shape }
public enum ItemRole { None, Title, Subtitle, Body }
public enum HAlign { Left, Center, Right }
public enum VAnchor { Top, Middle, Bottom }

/// <summary>投影片上的一個物件（文字方塊、圖片或形狀）；座標是投影片像素（16:9 為 1280×720）。</summary>
public sealed class SlideItem
{
    public ItemKind Kind { get; set; }
    public ItemRole Role { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    /// <summary>文字；一行一段（\n 分段）。</summary>
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>文字顏色 RRGGBB；null 表示跟著佈景主題。</summary>
    public string? Color { get; set; }
    public string? Font { get; set; }
    public HAlign Align { get; set; }
    public VAnchor Anchor { get; set; }
    public bool Bullets { get; set; }

    /// <summary>形狀：rect、roundRect、ellipse。</summary>
    public string Geometry { get; set; } = "rect";

    /// <summary>形狀的填滿色 RRGGBB；null 表示用主題的強調色。</summary>
    public string? Fill { get; set; }

    public byte[]? ImageBytes { get; set; }
    public string ImageExtension { get; set; } = ".png";

    public SlideItem Clone()
    {
        var c = (SlideItem)MemberwiseClone();
        return c; // ImageBytes 不會被改寫，只會整個換掉，共用沒關係
    }
}

public sealed class Slide
{
    public List<SlideItem> Items { get; set; } = [];

    /// <summary>背景色 RRGGBB；null 表示跟著佈景主題。</summary>
    public string? Background { get; set; }

    public Slide Clone() => new() { Background = Background, Items = Items.Select(i => i.Clone()).ToList() };
}

/// <summary>佈景主題：背景、標題、內文、強調色與字型。</summary>
public sealed record DeckTheme(string Name, string Background, string Title, string Text, string Accent, string TitleFont, string BodyFont)
{
    public static readonly DeckTheme Xuanzhi = new("宣紙", "F4EEDF", "3A2717", "3B2F25", "A8863E", "Microsoft JhengHei", "Microsoft JhengHei");
    public static readonly DeckTheme Xuanqi = new("玄漆", "17130F", "D4B474", "ECE6D8", "C0412F", "Microsoft JhengHei", "Microsoft JhengHei");
    public static readonly DeckTheme Plain = new("素白", "FFFFFF", "1F1F1F", "404040", "2F6DB5", "Microsoft JhengHei", "Microsoft JhengHei");
    public static readonly DeckTheme Celadon = new("青瓷", "E7EFEB", "1E3D36", "2F4A44", "4F8A7D", "Microsoft JhengHei", "Microsoft JhengHei");

    public static readonly DeckTheme[] All = [Xuanzhi, Xuanqi, Plain, Celadon];
}

public enum SlideLayout { Title, TitleContent, TitleOnly, Blank }

public sealed class Deck
{
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 720;
    public DeckTheme Theme { get; set; } = DeckTheme.Xuanzhi;
    public List<Slide> Slides { get; set; } = [];

    public Deck Clone() => new() { Width = Width, Height = Height, Theme = Theme, Slides = Slides.Select(s => s.Clone()).ToList() };

    public string BackgroundOf(Slide s) => s.Background ?? Theme.Background;

    public string ColorOf(SlideItem i) => i.Color ?? (i.Role is ItemRole.Title ? Theme.Title : Theme.Text);

    public string FontOf(SlideItem i) => i.Font ?? (i.Role is ItemRole.Title ? Theme.TitleFont : Theme.BodyFont);

    public string FillOf(SlideItem i) => i.Fill ?? Theme.Accent;

    /// <summary>依版面配置做一張新投影片（位置依投影片寬度等比調整，4:3 也合用）。</summary>
    public Slide NewSlide(SlideLayout layout)
    {
        double k = Width / 1280, m = Height / 720;
        SlideItem T(ItemRole role, double x, double y, double w, double h, double size, bool bold, HAlign align, VAnchor anchor, bool bullets = false) =>
            new() { Kind = ItemKind.Text, Role = role, X = x * k, Y = y * m, W = w * k, H = h * m, FontSize = size, Bold = bold, Align = align, Anchor = anchor, Bullets = bullets };
        var s = new Slide();
        switch (layout)
        {
            case SlideLayout.Title:
                s.Items.Add(T(ItemRole.Title, 100, 210, 1080, 170, 48, true, HAlign.Center, VAnchor.Bottom));
                s.Items.Add(T(ItemRole.Subtitle, 160, 400, 960, 100, 24, false, HAlign.Center, VAnchor.Top));
                break;
            case SlideLayout.TitleContent:
                s.Items.Add(T(ItemRole.Title, 70, 36, 1140, 110, 36, true, HAlign.Left, VAnchor.Middle));
                s.Items.Add(T(ItemRole.Body, 70, 170, 1140, 500, 24, false, HAlign.Left, VAnchor.Top, bullets: true));
                break;
            case SlideLayout.TitleOnly:
                s.Items.Add(T(ItemRole.Title, 70, 36, 1140, 110, 36, true, HAlign.Left, VAnchor.Middle));
                break;
        }
        return s;
    }

    public static Deck CreateDefault()
    {
        var d = new Deck();
        d.Slides.Add(d.NewSlide(SlideLayout.Title));
        return d;
    }

    /// <summary>編輯畫面上空白文字方塊的提示字。</summary>
    public static string Hint(SlideItem i) => i.Role switch
    {
        ItemRole.Title => "按兩下輸入標題",
        ItemRole.Subtitle => "按兩下輸入副標題",
        ItemRole.Body => "按兩下輸入內容",
        _ => "按兩下輸入文字",
    };
}
