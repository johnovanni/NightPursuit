using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Screen size, a scale factor (1 at 1280x720) and text drawing for one frame of screen-space drawing.
/// Everything in the HUD positions itself from these, so it follows the window size.
/// </summary>
internal sealed class HudLayout
{
    static readonly Typeface Bold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Typeface Regular = new("Consolas");

    public double Width { get; private set; }
    public double Height { get; private set; }
    public double HalfWidth { get; private set; }
    /// <summary>Scale relative to 1280x720: the smaller of the two axis ratios, kept within a sane range.</summary>
    public double K { get; private set; } = 1;
    public double Dpi { get; private set; } = 1;
    /// <summary>The text-size setting's scale (1, 1.25 or 1.5). Text and everything that holds text grows with it; at 1 nothing moves.</summary>
    public double TextScale { get; set; } = 1;
    /// <summary>Screen row of the top of the hood.</summary>
    public double HoodTop => Height * 0.86;

    public void Begin(double width, double height, double dpi)
    {
        Width = width;
        Height = height;
        HalfWidth = width / 2;
        K = Math.Clamp(Math.Min(width / 1280, height / 720), 0.4, 2.5);
        Dpi = dpi > 0 ? dpi : 1;
    }

    /// <summary>A length in pixels at the current scale.</summary>
    public double S(double v) => v * K;

    /// <summary>A length that holds text (a row height, a line gap, a box height or width): it grows with the text size.</summary>
    public double T(double v) => v * K * TextScale;

    /// <summary>How much more room <see cref="T"/> takes than <see cref="S"/>: 0 at TextScale 1.</summary>
    public double Grow(double v) => v * K * (TextScale - 1);

    public void Text(DrawingContext dc, string s, double x, double y, double size, Brush brush,
                     TextAlignment align = TextAlignment.Left, bool bold = false, bool scaled = true)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                   bold ? Bold : Regular, Math.Max(8, size * K * (scaled ? TextScale : 1)), brush, Dpi);
        double dx = align switch { TextAlignment.Center => -ft.Width / 2, TextAlignment.Right => -ft.Width, _ => 0 };
        dc.DrawText(ft, new Point(x + dx, y));
    }

    /// <summary>How wide <see cref="Text"/> would draw <paramref name="s"/>, in pixels.</summary>
    public double TextWidth(string s, double size, bool bold = false, bool scaled = true) =>
        new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                          bold ? Bold : Regular, Math.Max(8, size * K * (scaled ? TextScale : 1)), Brushes.White, Dpi).WidthIncludingTrailingWhitespace;

    /// <summary>
    /// The size (in the units <see cref="Text"/> takes) at which <paramref name="s"/> fits in <paramref name="room"/> pixels: <paramref name="size"/>
    /// itself when it already fits, otherwise smaller, but never below the Normal text size, so a large setting is never smaller than the default.
    /// </summary>
    public double FitSize(string s, double size, double room, bool bold = false)
    {
        double width = TextWidth(s, size, bold);
        return width <= room || width <= 0 ? size : Math.Max(size / TextScale, size * room / width);
    }

    /// <summary>Bold text centred on the screen at row <paramref name="y"/> (pixels).</summary>
    public void Centered(DrawingContext dc, string s, double y, double size, Brush brush) =>
        Text(dc, s, HalfWidth, y, size, brush, TextAlignment.Center, true);
}
