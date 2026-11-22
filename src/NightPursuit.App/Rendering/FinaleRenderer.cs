using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The beaten boss's crash (the finale), in the same line art as every car: the spin-out with the usual spin lean, a
/// sideways roll (the rear outline turning about its centre), or an end-over-end flip that shows each face of the car
/// while it faces you: rear, underside, front (upside down) and roof. Black fills hide every face turned away.
/// </summary>
internal static class FinaleRenderer
{
    static readonly (double From, double To)[] PanelGaps = { (0.1, 0.14), (0.6, 0.65) };

    /// <param name="g">The ground point under the middle of the car's rear, on screen.</param>
    /// <param name="s">Pixels per metre at the car.</param>
    public static void Draw(DrawingContext dc, Projector p, AgentCar car, BossFinale f, Point g, double s, double time, double fade)
    {
        var lifted = new Point(g.X, g.Y - p.Lerp(f.PrevLift, f.Lift) * s);
        double spin = p.SpinOf(car);
        // During the spin-out every crash spins; a roll or flip starts square on, where the spin has just ended.
        bool spinning = f.Choice.Kind == FinaleKind.SpinOff || f.Elapsed <= BossFinale.SpinOutSeconds;
        switch (f.Choice.Kind)
        {
            case FinaleKind.Roll when !spinning:
                double roof = car.Stats.Height * s;
                dc.PushTransform(new RotateTransform(p.Lerp(f.PrevRoll, f.Roll) * 180 / Math.PI, lifted.X, lifted.Y - roof / 2));
                VehicleRenderer.Body(dc, car, spin, lifted, s, time, fade, spinningLook: false);
                dc.Pop();
                break;
            case FinaleKind.Flip when !spinning:
                DrawFlip(dc, car, p.Lerp(f.PrevPitch, f.Pitch), lifted, s, time, fade, spin);
                break;
            default:
                VehicleRenderer.Body(dc, car, spin, lifted, s, time, fade, spinningLook: true);
                break;
        }
    }

    /// <summary>
    /// Seen from behind, the car's side profile (a box Length long and Height high) turns by <paramref name="theta"/>
    /// about its centre, the tail rising over the nose. Each face is drawn only while it faces the camera, as tall as
    /// that edge's height on screen: rear (cos &gt; 0), underside (sin &gt; 0), front upside down (cos &lt; 0), roof (sin &lt; 0).
    /// </summary>
    static void DrawFlip(DrawingContext dc, AgentCar car, double theta, Point ground, double s, double time, double fade, double spin)
    {
        double h = car.Stats.Height, len = car.Stats.Length, c = Math.Cos(theta), sn = Math.Sin(theta);
        double centre = h / 2;
        double ScreenY(double y) => ground.Y - (centre + y) * s;   // y: height in the turned profile, from its centre

        // Rear or front: the rear outline squashed to the face's height (and turned upside down for the front).
        if (Math.Abs(c) * h * s >= 1)
        {
            double z = c > 0 ? -len / 2 : len / 2;
            var foot = new Point(ground.X, ScreenY(-h / 2 * c - z * sn));
            dc.PushTransform(new ScaleTransform(1, c, foot.X, foot.Y));
            VehicleRenderer.Body(dc, car, spin, foot, s, time, fade, spinningLook: false);
            dc.Pop();
        }

        // Underside or roof: a flat panel the car's width wide, as tall as the face's length on screen.
        if (Math.Abs(sn) * len * s >= 1)
        {
            bool underside = sn > 0;
            double faceY = underside ? -h / 2 : h / 2;
            double halfW = car.Stats.Width / 2 * s;
            Point Q(double x, double zFrac) => new(ground.X + x * halfW, ScreenY(faceY * c - zFrac * len / 2 * sn));
            var look = car is BossCar b ? b.Look : BossLook.Fin;
            var colour = LineArt.ColorOf(Neon.BossCar);
            Panel(dc, Q, underside ? BossShapes.UndersideLines(look) : BossShapes.RoofLines(look), colour, fade);
        }
    }

    static void Panel(DrawingContext dc, Func<double, double, Point> q,
        IReadOnlyList<(double X1, double Z1, double X2, double Z2)> lines, Color colour, double fade)
    {
        var corners = new[] { q(-1, -1), q(1, -1), q(1, 1), q(-1, 1) };
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(corners[0], true, true);
            g.PolyLineTo(new[] { corners[1], corners[2], corners[3] }, true, false);
        }
        fill.Freeze();
        dc.DrawGeometry(Neon.Black, null, fill);
        LineArt.Draw(dc, LineArt.Polyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, PanelGaps),
                     LineArt.Outline(colour, fade));
        LineArt.Draw(dc, LineArt.Lines(lines.Select(l => (q(l.X1, l.Z1), q(l.X2, l.Z2)))), LineArt.Detail(colour, fade));
    }
}
