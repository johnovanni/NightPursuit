using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>Oil slicks (purple ellipses flat on the road), burning wrecks (a crumpled 3D body with flickering flames) and their smoke.</summary>
public static class HazardRenderer
{
    const double MaxDistance = 600;
    const double WreckHeight = 0.8;   // about 0.6 of a normal car

    // The crumpled top edge of the near (rear) and far (front) faces: across the width (-1..1) and height as a fraction
    // of WreckHeight. Both faces have the same number of points so roof edges can join them; the two differ, so the
    // body looks twisted and dented rather than a clean box.
    static readonly (double x, double y)[] NearTop =
        { (-1.00, 0.45), (-0.62, 0.95), (-0.15, 0.55), (0.30, 1.00), (0.78, 0.70), (1.02, 0.35) };
    static readonly (double x, double y)[] FarTop =
        { (-0.97, 0.60), (-0.55, 0.78), (-0.10, 1.00), (0.40, 0.62), (0.80, 0.92), (0.98, 0.50) };
    const int TopCount = 6;

    // A face is its bottom-left corner, the dented top edge, then its bottom-right corner. Reused scratch (UI thread only).
    static readonly Point[] NearFace = new Point[TopCount + 2], FarFace = new Point[TopCount + 2];

    // Smoke: a column of grey puffs in world space, driven by the burn time so it never needs randomness.
    const int Puffs = 8;
    const double PuffLife = 4.0, RiseSpeed = 1.5, WindSpeed = 0.7, StartRadius = 0.6, EndRadius = 3.0, MaxAlpha = 0.5;
    const double SmokeDistance = 900;   // visible from far away: it helps the WRECK AHEAD warning
    const int AlphaLevels = 10;         // opacity is quantised so the pens and brushes can be built once
    static readonly Pen[] SmokePens = MakeSmokePens();
    static readonly Brush[] SmokeFills = MakeSmokeFills();

    static Pen[] MakeSmokePens()
    {
        var pens = new Pen[AlphaLevels];
        for (int i = 0; i < pens.Length; i++)
        {
            pens[i] = new Pen(Neon.Solid(Neon.Gray, MaxAlpha * (i + 1) / AlphaLevels), 1.3);
            pens[i].Freeze();
        }
        return pens;
    }

    static Brush[] MakeSmokeFills()
    {
        var fills = new Brush[AlphaLevels];
        for (int i = 0; i < fills.Length; i++) fills[i] = Neon.Solid(Colors.Black, 0.55 * (i + 1) / AlphaLevels);
        return fills;
    }

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
        double nearD = Math.Max(dz - h.Length / 2, 1.0), farD = dz + h.Length / 2;
        double clip = p.ClipAt(nearD);
        if (clip <= 0) return;
        if (!p.ToScreen(dz, h.Lateral, 0, out var g, out double depth)) return;

        // Rear face at the near end, front face at the far end.
        double hw = h.Width / 2;
        if (!Face(p, nearD, h.Lateral, hw, NearTop, NearFace) || !Face(p, farD, h.Lateral, hw, FarTop, FarFace)) return;

        // Left side, right side and roof: filled black, so they hide the road lines behind the body.
        var shell = new StreamGeometry();
        using (var c = shell.Open())
        {
            Quad(c, NearFace[0], NearFace[1], FarFace[1], FarFace[0]);
            Quad(c, NearFace[TopCount + 1], NearFace[TopCount], FarFace[TopCount], FarFace[TopCount + 1]);
            c.BeginFigure(NearFace[1], true, true);
            for (int i = 2; i <= TopCount; i++) c.LineTo(NearFace[i], true, false);
            for (int i = TopCount; i >= 1; i--) c.LineTo(FarFace[i], true, false);
        }
        shell.Freeze();

        var near = new StreamGeometry();
        using (var c = near.Open())
        {
            c.BeginFigure(NearFace[0], true, true);
            for (int i = 1; i < NearFace.Length; i++) c.LineTo(NearFace[i], true, false);
        }
        near.Freeze();

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        dc.DrawGeometry(Neon.Black, Neon.WreckBody.Glow, shell);
        dc.DrawGeometry(null, Neon.WreckBody.Core, shell);

        // Roof ridges joining the faces, and the far face's top edge, so the length reads.
        var ridges = new LineBatch();
        for (int i = 1; i <= TopCount; i++) ridges.Line(NearFace[i], FarFace[i]);
        ridges.Draw(dc, Neon.WreckBody);

