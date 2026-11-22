using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Missions;

namespace NightPursuit.App.Rendering;

/// <summary>
/// One standing piece behind a boss's body outline: a fin or the dome's cabin. A mirrored piece is given for the right
/// side (x &gt; 0) and drawn on both; otherwise its points are the whole piece.
/// </summary>
internal sealed record BossPiece((double X, double Y)[] Points, bool Mirrored, (double From, double To)[] Gaps);

/// <summary>
/// The outline tables of the three boss looks, in the car's own units: x as a fraction of the car's half-width, y in
/// metres up from the road. The boss in play (<see cref="VehicleRenderer"/>) and the briefing portrait
/// (<see cref="Screens.BossPortrait"/>) both draw from these, with the same pens: open outlines, gaps where light would
/// catch the edge, no line along the base.
/// </summary>
internal static class BossShapes
{
    /// <summary>The Twin look's body is this much wider than the Fin and Dome bodies.</summary>
    public const double TwinWidth = 1.1;

    static readonly (double X, double Y)[] FinBody = { (1, 0.224), (1, 0.62), (0.88, 0.8), (0.55, 1.02) };   // long and low
    static readonly (double X, double Y)[] TwinBody = FinBody.Select(p => (p.X * TwinWidth, p.Y)).ToArray();

    static readonly (double X, double Y)[] FinPoints = { (0.92, 0.7), (0.84, 1.5), (0.68, 1.5), (0.62, 0.95) };
    static readonly (double X, double Y)[] TwinOuterPoints = { (0.92, 0.7), (0.84, 1.5), (0.68, 1.5), (0.62, 0.95) };
    static readonly (double X, double Y)[] TwinInnerPoints = { (0.55, 0.7), (0.5, 1.35), (0.36, 1.35), (0.3, 0.95) };
    // The dome's cabin: a raised outline from y 0.8 to 1.35 between x -0.5 and 0.5, narrowing a little to the roof.
    static readonly (double X, double Y)[] DomePoints = { (-0.5, 0.8), (-0.4, 1.35), (0.4, 1.35), (0.5, 0.8) };

    static readonly (double, double)[] FinGap = { (0.42, 0.5) };
    static readonly (double, double)[] DomeGap = { (0.62, 0.7) };

    static readonly BossPiece[] FinPieces = { new(FinPoints, true, FinGap) };
    static readonly BossPiece[] TwinPieces = { new(TwinOuterPoints, true, FinGap), new(TwinInnerPoints, true, FinGap) };
    static readonly BossPiece[] DomePieces = { new(DomePoints, false, DomeGap) };

    /// <summary>The body's right-hand outline, from the bottom of the side up to the roof.</summary>
    public static (double X, double Y)[] Body(BossLook look) => look == BossLook.Twin ? TwinBody : FinBody;

    /// <summary>The standing pieces behind the body: fins (Fin one pair, Twin two pairs) or the dome's cabin.</summary>
    public static IReadOnlyList<BossPiece> Fins(BossLook look) => look switch
    {
        BossLook.Twin => TwinPieces,
        BossLook.Dome => DomePieces,
        _ => FinPieces,
    };

    /// <summary>The body's half-width (fraction of the car's half-width) at a height in metres, read off <see cref="Body"/>.</summary>
    public static double HalfWidth(BossLook look, double y)
    {
        var shape = Body(look);
        for (int i = 1; i < shape.Length; i++)
            if (y <= shape[i].Y)
            {
                var (a, b) = (shape[i - 1], shape[i]);
                return a.X + (b.X - a.X) * Math.Clamp((y - a.Y) / (b.Y - a.Y), 0, 1);
            }
        return shape[^1].X;
    }

    /// <summary>How much wider than the Fin body this look is; the tail strip scales by it.</summary>
    public static double Widening(BossLook look) => look == BossLook.Twin ? TwinWidth : 1;

    static IEnumerable<Point[]> Figures(BossPiece piece, Func<double, double, Point> p)
    {
        if (!piece.Mirrored)
        {
            yield return piece.Points.Select(q => p(q.X, q.Y)).ToArray();
            yield break;
        }
        foreach (int side in Projector.Sides)
            yield return piece.Points.Select(q => p(side * q.X, q.Y)).ToArray();
    }

