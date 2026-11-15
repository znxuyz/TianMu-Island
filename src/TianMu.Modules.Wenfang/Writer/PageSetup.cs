namespace TianMu.Modules.Wenfang.Writer;

/// <summary>紙張與邊界（單位：WPF 像素，1 英吋 = 96）。</summary>
public sealed record PageSetup(double Width, double Height, double Margin)
{
    public const double Inch = 96;
    public const double Cm = Inch / 2.54;

    public static readonly PageSetup A4 = new(21.0 * Cm, 29.7 * Cm, 2.54 * Cm);
    public static PageSetup Letter => new(8.5 * Inch, 11 * Inch, Inch);
    public static PageSetup A5 => new(14.8 * Cm, 21.0 * Cm, 2.0 * Cm);

    public PageSetup Landscape() => Width > Height ? this : this with { Width = Height, Height = Width };
    public PageSetup Portrait() => Width < Height ? this : this with { Width = Height, Height = Width };

    /// <summary>Word 的單位：1 twip = 1/20 點 = 1/1440 英吋。</summary>
    public static int ToTwips(double px) => (int)Math.Round(px * 1440 / Inch);
    public static double FromTwips(double twips) => twips * Inch / 1440;

    /// <summary>圖片尺寸的單位：1 像素 = 9525 EMU。</summary>
    public static long ToEmu(double px) => (long)Math.Round(px * 9525);
    public static double FromEmu(long emu) => emu / 9525.0;
}
