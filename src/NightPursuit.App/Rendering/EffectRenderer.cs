using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>Tracers, explosions and the muzzle flash.</summary>
public static class EffectRenderer
{
    public const double StreakLength = 7;

    /// <summary>7 m amber streaks along each tracer's own heading (a bullet flies straight, not along the road), hidden behind hill crests.</summary>
    public static void DrawTracers(DrawingContext dc, Projector p, World w)
    {
        foreach (var t in w.Tracers)
        {
            double dz = p.Ahead(t);
            if (dz < 3) continue;
            double lateral = p.Lerp(t.PrevLateral, t.Pose.Lateral), heading = p.Lerp(t.PrevHeading, t.Pose.Heading);
            double tailD = dz - StreakLength * Math.Cos(heading);
            double tailL = lateral - StreakLength * Math.Sin(heading);
            if (!p.ToScreen(dz, lateral, 0.6, out var head, out _) || !p.ToScreen(tailD, tailL, 0.6, out var tail, out _)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            var batch = new LineBatch();
            batch.Line(tail, head);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            batch.Draw(dc, LineArt.Outline(t.Hostile ? LineArt.ColorOf(Neon.EnemyTracer) : Neon.Amber, LineArt.Fade(dz)));
            dc.Pop();
        }
    }

    static readonly Brush BeamFill = Neon.Solid(Color.FromRgb(0x9F, 0xF6, 0xFF), 0.22);

    /// <summary>A satellite strike's column of light: a glow from the spot on the road up to the top of the screen, fading over its life.</summary>
    public static void DrawBeams(DrawingContext dc, Projector p, EffectList effects, Rect exclude = default)
    {
        if (effects.Beams.Count == 0) return;
        bool hasExclude = !exclude.IsEmpty && exclude.Width > 0;
        if (hasExclude)   // keep the column off the rear-view mirror
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-100, -100, p.Width + 200, p.Height + 200)), new RectangleGeometry(exclude)));
        foreach (var b in effects.Beams)
        {
            double dz = b.Distance - p.CamDistance;
            if (dz < 3 || !p.ToScreen(dz, b.Lateral, 0, out var g, out double depth) || g.Y < -60) continue;   // g.Y < -60: the foot is above the beam's top, nothing to draw
            double half = 1.4 * p.Ppm(depth);
            dc.PushOpacity(Math.Max(0, 1 - b.Age / Beam.Life));
            dc.DrawRectangle(BeamFill, null, new Rect(g.X - half, -60, half * 2, g.Y + 60));
            var core = new LineBatch();
            core.Line(new Point(g.X, g.Y), new Point(g.X, -60));
            core.Draw(dc, LineArt.Scenery(LineArt.ColorOf(Neon.StrikeBeam), LineArt.Fade(dz)));
            dc.Pop();
        }
        if (hasExclude) dc.Pop();
    }

    public static void DrawExplosions(DrawingContext dc, Projector p, World w, EffectList effects)
    {
        foreach (var x in effects.Explosions)
        {
            double dz = x.Distance - p.CamDistance;
            if (dz < 3 || !p.ToScreen(dz, x.Lateral, 1, out var c, out double depth)) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            double r = (1 + x.Age * 9) * p.Ppm(depth);
            var batch = new LineBatch();
            foreach (var (dir, len) in x.Rays)
                batch.Line(c + dir * r * 0.35 * len, c + dir * r * len);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            dc.PushOpacity(Math.Max(0, 1 - x.Age / Explosion.Life));
            batch.Draw(dc, LineArt.Outline(Neon.Orange, LineArt.Fade(dz)));
            dc.Pop();
            dc.Pop();
        }
    }

    /// <summary>
    /// A star burst at the gun that fired last, in screen space (drawn with the hood). It stays for a minimum
    /// of world time after the shot so a slow frame rate cannot skip it.
    /// </summary>
    internal static void DrawMuzzleFlash(DrawingContext dc, HudLayout l, World w, EffectList effects)
    {
        double age = w.Time - effects.LastShotTime;
        if (age < 0 || age > EffectList.MuzzleFlashSeconds) return;

        double k = l.K;
        var m = new Point(l.HalfWidth + (effects.LastShotLeft ? -78 : 78) * k, l.HoodTop - 14 * k);
        var flash = new LineBatch();
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3 + w.Time * 7;
            flash.Line(m, m + new Vector(Math.Cos(a), Math.Sin(a)) * 12 * k);
        }
        flash.Draw(dc, LineArt.Outline(Neon.Amber, 1));
    }
}