    /// <summary>The black fill of every standing piece, so it hides the road lines behind it.</summary>
    public static StreamGeometry PiecesFill(BossLook look, Func<double, double, Point> p)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
            foreach (var piece in Fins(look))
                foreach (var pts in Figures(piece, p))
                {
                    c.BeginFigure(pts[0], true, true);
                    for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i], true, false);
                }
        geo.Freeze();
        return geo;
    }

    /// <summary>The open outline of each standing piece (no line along its base, which the body hides), with its gap.</summary>
    public static IEnumerable<StreamGeometry> PieceLines(BossLook look, Func<double, double, Point> p)
    {
        foreach (var piece in Fins(look))
            foreach (var pts in Figures(piece, p))
                yield return LineArt.Polyline(pts, piece.Gaps);
    }

    /// <summary>The body's outline points: up the left side, across the roof and down the right.</summary>
    public static List<Point> Edge(BossLook look, Func<double, double, Point> p)
    {
        var shape = Body(look);
        var edge = new List<Point>(shape.Length * 2);
        for (int i = 0; i < shape.Length; i++) edge.Add(p(-shape[i].X, shape[i].Y));
        for (int i = shape.Length - 1; i >= 0; i--) edge.Add(p(shape[i].X, shape[i].Y));
        return edge;
    }

    /// <summary>The body as a filled shape (to hide what is behind it).</summary>
    public static StreamGeometry BodyFill(BossLook look, Func<double, double, Point> p)
    {
        var edge = Edge(look, p);
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(edge[0], true, true);
            for (int i = 1; i < edge.Count; i++) c.LineTo(edge[i], true, false);
        }
        geo.Freeze();
        return geo;
    }

    // ---- the boss turning end over end (the finale): its roof and underside, seen square on.
    // Lines in (x, z): x as a fraction of the half-width, z as a fraction of the half-length, z = 1 at the nose.

    static readonly (double X1, double Z1, double X2, double Z2)[] Cabin =
    {
        (-0.55, 0.4, 0.55, 0.4),     // the windscreen's top edge
        (-0.55, -0.4, 0.55, -0.4),   // the rear window's top edge
        (-0.55, 0.4, -0.55, -0.4), (0.55, 0.4, 0.55, -0.4),
    };
    static readonly (double, double, double, double)[] FinTops = { (-0.78, -1, -0.78, -0.5), (0.78, -1, 0.78, -0.5) };
    static readonly (double, double, double, double)[] TwinTops =
        { (-0.78, -1, -0.78, -0.5), (0.78, -1, 0.78, -0.5), (-0.43, -1, -0.43, -0.55), (0.43, -1, 0.43, -0.55) };
    static readonly (double, double, double, double)[] DomeTop = Ellipse(0.45, 0.32, 0, 12);

    static readonly (double, double, double, double)[] Under =
    {
        (-0.85, 0.62, 0.85, 0.62), (-0.85, -0.62, 0.85, -0.62),            // the two axles
        (-0.95, 0.48, -0.95, 0.76), (0.95, 0.48, 0.95, 0.76),              // the four tyre ends
        (-0.95, -0.48, -0.95, -0.76), (0.95, -0.48, 0.95, -0.76),
        (0, 0.45, 0, -0.9),                                                 // the drive shaft
    };

    /// <summary>The detail lines on the roof: the cabin, and the look's fins, twin fins or dome.</summary>
    public static IReadOnlyList<(double X1, double Z1, double X2, double Z2)> RoofLines(BossLook look) => look switch
    {
        BossLook.Twin => Cabin.Concat(TwinTops).ToArray(),
        BossLook.Dome => Cabin.Concat(DomeTop).ToArray(),
        _ => Cabin.Concat(FinTops).ToArray(),
    };

    /// <summary>The detail lines on the underside: two axles, four tyre ends and the drive shaft.</summary>
    public static IReadOnlyList<(double X1, double Z1, double X2, double Z2)> UndersideLines(BossLook look) => Under;

    static (double, double, double, double)[] Ellipse(double rx, double rz, double z0, int n)
    {
        var lines = new (double, double, double, double)[n];
        for (int i = 0; i < n; i++)
        {
            double a = Math.Tau * i / n, b = Math.Tau * (i + 1) / n;
            lines[i] = (rx * Math.Cos(a), z0 + rz * Math.Sin(a), rx * Math.Cos(b), z0 + rz * Math.Sin(b));
        }
        return lines;
    }
}
