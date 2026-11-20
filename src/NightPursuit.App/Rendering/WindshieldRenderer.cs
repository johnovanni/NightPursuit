using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// SPIKE: cracks in the driver's windshield. They appear and spread as the car's worst damage grows
/// (<see cref="PlayerCar.WorstDamage"/>), and stay when armor is regained; each new car gets a clean windshield and its own
/// pattern. Each impact is a chip with jagged radial cracks that lengthen and branch, then broken rings of a spider web.
/// Thin, pale and dim, so the road stays readable through them; they keep clear of the aim mark at first.
/// </summary>
internal static class WindshieldRenderer
{
    /// <summary>Damage at which each impact appears; the first is a small chip.</summary>
    static readonly double[] Thresholds = { 4, 15, 28, 42, 56, 70, 84 };
    /// <summary>Damage over which one impact grows from a chip to its full spread.</summary>
    const double GrowDamage = 30;
    static readonly Color Glass = Color.FromRgb(0xD8, 0xEC, 0xFF);

    /// <summary>One crack: points as offsets from its impact in screen widths, and when it starts growing (0 to 1 of the impact's growth).</summary>
    sealed record Crack(Vector[] Pts, double Delay);
    sealed record Impact(double Threshold, double Fx, double Fy, Crack[] Radials, Crack[] Branches, double[] RingKeep);

    static int _seed = int.MinValue;
    static Impact[] _impacts = [];

    public static void Draw(DrawingContext dc, HudLayout l, PlayerCar player)
    {
        double damage = player.WorstDamage;
        if (damage < Thresholds[0]) return;
        int seed = 17 + 7919 * player.SpareCars;   // a new car, a new pattern
        if (seed != _seed) { _impacts = Make(seed); _seed = seed; }

        double W = l.Width, H = l.Height;
        var lines = new LineBatch();
        foreach (var im in _impacts)
        {
            if (damage < im.Threshold) break;
            double t = Math.Clamp((damage - im.Threshold) / GrowDamage + 0.15, 0, 1);
            var c = new Point(im.Fx * W, im.Fy * H);
            Point At(Vector v) => c + v * W;

            // The chip: a tiny ring where the stone struck.
            double r = 0.004 * W;
            for (int k = 0; k < 6; k++)
                lines.Line(c + new Vector(Math.Cos(k * Math.PI / 3), Math.Sin(k * Math.PI / 3)) * r,
                           c + new Vector(Math.Cos((k + 1) * Math.PI / 3), Math.Sin((k + 1) * Math.PI / 3)) * r);

            foreach (var crack in im.Radials) Grow(lines, crack, t, At);
            foreach (var crack in im.Branches) Grow(lines, crack, t, At);

            // Broken rings of the web, linking neighbouring radials once the impact is well spread.
            for (int ring = 0; ring < 2; ring++)
            {
                double ringT = ring == 0 ? 0.45 : 0.8;
                int idx = ring == 0 ? 1 : 3;
                if (t < ringT) continue;
                for (int i = 0; i < im.Radials.Length; i++)
                {
                    if (im.RingKeep[ring * im.Radials.Length + i] > 0.65) continue;   // some links missing: a broken web
                    var a = im.Radials[i].Pts; var b = im.Radials[(i + 1) % im.Radials.Length].Pts;
                    if (idx < a.Length && idx < b.Length) lines.Line(At(a[idx]), At(b[idx]));
                }
            }
        }
        lines.Draw(dc, LineArt.Detail(Glass, 0.8));
    }

    /// <summary>Draws the part of <paramref name="crack"/> grown so far: whole segments, then part of the next one.</summary>
    static void Grow(LineBatch lines, Crack crack, double t, Func<Vector, Point> at)
    {
        double g = Math.Clamp((t - crack.Delay) / (1 - crack.Delay), 0, 1);
        double reach = g * (crack.Pts.Length - 1);
        for (int i = 0; i + 1 < crack.Pts.Length && i < reach; i++)
        {
            double f = Math.Min(1, reach - i);
            lines.Line(at(crack.Pts[i]), at(crack.Pts[i] + (crack.Pts[i + 1] - crack.Pts[i]) * f));
        }
    }

    static Impact[] Make(int seed)
    {
        var rng = new Random(seed);
        var list = new List<Impact>();
        foreach (double threshold in Thresholds)
        {
            double fx, fy;
            do { fx = 0.06 + rng.NextDouble() * 0.88; fy = 0.06 + rng.NextDouble() * 0.72; }
            while (Math.Abs(fx - 0.5) < 0.17 && fy is > 0.26 and < 0.64);   // impacts keep clear of the aim mark

            int n = 5 + rng.Next(4);
            var radials = new Crack[n];
            var branches = new List<Crack>();
            double turn = rng.NextDouble() * Math.Tau;
            for (int i = 0; i < n; i++)
            {
                double angle = turn + i * Math.Tau / n + (rng.NextDouble() - 0.5) * 0.6;
                int segs = 5 + rng.Next(5);
                var pts = new Vector[segs + 1];
                for (int k = 1; k <= segs; k++)
                {
                    angle += (rng.NextDouble() - 0.5) * 0.6;   // jagged
                    double len = 0.012 + rng.NextDouble() * 0.018;
                    pts[k] = pts[k - 1] + new Vector(Math.Cos(angle), Math.Sin(angle)) * len;
                }
                radials[i] = new Crack(pts, 0);

                if (rng.NextDouble() < 0.6)   // a branch off the outer half, starting later
                {
                    int from = segs / 2 + rng.Next(segs - segs / 2);
                    double ba = angle + (rng.NextDouble() < 0.5 ? -1 : 1) * (0.5 + rng.NextDouble() * 0.5);
                    int bs = 2 + rng.Next(3);
                    var bp = new Vector[bs + 1];
                    bp[0] = pts[from];
                    for (int k = 1; k <= bs; k++)
                    {
                        ba += (rng.NextDouble() - 0.5) * 0.6;
                        bp[k] = bp[k - 1] + new Vector(Math.Cos(ba), Math.Sin(ba)) * (0.01 + rng.NextDouble() * 0.014);
                    }
                    branches.Add(new Crack(bp, 0.35 + 0.3 * (double)from / segs));
                }
            }
            var keep = new double[2 * n];
            for (int i = 0; i < keep.Length; i++) keep[i] = rng.NextDouble();
            list.Add(new Impact(threshold, fx, fy, radials, branches.ToArray(), keep));
        }
        return list.ToArray();
    }
}
