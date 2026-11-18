using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Enemy cars seen from behind. Each kind has its own outline as well as its own colour: a sedan (Runner),
/// a tall box truck with a door line (Blocker) and a low car with a spoiler (Pursuer).
/// </summary>
public static class VehicleRenderer
{
    public const double DrawDistance = 1200;

    /// <summary>
    /// Draws every visible enemy car and every wreck, far to near, so nearer black bodies hide farther things
    /// (a far car must not paint over a nearer wreck).
    /// </summary>
    public static void DrawAll(DrawingContext dc, Projector p, World w)
    {
        var items = new List<(double Distance, Action Draw)>();
        foreach (var car in w.Agents) items.Add((car.Distance, () => DrawRear(dc, p, car, w)));
        foreach (var wreck in w.Hazards.OfType<Wreck>()) items.Add((wreck.Distance, () => HazardRenderer.DrawWreck(dc, p, w, wreck)));
        foreach (var item in items.OrderByDescending(i => i.Distance)) item.Draw();
    }

    public static void DrawRear(DrawingContext dc, Projector p, AgentCar car, World w)
    {
        // A car blown up by something other than gunfire stays in the list for a moment; the explosion covers it.
        if (car.State == VehicleState.Wrecked) return;
        double dz = car.Distance - w.Player.Distance;
        if (dz < 2 || dz > DrawDistance) return;
        double rear = dz - car.Stats.Length / 2;   // we see the back of the car, not its centre
        if (!p.ToScreen(rear, car.Lateral, 0, out var g, out double depth)) return;
        double s = p.Ppm(depth);
        double clip = p.ClipAt(rear);
        double roof = car.Stats.Height * s;
        if (clip <= 0 || g.Y - roof > clip) return;   // hidden behind a hill

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        Body(dc, car, g, s);
        if (car.Armor < car.Stats.Armor && dz < 220)
        {
            double bw = Math.Max(16, car.Stats.Width * s), frac = Math.Clamp(car.Armor / car.Stats.Armor, 0, 1);
            var r = new Rect(g.X - bw / 2, g.Y - roof - 10, bw, 4);
            dc.DrawRectangle(Neon.BarBack, null, r);
            dc.DrawRectangle(Neon.Solid(Neon.Red), null, new Rect(r.X, r.Y, bw * frac, 4));
        }
        dc.Pop();
    }

    static void Body(DrawingContext dc, AgentCar car, Point g, double s)
    {
        bool spinning = car.State == VehicleState.Spinning;
        double tilt = spinning ? Math.Sin(car.SpinAngle) * 25 : 0;
        double widthScale = spinning ? 0.65 + 0.35 * Math.Abs(Math.Cos(car.SpinAngle)) : 1;
        double w = car.Stats.Width * s * widthScale;
        double wheel = 0.28 * s;
        double bodyTop = car.Kind switch { AgentKind.Blocker => 1.55, AgentKind.Pursuer => 0.62, _ => 0.78 } * s;
        double roof = car.Stats.Height * s;
        double inset = car.Kind == AgentKind.Blocker ? 0.04 : 0.22;
        var pen = car.HitFlash > 0 ? Neon.CarFlash : car.Kind switch
        {
            AgentKind.Blocker => Neon.BlockerCar,
            AgentKind.Pursuer => Neon.PursuerCar,
            _ => Neon.RunnerCar,
        };

        dc.PushTransform(new RotateTransform(tilt, g.X, g.Y - roof / 2));

        double ww = Math.Max(1, 0.3 * s * widthScale);
        foreach (int side in new[] { -1, 1 })
            dc.DrawRectangle(Neon.Black, pen.Core, new Rect(g.X + side * (w / 2 - ww / 2) - ww / 2, g.Y - wheel, ww, wheel));

        // Body and cabin as one filled outline, so it hides the road lines behind it.
        var body = new StreamGeometry();
        using (var c = body.Open())
        {
            c.BeginFigure(new Point(g.X - w / 2, g.Y - wheel * 0.8), true, true);
            c.PolyLineTo(new[]
            {
                new Point(g.X + w / 2, g.Y - wheel * 0.8),
                new Point(g.X + w / 2, g.Y - bodyTop),
                new Point(g.X + w / 2 * (1 - inset), g.Y - roof),
                new Point(g.X - w / 2 * (1 - inset), g.Y - roof),
                new Point(g.X - w / 2, g.Y - bodyTop),
            }, true, false);
        }
        body.Freeze();
        dc.DrawGeometry(Neon.Black, pen.Glow, body);
        dc.DrawGeometry(null, pen.Core, body);

        var detail = new LineBatch();
        double bumper = g.Y - 0.45 * s;
        detail.Line(new Point(g.X - w / 2, bumper), new Point(g.X + w / 2, bumper));
        if (car.Kind == AgentKind.Blocker)
            detail.Line(new Point(g.X, g.Y - 0.5 * s), new Point(g.X, g.Y - roof + 0.1 * s));   // cargo door line
        else
        {
            double winBottom = g.Y - bodyTop - 0.06 * s, winTop = g.Y - roof + 0.1 * s;
            double wb = w / 2 * (1 - inset * 0.4) - 0.12 * s, wt = w / 2 * (1 - inset) - 0.12 * s;
            detail.Line(new Point(g.X - wb, winBottom), new Point(g.X + wb, winBottom));
            detail.Line(new Point(g.X + wb, winBottom), new Point(g.X + wt, winTop));
            detail.Line(new Point(g.X + wt, winTop), new Point(g.X - wt, winTop));
            detail.Line(new Point(g.X - wt, winTop), new Point(g.X - wb, winBottom));
        }
        if (car.Kind == AgentKind.Pursuer)   // rear spoiler
        {
            double y = g.Y - bodyTop - 0.12 * s;
            detail.Line(new Point(g.X - w / 2 - 0.05 * s, y), new Point(g.X + w / 2 + 0.05 * s, y));
        }
        detail.Draw(dc, pen);

        double lw = Math.Max(2, 0.34 * s * widthScale), lh = Math.Max(1.5, 0.12 * s);
        foreach (int side in new[] { -1, 1 })
            dc.DrawRectangle(Neon.TailLight, null, new Rect(g.X + side * (w / 2 - lw * 0.7) - lw / 2, g.Y - 0.62 * s, lw, lh));

        dc.Pop();
    }
}