        // The rear face on top, filled, with cracks across it.
        dc.DrawGeometry(Neon.Black, Neon.WreckBody.Glow, near);
        dc.DrawGeometry(null, Neon.WreckBody.Core, near);
        var cracks = new LineBatch();
        cracks.Line(Mix(NearFace[2], NearFace[0], 0.1), Mix(NearFace[3], NearFace[0], 0.8));
        cracks.Line(Mix(NearFace[4], NearFace[TopCount + 1], 0.1), Mix(NearFace[5], NearFace[TopCount + 1], 0.8));
        cracks.Draw(dc, Neon.WreckBody);

        // Flames come up from the middle of the roof.
        double s = p.Ppm(depth);
        double roofY = p.ToScreen(dz, h.Lateral, WreckHeight * 0.85, out var roof, out _) ? roof.Y : g.Y - WreckHeight * s;
        Flames(dc, h, g.X, roofY, s, h.Width / 2 * s);
        dc.Pop();
    }

    static Point Mix(Point a, Point b, double t) => a + (b - a) * t;

    static void Quad(StreamGeometryContext c, Point a, Point b, Point d, Point e)
    {
        c.BeginFigure(a, true, true);
        c.LineTo(b, true, false);
        c.LineTo(d, true, false);
        c.LineTo(e, true, false);
    }

    /// <summary>Projects one face of the wreck at road distance <paramref name="d"/> into <paramref name="into"/>; false if any corner is behind the eye.</summary>
    static bool Face(Projector p, double d, double lateral, double hw, (double x, double y)[] top, Point[] into)
    {
        if (!p.ToScreen(d, lateral - hw, 0, out into[0], out _)) return false;
        for (int i = 0; i < top.Length; i++)
            if (!p.ToScreen(d, lateral + top[i].x * hw, top[i].y * WreckHeight, out into[i + 1], out _)) return false;
        return p.ToScreen(d, lateral + hw, 0, out into[top.Length + 1], out _);
    }

    /// <summary>Flames: heights flicker from sines of the burn time; the lane offset is the per-wreck phase.</summary>
    static void Flames(DrawingContext dc, Wreck wreck, double cx, double y0, double s, double hw)
    {
        double phase = wreck.Lateral * 7.3;
        var outer = new LineBatch();
        var inner = new LineBatch();
        const int tongues = 6;
        for (int i = 0; i < tongues; i++)
        {
            double u = (i + 0.5) / tongues * 2 - 1;            // -1..1 across the car
            double bx = cx + u * hw * 0.85;
            double flick = 0.5 + 0.5 * Math.Sin(wreck.Burn * (9 + i * 2.3) + phase + i * 1.7);
            double sway = Math.Sin(wreck.Burn * (5 + i) + phase + i) * 0.12 * s;
            double top = (0.45 + 0.95 * flick) * s;
            double baseW = hw / tongues * 0.9;
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

    /// <summary>
    /// Draws the smoke column above every burning wreck: puffs rise, drift downwind (the same sideways push for every
    /// wreck), grow and fade. A puff's age comes from the wreck's burn time and its slot, so nothing is random.
    /// Call this after the cars and wrecks, so the smoke rises in front of them.
    /// </summary>
    public static void DrawSmoke(DrawingContext dc, Projector p, World w)
    {
        foreach (var h in w.Hazards)
        {
            if (h is not Wreck wreck) continue;
            double dz = p.Ahead(wreck);
            if (dz < 2 || dz > SmokeDistance) continue;
            double clip = p.ClipAt(dz);
            if (clip <= 0) continue;
            double phase = wreck.Lateral * 1.37;   // lanes differ, so neighbouring columns do not pulse together
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
            for (int i = 0; i < Puffs; i++)
            {
                double age = Mod(wreck.Burn + phase + i * (PuffLife / Puffs), PuffLife);
                double t = age / PuffLife;
                double lateral = wreck.Lateral + WindSpeed * age + Math.Sin(age * 1.7 + i) * 0.2;
                double up = WreckHeight + RiseSpeed * age;
                if (!p.ToScreen(dz, lateral, up, out var c, out double depth)) continue;
                double r = (StartRadius + (EndRadius - StartRadius) * t) * p.Ppm(depth);
                if (!(r >= 0.5 && r < 5000)) continue;
                double fade = (1 - t) * Math.Min(1, t / 0.08);   // quick fade-in, then out over the life
                int level = Math.Clamp((int)Math.Ceiling(fade * AlphaLevels) - 1, 0, AlphaLevels - 1);
                dc.DrawEllipse(SmokeFills[level], SmokePens[level], c, r, r * 0.85);
            }
            dc.Pop();
        }
    }

    static double Mod(double x, double m) => x - Math.Floor(x / m) * m;
}
