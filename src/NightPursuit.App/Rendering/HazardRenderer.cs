using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>Oil slicks (purple ellipses flat on the road) and burning wrecks (crumpled outlines with flickering flames).</summary>
public static class HazardRenderer
{
    const double MaxDistance = 600;

    // Crumpled low outline: the edges sag at different heights and the roof line is broken.
    static readonly (double x, double y)[] WreckShape =
    {
        (-1.00, 0.00), (-1.05, 0.30), (-0.70, 0.22), (-0.45, 0.58), (-0.15, 0.36),
        (0.20, 0.64), (0.55, 0.30), (0.90, 0.40), (1.02, 0.12), (1.00, 0.00),
    };
    static readonly Point[] WreckPoints = new Point[WreckShape.Length - 1];   // reused scratch (UI thread only)

    public static void DrawSlicks(DrawingContext dc, Projector p, World w)
    {
        foreach (var h in w.Hazards)
        {
            if (h is not OilSlick o) continue;
            double dz = o.Distance - p.CamDistance;
            if (dz < 3 || dz > MaxDistance) continue;
            if (!p.ToScreen(dz, o.Lateral, 0, out var c, out double depth)
                || !p.ToScreen(dz - OilSlick.Radius, o.Lateral, 0, out var near, out _)
                || !p.ToScreen(dz + OilSlick.Radius, o.Lateral, 0, out var far, out _)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            double rx = OilSlick.Radius * p.Ppm(depth);
            double ry = Math.Max(1, Math.Abs(near.Y - far.Y) / 2);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            dc.DrawEllipse(Neon.OilFill, Neon.Oil.Glow, c, rx, ry);
            dc.DrawEllipse(null, Neon.Oil.Core, c, rx, ry);
            dc.Pop();
        }
    }

    /// <summary>Draws one wreck; the caller orders wrecks with cars so a far car never paints over a nearer wreck.</summary>
    public static void DrawWreck(DrawingContext dc, Projector p, Wreck h)
    {
        double dz = p.Ahead(h);
        if (dz < 2 || dz > MaxDistance) return;
        double rear = dz - h.Length / 2;
        if (!p.ToScreen(rear, h.Lateral, 0, out var g, out double depth)) return;
        double clip = p.ClipAt(rear);
        if (clip <= 0) return;
        double s = p.Ppm(depth);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        DrawWreck(dc, h, g, s);
        dc.Pop();
    }

    static void DrawWreck(DrawingContext dc, Wreck wreck, Point g, double s)
    {
        double hw = wreck.Width / 2 * s;
        var shape = WreckShape;
        var body = new StreamGeometry();
        using (var c = body.Open())
        {
            c.BeginFigure(new Point(g.X + shape[0].x * hw, g.Y - shape[0].y * s), true, true);
            var pts = WreckPoints;
            for (int i = 1; i < shape.Length; i++) pts[i - 1] = new Point(g.X + shape[i].x * hw, g.Y - shape[i].y * s);
            c.PolyLineTo(pts, true, false);
        }
        body.Freeze();
        dc.DrawGeometry(Neon.Black, Neon.WreckBody.Glow, body);
        dc.DrawGeometry(null, Neon.WreckBody.Core, body);

        var cracks = new LineBatch();
        cracks.Line(new Point(g.X - 0.45 * hw, g.Y - 0.58 * s), new Point(g.X - 0.1 * hw, g.Y - 0.1 * s));
        cracks.Line(new Point(g.X + 0.2 * hw, g.Y - 0.64 * s), new Point(g.X + 0.45 * hw, g.Y - 0.15 * s));
        cracks.Draw(dc, Neon.WreckBody);

        // Flames: heights flicker from sines of the burn time; the lane offset is the per-wreck phase.
        double phase = wreck.Lateral * 7.3;
        var outer = new LineBatch();
        var inner = new LineBatch();
        const int tongues = 6;
        for (int i = 0; i < tongues; i++)
        {
            double u = (i + 0.5) / tongues * 2 - 1;            // -1..1 across the car
            double bx = g.X + u * hw * 0.85;
            double flick = 0.5 + 0.5 * Math.Sin(wreck.Burn * (9 + i * 2.3) + phase + i * 1.7);
            double sway = Math.Sin(wreck.Burn * (5 + i) + phase + i) * 0.12 * s;
            double top = (0.45 + 0.95 * flick) * s;
            double baseW = hw / tongues * 0.9;
            double y0 = g.Y - 0.3 * s;
            var tip = new Point(bx + sway, y0 - top);
            outer.Line(new Point(bx - baseW, y0), tip);
            outer.Line(tip, new Point(bx + baseW, y0));
            var mid = new Point(bx + sway * 0.6, y0 - top * 0.6);
            inner.Line(new Point(bx - baseW * 0.4, y0), mid);
            inner.Line(mid, new Point(bx + baseW * 0.4, y0));
        }
        outer.Draw(dc, Neon.FlameOuter);
        inner.Draw(dc, Neon.FlameInner);
    }
}
