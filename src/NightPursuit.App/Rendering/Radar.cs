using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Top-down radar, bottom right. Each kind has its own shape as well as its colour: runner diamond (magenta),
/// blocker square (amber), pursuer triangle (red, pulsing), gunner ring with a dot (orange-red), dropper
/// down-triangle (teal), civilian small grey dot, police a white-outlined square with a dark bar flashing blue/red (larger for the armored SUV), armored hexagon (steel blue), boss pulsing double ring with a gold inner ring (white); a roadblock (parked or rolling) is a red bar across its held lanes with a green tick in the open lane, a speed trap an amber S; wrecks are X, oil slicks circles, you are the green arrow.
/// </summary>
internal static class Radar
{
    /// <summary>The radar's box in the bottom-right corner.</summary>
    public static Rect Bounds(HudLayout l) => new(l.Width - 214 * l.K, l.Height - 300 * l.K, 198 * l.K, 284 * l.K);

    public static void Draw(DrawingContext dc, HudLayout l, World w, Projector cam)
    {
        var view = cam.View;
        double k = l.K;
        var r = Bounds(l);
        dc.DrawRectangle(Neon.RadarFill, Neon.RadarLine.Core, r);
        dc.PushClip(new RectangleGeometry(r));

        double px = r.X + r.Width / 2, py = r.Bottom - 80 * k;
        double lat = 1.6 * k, lon = 0.42 * k;   // pixels per metre
        Point Map(double d, double x)
        {
            RoadPoint q = view.At(d, x);
            return new Point(px + (q.X - cam.CamLateral) * lat, py - q.F * lon);
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
            var at = Map(h is Wreck wk ? cam.Ahead(wk) : h.Distance - cam.CamDistance, h.Lateral);
            if (h is OilSlick) dc.DrawEllipse(null, Neon.Oil.Core, at, u * 0.8, u * 0.8);
            else if (h is Wreck) Cross(dc, at, u);
            else if (h is SmokeCloud smoke)   // a grey rounded rectangle at its real footprint
            {
                var c = Map(cam.Ahead(smoke), smoke.Lateral);
                dc.DrawRoundedRectangle(Neon.SmokeFill, SmokeRim, new Rect(c.X - SmokeCloud.HalfWidth * lat, c.Y - SmokeCloud.HalfLength * lon,
                                        2 * SmokeCloud.HalfWidth * lat, 2 * SmokeCloud.HalfLength * lon), 3 * k, 3 * k);
            }
            else if (h is Caltrops)   // three steel dots
            {
                foreach (double f in new[] { -1.0, 0, 1.0 })
                    dc.DrawEllipse(Neon.TextSteel, null, new Point(at.X + f * u * 0.9, at.Y), u * 0.3, u * 0.3);
            }
            else if (h is Roadblock block) RoadblockMark(dc, Map, k, h.Distance - cam.CamDistance, block.GapLane, 0);
            else if (h is SpeedTrap trap)   // an amber S at the road edge it stands on (red, and "S!", once it caught you)
                l.Text(dc, trap.Caught ? "S!" : "S", at.X, at.Y - 7 * k, 14, trap.Caught ? Neon.TextRed : Neon.TextAmber, TextAlignment.Center, true);
            else if (h is SupplyCrate crate)   // a white square with its item letter
            {
                double q = u * 1.4;
                dc.DrawRectangle(Neon.TextWhite, null, new Rect(at.X - q, at.Y - q, q * 2, q * 2));
                l.Text(dc, ItemLetter(crate.Item), at.X, at.Y - 6.5 * k, 11, Neon.Black, TextAlignment.Center, true);
            }
        }

        bool pulse = (int)(w.Time * 4) % 2 == 0;
        foreach (var e in w.Agents)
        {
            var at = Map(cam.Ahead(e), cam.LateralOf(e));
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
                case AgentKind.Gunner:   // a ring with a centre dot
                    dc.DrawEllipse(null, Neon.RadarGunner.Core, at, u * 1.1, u * 1.1);
                    dc.DrawEllipse(Neon.TextOrangeRed, null, at, u * 0.4, u * 0.4);
                    break;
                case AgentKind.Dropper:   // a down-triangle
                    Poly(dc, Neon.TextTeal, Neon.RadarDropper, new Point(at.X - u * 1.1, at.Y - u), new Point(at.X + u * 1.1, at.Y - u), new Point(at.X, at.Y + u * 1.2));
                    break;
                case AgentKind.Armored:   // a hexagon
                    Poly(dc, Neon.TextSteelBlue, Neon.RadarArmored, Hexagon(at, u * 1.15));
                    break;
                case AgentKind.Boss:   // a pulsing white double ring around a gold inner ring
                    double ring = pulse ? 1.9 : 1.6;
                    dc.DrawEllipse(null, Neon.RadarBossOuter.Core, at, u * ring, u * ring);
                    dc.DrawEllipse(null, Neon.RadarBossOuter.Core, at, u * (ring - 0.5), u * (ring - 0.5));
                    dc.DrawEllipse(null, Neon.OpenArmor.Core, at, u * 0.8, u * 0.8);
                    break;
                case AgentKind.Civilian:   // a small filled grey dot
                    dc.DrawEllipse(Neon.RadarCivilian, null, at, u * 0.6, u * 0.6);
                    break;
                case AgentKind.PolicePatrol or AgentKind.PoliceCruiser or AgentKind.Interceptor:
                    PoliceMark(dc, w.Time, at, u);
                    break;
                case AgentKind.ArmoredSuv:   // the same mark, larger
                    PoliceMark(dc, w.Time, at, u * 1.4);
                    break;
                case AgentKind.Pursuer:
                    double s = pulse ? u * 1.3 : u;
                    Poly(dc, Neon.TextRed, Neon.RadarPursuer, new Point(at.X, at.Y - s), new Point(at.X + s, at.Y + s), new Point(at.X - s, at.Y + s));
                    break;
            }
        }

