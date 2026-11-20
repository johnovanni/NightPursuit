using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Road;

namespace NightPursuit.App.Rendering;

/// <summary>Stars, moon and ridge line, laid out by compass angle so they turn with the road and the car's nose.</summary>
public static class SkyRenderer
{
    static readonly double[] Ridge = MakeRidge();
    static readonly (double Angle, double Up, double Size)[] Stars = MakeStars();

    static double[] MakeRidge()
    {
        var rng = new Random(11);
        var r = new double[96];
        for (int i = 0; i < r.Length; i++) r[i] = 8 + rng.NextDouble() * 34 + (i % 12 < 4 ? 18 : 0);
        return r;
    }

    static (double, double, double)[] MakeStars()
    {
        var rng = new Random(5);
        var s = new (double, double, double)[140];
        for (int i = 0; i < s.Length; i++) s[i] = (rng.NextDouble() * Math.Tau, 40 + rng.NextDouble() * 300, rng.NextDouble() < 0.15 ? 2 : 1.2);
        return s;
    }

    /// <summary>Draws the sky; the moon is skipped where it would sit under <paramref name="keepClear"/> (the top-right readouts).</summary>
    public static void Draw(DrawingContext dc, Projector p, Track track, Rect keepClear)
    {
        double heading = track.HeadingAt(p.CamDistance) + p.CamHeading;
        double ppr = p.PxPerRadian, halfW = p.HalfWidth, horizon = p.Horizon;

        foreach (var (angle, up, size) in Stars)
        {
            double a = WrapPi(angle - heading);
            if (Math.Abs(a) > 1.0) continue;
            dc.DrawRectangle(Neon.Star, null, new Rect(halfW + a * ppr, horizon - 60 - up * (p.Height / 900), size, size));
        }

        double moonA = WrapPi(0.5 - heading);
        var moon = new Point(halfW + moonA * ppr, horizon - p.Height * 0.26);
        var moonBox = new Rect(moon.X - 30, moon.Y - 30, 60, 60);
        if (Math.Abs(moonA) < 1.1 && !moonBox.IntersectsWith(keepClear))
        {
            var c = moon;
            dc.DrawEllipse(null, Neon.Moon.Glow, c, 20, 20);
            dc.DrawEllipse(null, Neon.Moon.Core, c, 20, 20);
            dc.DrawEllipse(Neon.Black, null, new Point(c.X + 8, c.Y - 4), 17, 17);   // crescent
        }

        double step = Math.Tau / Ridge.Length;
        if (LineArt.Restyled)
        {
            DrawLayeredRidges(dc, p, heading, step);
            return;
        }

        var ridge = new LineBatch();
        for (int i = 0; i < Ridge.Length; i++)
        {
            double a = WrapPi(i * step - heading);
            if (Math.Abs(a) > 1.05) continue;
            ridge.Line(new Point(halfW + a * ppr, horizon - Ridge[i]),
                       new Point(halfW + (a + step) * ppr, horizon - Ridge[(i + 1) % Ridge.Length]));
        }
        ridge.Draw(dc, Neon.Sky);
    }

    static readonly double[] FarRidge = MakeFarRidge();

    static double[] MakeFarRidge()
    {
        var rng = new Random(23);
        var r = new double[64];
        for (int i = 0; i < r.Length; i++) r[i] = 30 + rng.NextDouble() * 46 + (i % 9 < 3 ? 26 : 0);
        return r;
    }

    /// <summary>
    /// SPIKE: two ridges for depth. The far range is taller and dim; the near one is brighter and filled black below its
    /// line, so it hides the far range behind it, as real hills do.
    /// </summary>
    static void DrawLayeredRidges(DrawingContext dc, Projector p, double heading, double step)
    {
        double ppr = p.PxPerRadian, halfW = p.HalfWidth, horizon = p.Horizon;
        double scale = p.Height / 720;

        var far = new List<Point>();
        double farStep = Math.Tau / FarRidge.Length;
        for (int i = 0; i < FarRidge.Length; i++)
        {
            double a = WrapPi(i * farStep - heading * 0.92);   // a touch of parallax: the far range turns more slowly
            if (Math.Abs(a) > 1.1) continue;
            far.Add(new Point(halfW + a * ppr, horizon - FarRidge[i] * scale));
        }
        far.Sort((x, y) => x.X.CompareTo(y.X));
        if (far.Count > 1) LineArt.Draw(dc, LineArt.Polyline(far), LineArt.Scenery(Neon.Indigo, 0.35));

        var near = new List<Point>();
        for (int i = 0; i < Ridge.Length; i++)
        {
            double a = WrapPi(i * step - heading);
            if (Math.Abs(a) > 1.1) continue;
            near.Add(new Point(halfW + a * ppr, horizon - Ridge[i] * scale));
        }
        near.Sort((x, y) => x.X.CompareTo(y.X));
        if (near.Count < 2) return;
        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(near[0].X, horizon + 2), true, true);
            foreach (var pt in near) c.LineTo(pt, false, false);
            c.LineTo(new Point(near[^1].X, horizon + 2), false, false);
        }
        fill.Freeze();
        dc.DrawGeometry(Neon.Black, null, fill);
        LineArt.Draw(dc, LineArt.Polyline(near), LineArt.Scenery(Neon.Purple, 0.6));
    }

    static double WrapPi(double a)
    {
        a %= Math.Tau;
        if (a > Math.PI) a -= Math.Tau;
        if (a < -Math.PI) a += Math.Tau;
        return a;
    }
}
