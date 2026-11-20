using System.Windows;
using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>The bonnet outline, the two gun barrels and the aim mark at the screen centre; after a wreck, the crumpled bonnet with smoke and flames.</summary>
internal static class HoodRenderer
{
    public static void Draw(DrawingContext dc, HudLayout l, Projector p) => Draw(dc, l, p, Breakdown.None);

    public static void Draw(DrawingContext dc, HudLayout l, Projector p, Breakdown breakdown)
    {
        double top = l.HoodTop, hw = l.HalfWidth, k = l.K, W = l.Width;
        double c = breakdown.Crumple;

        // The bonnet's top line. SPIKE: after a wreck it buckles: extra points on the two sloping lines are pushed up and
        // down (at c = 0 they lie on the straight lines, so the intact bonnet is unchanged).
        double Straight(double x) => x <= hw
            ? top + 10 * k * (hw - x) / (hw - W * 0.2)
            : top + 10 * k * (x - hw) / (W * 0.8 - hw);
        Point Buckle(double fx, double up) => new(W * fx, Straight(W * fx) - up * k * c);
        var line = new[]
        {
            new Point(-5, top + 46 * k), new Point(W * 0.2, top + 10 * k),
            Buckle(0.30, 12), Buckle(0.36, -7), new Point(hw, top + 3 * k * c), Buckle(0.63, 9), Buckle(0.69, -5),
            new Point(W * 0.8, top + 10 * k), new Point(W + 5, top + 46 * k),
        };
        var hood = new StreamGeometry();
        using (var g = hood.Open())
        {
            g.BeginFigure(new Point(-5, l.Height + 5), true, true);
            g.PolyLineTo(line, true, false);
            g.LineTo(new Point(W + 5, l.Height + 5), true, false);
        }
        hood.Freeze();
        dc.DrawGeometry(Neon.Black, Neon.Hood.Glow, hood);
        dc.DrawGeometry(null, Neon.Hood.Core, hood);

        var guns = new LineBatch();
        foreach (int side in Projector.Sides)
        {
            var root = new Point(hw + side * 95 * k, top + 6 * k);
            var tip = new Point(hw + side * 78 * k, top - 12 * k);
            if (side > 0 && c > 0)
            {
                // The right barrel bends down at its middle.
                var mid = root + (tip - root) * 0.5;
                var bent = mid + new Vector(-8.5 * k * (1 - 0.4 * c), -9 * k + 15 * k * c);
                guns.Line(root, mid);
                guns.Line(mid, bent);
            }
            else guns.Line(root, tip);
        }
        guns.Draw(dc, Neon.Gun);

        if (breakdown.Active)
        {
            DrawSmoke(dc, l, breakdown.Seconds);
            DrawFlames(dc, l, line[2], breakdown.Seconds, 0);
            DrawFlames(dc, l, line[5], breakdown.Seconds, 1.7);
            return;   // the guns are dead: no aim mark
        }

        // Aim mark: where the tracers will be 60 m ahead. The nose is always at the screen centre.
        double aimY = l.Height * 0.44 - (p.View.At(60).Y - Projector.CamHeight + 0.6) * hw * Projector.Focal / 60;
        var aim = new Point(hw, aimY);
        var cross = new LineBatch();
        cross.Line(aim + new Vector(-14 * k, 0), aim + new Vector(-5 * k, 0));
        cross.Line(aim + new Vector(5 * k, 0), aim + new Vector(14 * k, 0));
        cross.Line(aim + new Vector(0, -10 * k), aim + new Vector(0, -4 * k));
        cross.Draw(dc, Neon.Hood);
    }

    const int Puffs = 16;
    const double PuffLife = 2.6;

    /// <summary>SPIKE: grey puffs rolling up from under the bonnet, growing and fading as they rise. A fixed set, so no state.</summary>
    static void DrawSmoke(DrawingContext dc, HudLayout l, double t)
    {
        double ramp = Math.Clamp(t / 0.5, 0, 1);
        for (int i = 0; i < Puffs; i++)
        {
            double birth = i * PuffLife / Puffs * 1.1;
            if (t < birth) continue;
            double age = (t - birth) % PuffLife, life = age / PuffLife;
            double hash = Frac(Math.Sin(i * 12.9898) * 43758.5453);
            double x = l.Width * (0.22 + 0.56 * hash) + Math.Sin(t * 0.9 + i) * l.Width * 0.01;
            double y = l.HoodTop + 4 * l.K - age * l.Height * 0.13;
            double r = l.Width * (0.018 + 0.035 * life);
            double a = 0.2 * ramp * (1 - life) * Math.Min(1, age / 0.3);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * a), 0x9A, 0xA0, 0xA8));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), r, r * 0.75);
        }
    }

    /// <summary>SPIKE: three small flame tongues at a buckle in the bonnet; their heights sway slowly (no on/off flicker).</summary>
    static void DrawFlames(DrawingContext dc, HudLayout l, Point at, double t, double phase)
    {
        double k = l.K, grow = Math.Clamp((t - 0.2) / 0.6, 0, 1);
        if (grow <= 0) return;
        var outer = new LineBatch();
        var inner = new LineBatch();
        for (int j = -1; j <= 1; j++)
        {
            double h = grow * k * (j == 0 ? 22 : 13) * (0.75 + 0.25 * Math.Sin(t * 6.5 + phase + j * 2.1));
            var b = at + new Vector(j * 8 * k, 1 * k);
            var tip = b + new Vector(Math.Sin(t * 3 + j) * 2 * k, -h);
            outer.Line(b + new Vector(-4 * k, 0), tip);
            outer.Line(tip, b + new Vector(4 * k, 0));
            inner.Line(b + new Vector(-1.5 * k, 0), b + new Vector(0, -h * 0.55));
            inner.Line(b + new Vector(0, -h * 0.55), b + new Vector(1.5 * k, 0));
        }
        outer.Draw(dc, LineArt.Outline(Neon.Orange, 1));
        inner.Draw(dc, LineArt.Detail(Neon.Amber, 1));
    }

    static double Frac(double v) => v - Math.Floor(v);
}
