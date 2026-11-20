using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>The rear-view mirror: the road and the cars behind, as a mirror shows them (a car behind on your right appears on the right).</summary>
internal static class Mirror
{
    const double BossScale = 1.3;
    /// <summary>The mirror's rectangle, so the world layer can keep clear of it.</summary>
    public static Rect Bounds(HudLayout l) => new(l.HalfWidth - 200 * l.K, 10 * l.K, 400 * l.K, 86 * l.K);

    public static void Draw(DrawingContext dc, HudLayout l, World w, Projector cam)
    {
        var view = cam.View;
        double k = l.K;
        var m = Bounds(l);
        dc.DrawRectangle(Neon.Black, null, m);
        LineArt.Draw(dc, new RectangleGeometry(m), LineArt.Detail(Neon.Cyan, 1));
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

        // The road fades with distance behind you, in four bands (by the segment's near end) instead of one pen per segment.
        double[] bandEdges = { 50, 110, 190 };       // metres behind; a fourth band takes the rest
        double[] bandFade = { LineArt.Fade(25), LineArt.Fade(80), LineArt.Fade(150), LineArt.Fade(240) };
        var road = new[] { new LineBatch(), new LineBatch(), new LineBatch(), new LineBatch() };
        Point? pl = null, pr = null;
        for (double d = -3; d > -300; d -= 10)
        {
            bool okL = MirrorPoint(d, -Track.RoadHalfWidth, out var left, out _);
            bool okR = MirrorPoint(d, Track.RoadHalfWidth, out var right, out _);
            if (!okL || !okR) { pl = pr = null; continue; }
            if (pl is Point a && pr is Point b)
            {
                int band = 0;
                while (band < bandEdges.Length && -(d + 10) > bandEdges[band]) band++;
                road[band].Line(a, left); road[band].Line(b, right);
            }
            pl = left; pr = right;
        }
        for (int i = 0; i < road.Length; i++) road[i].Draw(dc, LineArt.Scenery(Neon.Cyan, bandFade[i]));

        // Cars far to near, then the smoke over them (a car inside it fades behind it), then hostile drops and enemy missiles.
        foreach (var e in w.Agents.Where(e => cam.Ahead(e) is < -2.5 and > -300 && e.State != VehicleState.Wrecked)
                                  .OrderBy(e => cam.Ahead(e)))
        {
            if (!MirrorPoint(cam.Ahead(e), cam.LateralOf(e), out var g, out double s)) continue;
            double scale = e.Kind == AgentKind.Boss ? BossScale : 1;
            double cw = e.Stats.Width * s * scale, ch = e.Stats.Height * s * scale;
            var carPen = LineArt.Detail(LineArt.ColorOf(VehicleRenderer.PenFor(e.Kind)), 1);
            dc.DrawRectangle(Neon.Black, null, new Rect(g.X - cw / 2, g.Y - ch, cw, ch));
            // Left side, top, right side: no line along the bottom, where the car stands on the road.
            LineArt.Draw(dc, LineArt.Polyline(new[]
            {
                new Point(g.X - cw / 2, g.Y), new Point(g.X - cw / 2, g.Y - ch),
                new Point(g.X + cw / 2, g.Y - ch), new Point(g.X + cw / 2, g.Y),
            }), carPen);
            if (KindRules.FactionOf(e.Kind) == Faction.Police)   // a two-colour top line, flashing like the light bar
            {
                double topY = g.Y - ch;
                foreach (int side in Projector.Sides)
                {
                    var half = new LineBatch();
                    half.Line(new Point(side < 0 ? g.X - cw / 2 : g.X, topY), new Point(side < 0 ? g.X : g.X + cw / 2, topY));
                    half.Draw(dc, LineArt.Outline(LineArt.ColorOf(Neon.LightPen(w.Time, side)), 1));
                }
            }
            if (e.Kind == AgentKind.Boss)   // the gold-trimmed tail strip
            {
                var strip = new Rect(g.X - cw * 0.46, g.Y - ch * 0.45, cw * 0.92, ch * 0.22);
                dc.DrawRectangle(Neon.TailLight, null, strip);
                LineArt.Draw(dc, new RectangleGeometry(strip), LineArt.Outline(LineArt.ColorOf(Neon.BossTrim), 1));
            }
            double lw = Math.Max(2, 0.3 * s);
            foreach (int side in Projector.Sides)
                dc.DrawEllipse(Neon.TextWhite, null, new Point(g.X + side * (cw / 2 - lw), g.Y - 0.5 * s), lw / 2 + 0.5, lw / 2 + 0.5);
        }

        foreach (var h in w.Hazards)
        {
            if (h is SmokeCloud smoke)
            {
                double dz = cam.Ahead(smoke);
                if (dz - SmokeCloud.HalfLength > -4 || dz < -300) continue;   // nothing of it is behind the car, or it is too far
                double fade = Math.Clamp((SmokeCloud.Lifetime - smoke.Age) / 1.0, 0, 1);
                if (!MirrorPoint(Math.Min(dz, -4), smoke.Lateral, out var c, out double s)) continue;
                double rx = Math.Max(3, SmokeCloud.HalfWidth * s), ry = Math.Max(2, 2.2 * s);
                dc.PushOpacity(fade);
                dc.DrawEllipse(Neon.SmokeFill, null, new Point(c.X, c.Y - ry * 0.6), rx, ry);
                dc.Pop();
            }
            else if (h is Caltrops { Hostile: true } caltrops)
            {
                double dz = caltrops.Distance - cam.CamDistance;   // caltrops never move, so no interpolation is needed
                if (dz is >= -2.5 or <= -300 || !MirrorPoint(dz, caltrops.Lateral, out var c, out double s)) continue;
                double half = Caltrops.HalfWidth * s;
                var dots = new LineBatch();
                dots.Line(new Point(c.X - half, c.Y), new Point(c.X + half, c.Y));
                dots.Draw(dc, LineArt.Dashed(LineArt.Detail(LineArt.ColorOf(Neon.HostileOutline), 1)));
                foreach (double f in new[] { -0.6, 0, 0.6 })
                    dc.DrawEllipse(Neon.TextSteel, null, new Point(c.X + f * half, c.Y), 1.8, 1.8);
            }
        }

        foreach (var missile in w.EnemyMissiles)
        {
            double dz = cam.Ahead(missile);
            if (dz is >= -2.5 or <= -300 || !MirrorPoint(dz, cam.LateralOf(missile), out var c, out double s)) continue;
            double r = Math.Max(3, 1.0 * s);
            var at = new Point(c.X, c.Y - 0.8 * s);
            dc.DrawEllipse(Neon.TextRed, null, at, Math.Max(1.8, 0.35 * s), Math.Max(1.8, 0.35 * s));
            LineArt.Draw(dc, new EllipseGeometry(at, r, r), LineArt.Detail(LineArt.ColorOf(Neon.EnemyMissile), 1));
        }
        dc.Pop();
        l.Text(dc, "REAR VIEW", m.X + 6 * k, m.Bottom - 17 * k, 13, Neon.TextGray);
    }
}
