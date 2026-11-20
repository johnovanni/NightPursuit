using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The arsenal on the road: smoke clouds, caltrops, supply crates, both kinds of missile, the satellite strike's scan
/// line and target brackets, and the missile lock. Everything that moves is placed through the <see cref="Projector"/>.
/// (Internal because <see cref="DrawCrates"/> takes the internal <see cref="HudLayout"/>.)
/// </summary>
internal static class ArsenalRenderer
{
    const double MaxDistance = 600, CrateLabelDistance = 250;

    // ---- smoke ----------------------------------------------------------------------------------------------

    const double SmokeGrowSeconds = 0.8, SmokeFadeSeconds = 1.0;

    /// <summary>The 10 puffs of a cloud, spread over its 30 x 12 m footprint: along, across, radius and height, far to near.</summary>
    static readonly (double Along, double Across, double Radius, double Up)[] Puffs = MakePuffs();

    static (double, double, double, double)[] MakePuffs()
    {
        var list = new List<(double Along, double Across, double Radius, double Up)>();
        for (int i = 0; i < 10; i++)
        {
            double a = Frac(i * 0.618034 + 0.13), b = Frac(i * 0.754878 + 0.37);
            list.Add(((a * 2 - 1) * (SmokeCloud.HalfLength - 3), (b * 2 - 1) * (SmokeCloud.HalfWidth - 1.5),
                      3.2 + 1.6 * Frac(i * 0.3819), 1 + 2 * Frac(i * 0.5701)));
        }
        list.Sort((x, y) => y.Along.CompareTo(x.Along));   // far puffs first
        return list.ToArray();
    }

    static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Each cloud is 10 soft grey ellipses, grown over its first 0.8 s and faded over its last 1 s. Call it after the cars, so a car inside fades behind it.</summary>
    public static void DrawSmoke(DrawingContext dc, Projector p, World w)
    {
        foreach (var h in w.Hazards)
        {
            if (h is not SmokeCloud s) continue;
            double dz = p.Ahead(s);
            if (dz + SmokeCloud.HalfLength < 1 || dz > MaxDistance) continue;
            double grow = Math.Clamp(s.Age / SmokeGrowSeconds, 0, 1);
            double fade = Math.Clamp((SmokeCloud.Lifetime - s.Age) / SmokeFadeSeconds, 0, 1);
            if (fade <= 0) continue;
            double scale = 0.3 + 0.7 * grow;
            dc.PushOpacity(fade);
            foreach (var (along, across, radius, up) in Puffs)
            {
                double d = dz + along * scale;
                if (!p.ToScreen(d, s.Lateral + across * scale, up * grow, out var c, out double depth)) continue;
                double clip = p.ClipAt(d);
                if (clip <= 0) continue;
                double rx = Math.Min(radius * scale * p.Ppm(depth), 0.35 * p.Height);
                if (!(rx >= 0.5)) continue;
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
                dc.DrawEllipse(Neon.SmokeFill, null, c, rx, rx * 0.7);
                dc.Pop();
            }
            dc.Pop();
        }
    }

    // ---- caltrops -------------------------------------------------------------------------------------------

    const int CaltropStars = 9;

