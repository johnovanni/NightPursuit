using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>The rear-view mirror: the road and the cars behind, as a mirror shows them (a car behind on your right appears on the right).</summary>
internal static class Mirror
{
    public static void Draw(DrawingContext dc, HudLayout l, World w, Projector cam)
    {
        var view = cam.View;
        double k = l.K;
        var m = new Rect(l.HalfWidth - 200 * k, 10 * k, 400 * k, 86 * k);
        dc.DrawRectangle(Neon.Black, Neon.Hud.Glow, m);
        dc.DrawRectangle(null, Neon.Hud.Core, m);
        dc.PushClip(new RectangleGeometry(m));

        double cx = m.X + m.Width / 2, horizon = m.Y + 30 * k;
        double cos = Math.Cos(cam.CamHeading), sin = Math.Sin(cam.CamHeading);

        // Looking backwards, depth is -F in the car's frame: the mirror turns with the car's nose, like the main view.
        bool MirrorPoint(double d, double lateral, out Point pt, out double scale)
        {
            RoadPoint q = view.At(d, lateral);
            double x = q.X - cam.CamLateral;
            double carX = x * cos - q.F * sin, carF = x * sin + q.F * cos;
            double depth = -carF;
            scale = depth > 1 ? 140 * k / depth : 0;
            pt = new Point(cx + carX * scale, horizon - (q.Y - Projector.CamHeight) * scale);
            return depth > 1;
        }

        var road = new LineBatch();
        Point? pl = null, pr = null;
        for (double d = -3; d > -300; d -= 10)
        {
            bool okL = MirrorPoint(d, -Track.RoadHalfWidth, out var left, out _);
            bool okR = MirrorPoint(d, Track.RoadHalfWidth, out var right, out _);
            if (!okL || !okR) { pl = pr = null; continue; }
            if (pl is Point a && pr is Point b) { road.Line(a, left); road.Line(b, right); }
            pl = left; pr = right;
        }
        road.Draw(dc, Neon.MirrorRoad);

        foreach (var e in w.Agents.Where(e => cam.Ahead(e) is < -2.5 and > -300 && e.State != VehicleState.Wrecked)
                                  .OrderBy(e => e.Distance))
        {
            if (!MirrorPoint(cam.Ahead(e), cam.LateralOf(e), out var g, out double s)) continue;
            double cw = e.Stats.Width * s, ch = e.Stats.Height * s;
            var pen = e.Kind switch { AgentKind.Blocker => Neon.BlockerCar, AgentKind.Pursuer => Neon.PursuerCar, _ => Neon.RunnerCar };
            dc.DrawRectangle(Neon.Black, pen.Core, new Rect(g.X - cw / 2, g.Y - ch, cw, ch));
            double lw = Math.Max(2, 0.3 * s);
            foreach (int side in Projector.Sides)
                dc.DrawEllipse(Neon.TextWhite, null, new Point(g.X + side * (cw / 2 - lw), g.Y - 0.5 * s), lw / 2 + 0.5, lw / 2 + 0.5);
        }
        dc.Pop();
        l.Text(dc, "REAR VIEW", m.X + 6 * k, m.Bottom - 17 * k, 13, Neon.TextGray);
    }
}
