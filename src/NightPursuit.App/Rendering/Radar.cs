using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Top-down radar, bottom right. Each kind has its own shape as well as its colour: runner diamond (magenta),
/// blocker square (amber), pursuer triangle (red, pulsing); wrecks are X, oil slicks circles, you are the green arrow.
/// </summary>
internal static class Radar
{
    public static void Draw(DrawingContext dc, HudLayout l, World w)
    {
        var p = w.Player;
        var view = w.View;
        double k = l.K;
        var r = new Rect(l.Width - 214 * k, l.Height - 300 * k, 198 * k, 284 * k);
        dc.DrawRectangle(Neon.RadarFill, Neon.RadarLine.Core, r);
        dc.PushClip(new RectangleGeometry(r));

        double px = r.X + r.Width / 2, py = r.Bottom - 80 * k;
        double lat = 1.6 * k, lon = 0.42 * k;   // pixels per metre
        Point Map(double d, double x)
        {
            RoadPoint q = view.At(d, x);
            return new Point(px + (q.X - p.Lateral) * lat, py - q.F * lon);
        }

        var lines = new LineBatch();
        for (double d = -190; d < 500; d += 10)
        {
            lines.Line(Map(d, -Track.RoadHalfWidth), Map(d + 10, -Track.RoadHalfWidth));
            lines.Line(Map(d, Track.RoadHalfWidth), Map(d + 10, Track.RoadHalfWidth));
        }
        for (int ring = 100; ring <= 400; ring += 100)
            lines.Line(new Point(r.X + 4 * k, py - ring * lon), new Point(r.X + 14 * k, py - ring * lon));
        lines.Draw(dc, Neon.RadarLine);

        double u = 5 * k;   // marker half-size
        foreach (var h in w.Hazards)
        {
            var at = Map(h.Distance - p.Distance, h.Lateral);
            if (h is OilSlick) dc.DrawEllipse(null, Neon.Oil.Core, at, u * 0.8, u * 0.8);
            else if (h is Wreck) Cross(dc, at, u);
        }

        bool pulse = (int)(w.Time * 4) % 2 == 0;
        foreach (var e in w.Agents)
        {
            var at = Map(e.Distance - p.Distance, e.Lateral);
            if (e.State == VehicleState.Wrecked) { Cross(dc, at, u); continue; }
            switch (e.Kind)
            {
                case AgentKind.Runner:
                    Poly(dc, Neon.RadarRunnerFill, Neon.RadarRunner, new Point(at.X, at.Y - u * 1.2), new Point(at.X + u, at.Y),
                         new Point(at.X, at.Y + u * 1.2), new Point(at.X - u, at.Y));
                    break;
                case AgentKind.Blocker:
                    dc.DrawRectangle(Neon.TextAmber, null, new Rect(at.X - u, at.Y - u, u * 2, u * 2));
                    break;
                default:
                    double s = pulse ? u * 1.3 : u;
                    Poly(dc, Neon.TextRed, Neon.RadarPursuer, new Point(at.X, at.Y - s), new Point(at.X + s, at.Y + s), new Point(at.X - s, at.Y + s));
                    break;
            }
        }

        var me = new StreamGeometry();
        using (var c = me.Open())
        {
            c.BeginFigure(new Point(px, py - 9 * k), true, true);
            c.PolyLineTo(new[] { new Point(px - 6 * k, py + 6 * k), new Point(px + 6 * k, py + 6 * k) }, true, false);
        }
        me.Freeze();
        dc.PushTransform(new RotateTransform(p.Heading * 180 / Math.PI, px, py));   // the nose direction
        dc.DrawGeometry(Neon.TextGreen, null, me);
        dc.Pop();
        dc.Pop();
        l.Text(dc, "RADAR", r.X + 6 * k, r.Y + 4 * k, 13, Neon.TextGray);
    }

    static void Cross(DrawingContext dc, Point at, double u)
    {
        var x = new LineBatch();
        x.Line(at + new Vector(-u, -u), at + new Vector(u, u));
        x.Line(at + new Vector(-u, u), at + new Vector(u, -u));
        x.Draw(dc, Neon.RadarWreck);
    }

    static void Poly(DrawingContext dc, Brush fill, NeonPen pen, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            c.PolyLineTo(pts[1..], true, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill, pen.Core, g);
    }
}
