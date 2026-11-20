using System.Windows;
using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>A stroke: a wide soft halo, a glow, and the core line.</summary>
internal sealed record ArtPen(Pen Halo, Pen Glow, Pen Core);

/// <summary>
/// Shared rules for the line art: luminous strokes with a soft bloom, a heavy outline over light detail, and gaps where
/// light would catch an edge. Only the edges that face you are drawn (no line along the bottom of a car, open windows),
/// and everything fades with distance.
/// </summary>
internal static class LineArt
{
    /// <summary>Gaps are left out of a line narrower than this many pixels: on a small shape they would read as damage.</summary>
    const double MinGapWidth = 30;

    /// <summary>How bright and heavy a line is at <paramref name="ahead"/> metres: 1 close up, falling to about 0.25 far away.</summary>
    public static double Fade(double ahead)
    {
        double f = 1 / (1 + Math.Pow(Math.Max(0, ahead - 20) / 260, 1.5));
        return Math.Clamp(f, 0.22, 1);
    }

    const int Levels = 12;
    static int Level(double fade) => Math.Clamp((int)Math.Round(fade * Levels), 1, Levels);

    static readonly Dictionary<(Color, int, int), ArtPen> Cache = new();
    static readonly Dictionary<ArtPen, ArtPen> DashedCache = new();

    /// <summary>The silhouette: the heaviest line of a thing.</summary>
    public static ArtPen Outline(Color c, double fade) => Get(c, fade, 0);
    /// <summary>Inner detail (windows, doors, plates): lighter and dimmer than the outline.</summary>
    public static ArtPen Detail(Color c, double fade) => Get(c, fade, 1);
    /// <summary>Road edges and scenery lines.</summary>
    public static ArtPen Scenery(Color c, double fade) => Get(c, fade, 2);

    static ArtPen Get(Color c, double fade, int role)
    {
        int level = Level(fade);
        var key = (c, level, role);
        if (Cache.TryGetValue(key, out var pen)) return pen;
        double f = (double)level / Levels;
        // Weight falls with distance more slowly than brightness, so far lines stay visible but quiet.
        double weight = 0.45 + 0.55 * f;
        (double width, double alpha) = role switch
        {
            0 => (2.3 * weight, f),
            1 => (1.0 * weight, 0.7 * f),
            _ => (1.6 * weight, f),
        };
        width = Math.Max(0.7, width);
        pen = new ArtPen(Make(c, 0.08 * alpha, width + 9 * weight), Make(c, 0.24 * alpha, width + 3.5 * weight), Make(c, alpha, width));
        Cache[key] = pen;
        return pen;
    }

    static Pen Make(Color c, double alpha, double width)
    {
        var p = new Pen(Neon.Solid(c, alpha), width)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    /// <summary>The colour of a classic pen, at full strength.</summary>
    public static Color ColorOf(NeonPen pen)
    {
        var c = ((SolidColorBrush)pen.Core.Brush).Color;
        return Color.FromRgb(c.R, c.G, c.B);
    }

    /// <summary>The solid colour of a brush, at full strength.</summary>
    public static Color ColorOf(Brush brush)
    {
        var c = ((SolidColorBrush)brush).Color;
        return Color.FromRgb(c.R, c.G, c.B);
    }

    /// <summary>The same pen with a dashed halo, glow and core. Hostile outlines use it: their dashes are part of their shape.</summary>
    public static ArtPen Dashed(ArtPen pen)
    {
        if (DashedCache.TryGetValue(pen, out var dashed)) return dashed;
        dashed = new ArtPen(Dash(pen.Halo), Dash(pen.Glow), Dash(pen.Core));
        DashedCache[pen] = dashed;
        return dashed;
    }

    static Pen Dash(Pen p)
    {
        var d = p.Clone();
        d.DashStyle = DashStyles.Dash;
        d.Freeze();
        return d;
    }

    public static void Draw(DrawingContext dc, Geometry g, ArtPen pen)
    {
        dc.DrawGeometry(null, pen.Halo, g);
        dc.DrawGeometry(null, pen.Glow, g);
        dc.DrawGeometry(null, pen.Core, g);
    }

    /// <summary>
    /// An open polyline through <paramref name="pts"/>, leaving out the stretches given as fractions of its length
    /// (unless the line is narrower than <see cref="MinGapWidth"/>). The gaps are where light would catch the edge, and the eye fills them in.
    /// </summary>
    public static StreamGeometry Polyline(IReadOnlyList<Point> pts, params (double From, double To)[] gaps)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            foreach (var pt in pts) { minX = Math.Min(minX, pt.X); maxX = Math.Max(maxX, pt.X); }
            if (gaps.Length == 0 || maxX - minX < MinGapWidth)
            {
                c.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, true);
            }
            else
            {
                double total = 0;
                for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).Length;
                // Walk the line, switching the pen off inside each gap.
                bool open = false;
                double run = 0;
                for (int i = 1; i < pts.Count; i++)
                {
                    var a = pts[i - 1];
                    var b = pts[i];
                    double len = (b - a).Length;
                    if (len <= 0) continue;
                    // Split this segment at every gap edge that falls inside it.
                    var cuts = new List<double> { 0, 1 };
                    foreach (var (from, to) in gaps)
                        foreach (double edge in new[] { from * total, to * total })
                            if (edge > run && edge < run + len) cuts.Add((edge - run) / len);
                    cuts.Sort();
                    for (int k = 1; k < cuts.Count; k++)
                    {
                        double mid = (run + len * (cuts[k - 1] + cuts[k]) / 2) / total;
                        bool inGap = gaps.Any(gp => mid > gp.From && mid < gp.To);
                        var p0 = a + (b - a) * cuts[k - 1];
                        var p1 = a + (b - a) * cuts[k];
                        if (inGap) { open = false; continue; }
                        if (!open) { c.BeginFigure(p0, false, false); open = true; }
                        c.LineTo(p1, true, true);
                    }
                    run += len;
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>Separate straight lines, all in one geometry.</summary>
    public static StreamGeometry Lines(IEnumerable<(Point A, Point B)> lines)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
            foreach (var (a, b) in lines)
            {
                c.BeginFigure(a, false, false);
                c.LineTo(b, true, false);
            }
        geo.Freeze();
        return geo;
    }
}
