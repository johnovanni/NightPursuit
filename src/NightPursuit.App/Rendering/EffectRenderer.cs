using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>Tracers, explosions and the muzzle flash.</summary>
public static class EffectRenderer
{
    public const double StreakLength = 7;

    /// <summary>7 m amber streaks along each tracer's own heading (a bullet flies straight, not along the road).</summary>
    public static void DrawTracers(DrawingContext dc, Projector p, World w)
    {
        var batch = new LineBatch();
        foreach (var t in w.Tracers)
        {
            double dz = t.Pose.Distance - w.Player.Distance;
            if (dz < 3) continue;
            double tailD = dz - StreakLength * Math.Cos(t.Pose.Heading);
            double tailL = t.Pose.Lateral - StreakLength * Math.Sin(t.Pose.Heading);
            if (!p.ToScreen(dz, t.Pose.Lateral, 0.6, out var head, out _) || !p.ToScreen(tailD, tailL, 0.6, out var tail, out _)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0 || tail.Y > clip) continue;
            batch.Line(tail, head);
        }
        batch.Draw(dc, Neon.Tracer);
    }

    public static void DrawExplosions(DrawingContext dc, Projector p, World w, EffectList effects)
    {
        foreach (var x in effects.Explosions)
        {
            double dz = x.Distance - w.Player.Distance;
            if (dz < 3 || !p.ToScreen(dz, x.Lateral, 1, out var c, out double depth)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            double r = (1 + x.Age * 9) * p.Ppm(depth);
            var rng = new Random(x.Seed);   // seeded per explosion: the same burst every frame
            var batch = new LineBatch();
            for (int i = 0; i < 18; i++)
            {
                double a = rng.NextDouble() * Math.Tau, len = 0.5 + rng.NextDouble() * 0.5;
                var dir = new Vector(Math.Cos(a), Math.Sin(a) * 0.8);
                batch.Line(c + dir * r * 0.35 * len, c + dir * r * len);
            }
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            dc.PushOpacity(Math.Max(0, 1 - x.Age / Explosion.Life));
            batch.Draw(dc, Neon.Boom);
            dc.Pop();
            dc.Pop();
        }
    }

    /// <summary>A star burst at the gun that fired last, in screen space (the hood and guns are drawn elsewhere).</summary>
    public static void DrawMuzzleFlash(DrawingContext dc, Projector p, World w)
    {
        Tracer? fresh = null;
        foreach (var t in w.Tracers)
            if (t.OwnerId == w.Player.Id && t.Travel < 6 && (fresh is null || t.Travel < fresh.Travel)) fresh = t;
        if (fresh is null) return;

        // Which gun: the sideways offset of the new tracer from the car's centre line (the guns sit 0.7 m either side).
        double h = w.Player.Heading;
        double dx = fresh.Pose.Lateral - w.Player.Lateral, dd = fresh.Pose.Distance - w.Player.Distance;
        bool left = dx * Math.Cos(h) - dd * Math.Sin(h) < 0;

        double top = p.Height * 0.86;
        var m = new Point(p.HalfWidth + (left ? -78 : 78), top - 14);
        var flash = new LineBatch();
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3 + w.Time * 7;
            flash.Line(m, m + new Vector(Math.Cos(a), Math.Sin(a)) * 12);
        }
        flash.Draw(dc, Neon.Tracer);
    }
}