        foreach (var rb in w.RollingRoadblocks)   // a formed pair marks its held lanes like a parked roadblock
        {
            if (!rb.Formed) continue;
            double sum = 0;
            int n = 0;
            foreach (var suv in rb.Cars)
                if (suv.State != VehicleState.Wrecked) { sum += cam.Ahead(suv); n++; }
            if (n > 0) RoadblockMark(dc, Map, k, sum / n, rb.OpenLane, u * 1.4 + 3 * k);   // just beyond the SUVs' own squares, or they would hide it
        }

        foreach (var m in w.Missiles)   // yours: a white dot
            dc.DrawEllipse(Neon.TextWhite, null, Map(cam.Ahead(m), cam.LateralOf(m)), u * 0.45, u * 0.45);
        foreach (var m in w.EnemyMissiles)   // theirs: a red dot with a ring
        {
            var at = Map(cam.Ahead(m), cam.LateralOf(m));
            dc.DrawEllipse(Neon.TextRed, null, at, u * 0.45, u * 0.45);
            dc.DrawEllipse(null, Neon.EnemyMissile.Core, at, u * 1.0, u * 1.0);
        }

        var me = new StreamGeometry();
        using (var c = me.Open())
        {
            c.BeginFigure(new Point(px, py - 9 * k), true, true);
            c.PolyLineTo(new[] { new Point(px - 6 * k, py + 6 * k), new Point(px + 6 * k, py + 6 * k) }, true, false);
        }
        me.Freeze();
        dc.PushTransform(new RotateTransform(cam.CamHeading * 180 / Math.PI, px, py));   // the nose direction
        dc.DrawGeometry(Neon.TextGreen, null, me);
        dc.Pop();
        dc.Pop();
        l.Text(dc, "RADAR", r.X + 6 * k, r.Y + 4 * k, 13, Neon.TextGray);
    }

    static readonly Pen SmokeRim = MakeRim();

    static Pen MakeRim()
    {
        var pen = new Pen(Neon.Solid(Neon.Gray, 0.6), 1.0);
        pen.Freeze();
        return pen;
    }

    static string ItemLetter(SupplyItem item) => item switch
    {
        SupplyItem.Oil => "O", SupplyItem.Smoke => "S", SupplyItem.Caltrops => "C",
        SupplyItem.Missile => "M", SupplyItem.Strike => "★", _ => "A",
    };

    static Point[] Hexagon(Point c, double r)
    {
        var pts = new Point[6];
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 6 + i * Math.PI / 3;
            pts[i] = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
        return pts;
    }

    /// <summary>
    /// A police car: a square filled blue or red on the shared light-bar clock, with a white outline and a dark bar across it.
    /// The Blocker is a plain amber square, so the outline and bar (not the colour) tell the two apart.
    /// </summary>
    static void PoliceMark(DrawingContext dc, double time, Point at, double half)
    {
        var fill = Neon.LightPhase(time) == 0 ? Neon.LightBlueFill : Neon.LightRedFill;
        dc.DrawRectangle(fill, Neon.RadarPolice.Core, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
        dc.DrawRectangle(Neon.Black, null, new Rect(at.X - half * 0.6, at.Y - half * 0.18, half * 1.2, half * 0.36));
    }

    /// <summary>
    /// A roadblock: a red bar across each lane it holds and a green tick in the open lane. Parked and rolling roadblocks look the same.
    /// <paramref name="ahead"/> is its distance in front of you and <paramref name="gapLane"/> the open lane (an index into <see cref="Track.LaneCentres"/>); <paramref name="lift"/> moves the mark up the screen by that many pixels.
    /// </summary>
    static void RoadblockMark(DrawingContext dc, Func<double, double, Point> map, double k, double ahead, int gapLane, double lift)
    {
        Point At(double lateral) { var q = map(ahead, lateral); return new Point(q.X, q.Y - lift); }
        var bars = new LineBatch();
        for (int lane = 0; lane < Track.LaneCentres.Length; lane++)
        {
            if (lane == gapLane) continue;
            double c = Track.LaneCentres[lane];
            bars.Line(At(c - Roadblock.BarrierHalfWidth), At(c + Roadblock.BarrierHalfWidth));
        }
        bars.Draw(dc, Neon.RadarBarrier);
        var at = At(Track.LaneCentres[gapLane]);   // the tick: a check mark
        var tick = new LineBatch();
        tick.Line(new Point(at.X - 4 * k, at.Y), new Point(at.X - 1 * k, at.Y + 4 * k));
        tick.Line(new Point(at.X - 1 * k, at.Y + 4 * k), new Point(at.X + 5 * k, at.Y - 5 * k));
        tick.Draw(dc, Neon.RadarGap);
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
