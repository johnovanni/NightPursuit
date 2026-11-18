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

    public void Text(DrawingContext dc, string s, double x, double y, double size, Brush brush,
                     TextAlignment align = TextAlignment.Left, bool bold = false)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                   bold ? Bold : Regular, Math.Max(8, size * K), brush, Dpi);
        double dx = align switch { TextAlignment.Center => -ft.Width / 2, TextAlignment.Right => -ft.Width, _ => 0 };
        dc.DrawText(ft, new Point(x + dx, y));
    }

    /// <summary>Bold text centred on the screen at row <paramref name="y"/> (pixels).</summary>
    public void Centered(DrawingContext dc, string s, double y, double size, Brush brush) =>
        Text(dc, s, HalfWidth, y, size, brush, TextAlignment.Center, true);
}
