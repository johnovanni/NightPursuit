using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The green "GAP" chevron flat on the road in a roadblock's open lane, pointing the way you drive. One shared drawing
/// for the rolling roadblock (Task 12, one chevron) and the parked ones (Task 13, three), so the two never drift apart.
/// </summary>
internal static class GapChevron
{
    /// <summary>How far behind a roadblock's cars the chevron sits, metres.</summary>
    public const double Behind = 10;

    const double MaxDistance = 600;
    const double HalfWidth = 1.8, Length = 3.0, Thickness = 1.0;   // metres, inside a 5 m lane
    static readonly Typeface Font = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Brush Fill = Neon.Solid(Neon.Green, 0.35);

    /// <summary>Distance ahead of the camera of the chevron for cars <paramref name="carsAhead"/> m ahead (nearest car's centre).</summary>
    public static double PlaceAt(double carsAhead) => carsAhead - Behind;

    /// <summary>Gap between the tips of a row of chevrons, metres.</summary>
    const double Spacing = 4.5;

    /// <summary>
    /// Draws <paramref name="count"/> chevrons in a row (the first with its tip <paramref name="ahead"/> m along the road, the
    /// others <see cref="Spacing"/> behind it), centred in the lane at <paramref name="lateral"/>. The word GAP goes above the
    /// first one when <paramref name="label"/> is set.
    /// </summary>
    public static void Draw(DrawingContext dc, Projector p, double ahead, double lateral, int count = 1, bool label = true)
    {
        for (int i = 0; i < count; i++) DrawOne(dc, p, ahead - i * Spacing, lateral, label && i == 0);
    }

    static void DrawOne(DrawingContext dc, Projector p, double ahead, double lateral, bool label)
    {
        if (ahead < 4 || ahead > MaxDistance) return;
        // A "^" lying on the road: the tip is furthest ahead; the two arms sweep back and out, Thickness apart.
        (double Z, double X)[] outline =
        {
            (ahead, 0), (ahead - Length, -HalfWidth), (ahead - Length - Thickness, -HalfWidth),
            (ahead - Thickness, 0), (ahead - Length - Thickness, HalfWidth), (ahead - Length, HalfWidth),
        };
        var pts = new Point[outline.Length];
        for (int i = 0; i < outline.Length; i++)
            if (!p.ToScreen(outline[i].Z, lateral + outline[i].X, 0, out pts[i], out _)) return;
        if (!p.ToScreen(ahead, lateral, 0, out var tip, out double depth)) return;
        if (p.ClipAt(ahead) <= 0 || pts[0].Y > p.ClipAt(ahead)) return;   // hidden behind a hill

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            c.PolyLineTo(pts.AsSpan(1).ToArray(), true, false);
        }
        g.Freeze();
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, p.ClipAt(ahead))));
        dc.DrawGeometry(Fill, null, g);
        LineArt.Draw(dc, g, LineArt.Outline(Neon.Green, LineArt.Fade(ahead)));
        if (label)
        {
            double size = Math.Clamp(0.9 * p.Ppm(depth), 9, 26);
            var text = new FormattedText("GAP", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Neon.TextGreen, 1.0);
            dc.DrawText(text, new Point(tip.X - text.Width / 2, tip.Y - text.Height - 2));   // just above the tip
        }
        dc.Pop();
    }
}