    /// <summary>A row of small steel stars across the lane; a hostile set also gets a red dashed outline (shape as well as colour).</summary>
    public static void DrawCaltrops(DrawingContext dc, Projector p, World w)
    {
        foreach (var h in w.Hazards)
        {
            if (h is not Caltrops c) continue;
            double dz = c.Distance - p.CamDistance;
            if (dz < 3 || dz > MaxDistance) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            var stars = new LineBatch();
            for (int i = 0; i < CaltropStars; i++)
            {
                double u = (i + 0.5) / CaltropStars * 2 - 1;
                double d = dz + (i % 2 == 0 ? -0.6 : 0.6), lat = c.Lateral + u * (Caltrops.HalfWidth - 0.3);
                if (!p.ToScreen(d, lat, 0.12, out var g, out double depth)) continue;
                double r = Math.Max(2, 0.28 * p.Ppm(depth));
                // A jack: three crossing spikes.
                stars.Line(new Point(g.X - r, g.Y), new Point(g.X + r, g.Y));
                stars.Line(new Point(g.X - r * 0.7, g.Y + r * 0.7), new Point(g.X + r * 0.7, g.Y - r * 0.7));
                stars.Line(new Point(g.X - r * 0.7, g.Y - r * 0.7), new Point(g.X + r * 0.7, g.Y + r * 0.7));
            }
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            double fade = LineArt.Fade(dz);
            stars.Draw(dc, LineArt.Detail(LineArt.ColorOf(Neon.Caltrop), fade));
            if (c.Hostile
                && p.ToScreen(dz - Caltrops.HalfLength, c.Lateral - Caltrops.HalfWidth, 0, out var a, out _)
                && p.ToScreen(dz - Caltrops.HalfLength, c.Lateral + Caltrops.HalfWidth, 0, out var b, out _)
                && p.ToScreen(dz + Caltrops.HalfLength, c.Lateral + Caltrops.HalfWidth, 0, out var e, out _)
                && p.ToScreen(dz + Caltrops.HalfLength, c.Lateral - Caltrops.HalfWidth, 0, out var f, out _))
            {
                var box = LineArt.Polyline(new[] { a, b, e, f, a });
                LineArt.Draw(dc, box, LineArt.Dashed(LineArt.Outline(LineArt.ColorOf(Neon.HostileOutline), fade)));
            }
            dc.Pop();
        }
    }

    // ---- crates ---------------------------------------------------------------------------------------------

    /// <summary>A 1.6 m glowing box drawn as its near face without the base plus the top's far edge, with its label ("MISSILE +1") above it within 250 m.</summary>
    public static void DrawCrates(DrawingContext dc, Projector p, World w, HudLayout l)
    {
        const double h = SupplyCrate.Size, half = SupplyCrate.Size / 2;
        foreach (var hz in w.Hazards)
        {
            if (hz is not SupplyCrate c) continue;
            double dz = c.Distance - p.CamDistance;
            if (dz < 2 || dz > MaxDistance) continue;
            double near = dz - half, far = dz + half;
            double clip = p.ClipAt(near);
            if (clip <= 0) continue;
            if (!p.ToScreen(near, c.Lateral - half, 0, out var nbl, out _) || !p.ToScreen(near, c.Lateral + half, 0, out var nbr, out _)
                || !p.ToScreen(near, c.Lateral + half, h, out var ntr, out _) || !p.ToScreen(near, c.Lateral - half, h, out var ntl, out _)
                || !p.ToScreen(far, c.Lateral - half, 0, out var fbl, out _) || !p.ToScreen(far, c.Lateral + half, 0, out var fbr, out _)
                || !p.ToScreen(far, c.Lateral + half, h, out var ftr, out _) || !p.ToScreen(far, c.Lateral - half, h, out var ftl, out _)) continue;
            if (ntl.Y > clip) continue;   // its top is below the hill crest: hidden

            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            var face = new StreamGeometry();
            using (var g = face.Open())
            {
                g.BeginFigure(nbl, true, true);
                g.LineTo(nbr, true, false);
                g.LineTo(ntr, true, false);
                g.LineTo(ntl, true, false);
            }
            face.Freeze();
            dc.DrawGeometry(Neon.CrateFill, null, face);
            // Only the presenting edges: the near face without its base, and the top's far edge.
            var crate = LineArt.Outline(LineArt.ColorOf(Neon.Crate), LineArt.Fade(dz));
            LineArt.Draw(dc, LineArt.Polyline(new[] { nbl, ntl, ntr, nbr }), crate);
            LineArt.Draw(dc, LineArt.Polyline(new[] { ftl, ftr }), crate);
            dc.Pop();

            double top = Math.Min(ntl.Y, ftl.Y);
            if (dz <= CrateLabelDistance && top - 22 * l.K < clip)
                l.Text(dc, c.Label, (nbl.X + nbr.X) / 2, top - 22 * l.K, 15, Neon.TextCyan, TextAlignment.Center, true, scaled: false);   // a label in the world, not part of the HUD
        }
    }

    // ---- missiles -------------------------------------------------------------------------------------------

    const double DartLength = 2;

    /// <summary>Player missiles: a 2 m white dart with a flickering flame tail. Enemy missiles: a red dart with a pulsing ring.</summary>
    public static void DrawMissiles(DrawingContext dc, Projector p, World w)
    {
        foreach (var m in w.Missiles)
        {
            double dz = p.Ahead(m), lat = p.LateralOf(m), heading = p.HeadingOf(m);
            if (dz < 1 || dz > MaxDistance) continue;
            double dd = Math.Cos(heading), dl = Math.Sin(heading);
            double flick = 0.5 + 0.5 * Math.Sin(m.Travel * 5.3);   // from the distance flown, so no clock or randomness
            double flameLen = 1.2 + 1.6 * flick;
            if (!p.ToScreen(dz, lat, 0.8, out var head, out _)
                || !p.ToScreen(dz - DartLength * dd, lat - DartLength * dl, 0.8, out var tail, out _)
                || !p.ToScreen(dz - (DartLength + flameLen) * dd, lat - (DartLength + flameLen) * dl, 0.8, out var flame, out _)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            var body = new LineBatch();
            body.Line(tail, head);
            double fin = Math.Max(2, (head - tail).Length * 0.25);
            body.Line(tail, new Point(tail.X - fin, tail.Y + fin * 0.6));
            body.Line(tail, new Point(tail.X + fin, tail.Y + fin * 0.6));
            body.Draw(dc, LineArt.Outline(LineArt.ColorOf(Neon.PlayerMissile), LineArt.Fade(dz)));
            var fl = new LineBatch();
            fl.Line(tail, flame);
            fl.Line(new Point(tail.X - fin * 0.4, tail.Y), flame);
            fl.Line(new Point(tail.X + fin * 0.4, tail.Y), flame);
            fl.Draw(dc, LineArt.Detail(LineArt.ColorOf(flick > 0.5 ? Neon.FlameOuter : Neon.FlameInner), LineArt.Fade(dz)));
            dc.Pop();
        }

        foreach (var m in w.EnemyMissiles)
        {
            double dz = p.Ahead(m), lat = p.LateralOf(m);
            if (dz < 1 || dz > MaxDistance) continue;
            // It flies toward you: the nose points back along the road (forward when it was launched from behind).
            double nose = -m.Dir * DartLength / 2, tailOff = m.Dir * DartLength / 2;
            if (!p.ToScreen(dz + nose, lat, 0.8, out var head, out _) || !p.ToScreen(dz + tailOff, lat, 0.8, out var tail, out var depth)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            double pulse = 0.5 + 0.5 * Math.Sin(w.Time * 12);
            double ring = Math.Max(5, (0.8 + 0.5 * pulse) * p.Ppm(depth));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            var body = new LineBatch();
            body.Line(tail, head);
            double fin = Math.Max(3, (head - tail).Length * 0.3);
            body.Line(tail, new Point(tail.X - fin, tail.Y - fin * 0.6));
            body.Line(tail, new Point(tail.X + fin, tail.Y - fin * 0.6));
            var missilePen = LineArt.Outline(Neon.Red, LineArt.Fade(dz));
            body.Draw(dc, missilePen);
            var mid = new Point((head.X + tail.X) / 2, (head.Y + tail.Y) / 2);
            LineArt.Draw(dc, new EllipseGeometry(mid, ring, ring), missilePen);
            dc.Pop();
        }
    }

    // ---- strike and lock ------------------------------------------------------------------------------------

    /// <summary>
    /// While the satellite sweeps: a cyan scan line across the road moving from 400 m toward the car, and corner brackets
    /// on every hostile car in strike range. The line is placed from the sweep time minus the interpolated step, so it
    /// moves evenly at any refresh rate.
    /// </summary>
    public static void DrawStrike(DrawingContext dc, Projector p, World w)
    {
        var strike = w.Loadout.Strike;
        if (!strike.Targeting) return;

        double left = Math.Max(0, strike.SweepLeft - w.Alpha * World.Step);
        double d = Math.Max(2, World.StrikeAhead * left / SatelliteStrike.SweepSeconds);
        double clip = p.ClipAt(d);
        if (clip > 0)
        {
            var line = new LineBatch();
            Point? prev = null;
            for (int i = 0; i <= 8; i++)
            {
                double lat = -Track.RoadHalfWidth + 2 * Track.RoadHalfWidth * i / 8;
                if (!p.ToScreen(d, lat, 0, out var pt, out _)) { prev = null; continue; }
                if (prev is Point a) line.Line(a, pt);
                prev = pt;
            }
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            line.Draw(dc, LineArt.Scenery(LineArt.ColorOf(Neon.StrikeBeam), LineArt.Fade(d)));
            dc.Pop();
        }

        foreach (var car in w.Agents)
            if (w.InStrikeRange(car)) CarBrackets(dc, p, car, LineArt.ColorOf(Neon.StrikeBeam), 0.35);
    }

    static readonly Typeface LockFont = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>Corner brackets and "LOCK" on the car a missile would lock onto, while there is a missile to fire.</summary>
    public static void DrawLock(DrawingContext dc, Projector p, World w)
    {
        if (w.Loadout.Missiles.Charges <= 0 || w.Player.State == VehicleState.Wrecked) return;
        var target = w.FindMissileTarget();
        if (target is null) return;
        if (CarBrackets(dc, p, target, Neon.Green, 0.45) is not Rect r) return;
        // Drawn in the shaken world layer, so it sizes itself from the box rather than through HudLayout.
        var ft = new FormattedText("LOCK", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LockFont,
                                   Math.Clamp(r.Width * 0.3, 12, 20), Neon.TextGreen, 1.0);
        dc.DrawText(ft, new Point(r.X + r.Width / 2 - ft.Width / 2, r.Y - ft.Height - 2));
    }

    /// <summary>Four corner brackets around a car (arm length is <paramref name="arm"/> of the box's shorter side); returns the box, or null when hidden.</summary>
    static Rect? CarBrackets(DrawingContext dc, Projector p, AgentCar car, Color colour, double arm)
    {
        if (car.State == VehicleState.Wrecked) return null;
        double dz = p.Ahead(car);
        if (dz < 2 || dz > MaxDistance) return null;   // cars behind you are shown by the mirror and the radar (Task 16)
        double rear = dz - car.Stats.Length / 2;
        if (!p.ToScreen(rear, p.LateralOf(car), 0, out var g, out double depth)) return null;
        double s = p.Ppm(depth), clip = p.ClipAt(rear);
        double roof = car.Stats.Height * s, pad = Math.Max(4, 0.35 * s);
        if (clip <= 0 || g.Y - roof > clip) return null;   // hidden behind a hill
        double w = car.Stats.Width * s;
        var box = new Rect(g.X - w / 2 - pad, g.Y - roof - pad, w + 2 * pad, roof + 2 * pad);
        double a = Math.Min(box.Width, box.Height) * arm;
        var b = new LineBatch();
        foreach (double x in new[] { box.Left, box.Right })
            foreach (double y in new[] { box.Top, box.Bottom })
            {
                double sx = x == box.Left ? 1 : -1, sy = y == box.Top ? 1 : -1;
                b.Line(new Point(x, y), new Point(x + sx * a, y));
                b.Line(new Point(x, y), new Point(x, y + sy * a));
            }
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        b.Draw(dc, LineArt.Outline(colour, 1));
        dc.Pop();
        return box;
    }
}
