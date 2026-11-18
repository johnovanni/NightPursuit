using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Enemy cars seen from behind. Each kind has its own outline as well as its own colour: a sedan (Runner),
/// a tall box truck with a door line (Blocker), a low car with a spoiler (Pursuer), an orange-red fastback with a gun port
/// (Gunner), a teal van with a hatch and chute (Dropper), a steel-blue plated wedge (Armored) and the white finned boss.
/// </summary>
public static class VehicleRenderer
{
    public const double DrawDistance = 1200;
    const double BossTagDistance = 300;

    /// <summary>
    /// Draws every visible enemy car and every wreck, far to near, so nearer black bodies hide farther things
    /// (a far car must not paint over a nearer wreck).
    /// </summary>
    public static void DrawAll(DrawingContext dc, Projector p, World w)
    {
        var items = Items;
        items.Clear();
        foreach (var car in w.Agents) items.Add(new DrawItem(p.Ahead(car), car, null));
        foreach (var h in w.Hazards)
            if (h is Wreck wreck) items.Add(new DrawItem(p.Ahead(wreck), null, wreck));
        items.Sort(FarToNear);
        foreach (var item in items)
        {
            if (item.Car is not null) DrawRear(dc, p, item.Car);
            else HazardRenderer.DrawWreck(dc, p, item.Wreck!);
        }
        items.Clear();   // do not keep cars alive between frames
    }

    readonly record struct DrawItem(double Distance, AgentCar? Car, Wreck? Wreck);

    static readonly List<DrawItem> Items = new();   // reused every frame (drawing happens on the UI thread only)
    static readonly Comparison<DrawItem> FarToNear = static (a, b) => b.Distance.CompareTo(a.Distance);

    public static void DrawRear(DrawingContext dc, Projector p, AgentCar car)
    {
        // A car blown up by something other than gunfire stays in the list for a moment; the explosion covers it.
        if (car.State == VehicleState.Wrecked) return;
        double dz = p.Ahead(car);
        if (dz < 2 || dz > DrawDistance) return;
        double rear = dz - car.Stats.Length / 2;   // we see the back of the car, not its centre
        if (!p.ToScreen(rear, p.LateralOf(car), 0, out var g, out double depth)) return;
        double s = p.Ppm(depth);
        double clip = p.ClipAt(rear);
        double roof = car.Stats.Height * s;
        if (clip <= 0 || g.Y - roof > clip) return;   // hidden behind a hill

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        Body(dc, car, p.SpinOf(car), g, s);
        if (car.Kind == AgentKind.Boss && dz < BossTagDistance) BossTag(dc, g.X, g.Y - roof - 34 - 0.35 * s, s);
        if (car.Armor < car.Stats.Armor && dz < 220)
        {
            double bw = Math.Max(16, car.Stats.Width * s), frac = Math.Clamp(car.Armor / car.Stats.Armor, 0, 1);
            var r = new Rect(g.X - bw / 2, g.Y - roof - 10, bw, 4);
            dc.DrawRectangle(Neon.BarBack, null, r);
            dc.DrawRectangle(Neon.BarFill, null, new Rect(r.X, r.Y, bw * frac, 4));
        }
        dc.Pop();
    }

    /// <summary>The line colour of a kind of car (shared with the mirror).</summary>
    internal static NeonPen PenFor(AgentKind kind) => kind switch
    {
        AgentKind.Blocker => Neon.BlockerCar,
        AgentKind.Pursuer => Neon.PursuerCar,
        AgentKind.Gunner => Neon.GunnerCar,
        AgentKind.Dropper => Neon.DropperCar,
        AgentKind.Armored => Neon.ArmoredCar,
        AgentKind.Boss => Neon.BossCar,
        _ => Neon.RunnerCar,
    };

    // The right half of each body outline, bottom to roof: x as a fraction of the half-width, y in metres (the left half mirrors it).
    static readonly (double X, double Y)[] RunnerShape = { (1, 0.224), (1, 0.78), (0.78, 1.4) };
    static readonly (double X, double Y)[] BlockerShape = { (1, 0.224), (1, 1.55), (0.96, 2.1) };
    static readonly (double X, double Y)[] PursuerShape = { (1, 0.224), (1, 0.62), (0.78, 1.15) };
    static readonly (double X, double Y)[] GunnerShape = { (1, 0.224), (1, 0.7), (0.9, 0.82), (0.6, 1.3) };    // fastback: the roof slopes down to the tail
    static readonly (double X, double Y)[] DropperShape = { (1, 0.224), (1, 1.62), (0.94, 1.8) };              // a tall flat-roofed van
    static readonly (double X, double Y)[] ArmoredShape = { (1, 0.224), (1, 0.62), (0.5, 1.6) };               // a wide wedge up to a narrow flat roof
    static readonly (double X, double Y)[] BossShape = { (1, 0.224), (1, 0.62), (0.88, 0.8), (0.55, 1.02) };   // long and low
    static readonly (double X, double Y)[] BossFin = { (0.92, 0.7), (0.84, 1.5), (0.68, 1.5), (0.62, 0.95) };

    static (double X, double Y)[] ShapeOf(AgentKind kind) => kind switch
    {
        AgentKind.Blocker => BlockerShape,
        AgentKind.Pursuer => PursuerShape,
        AgentKind.Gunner => GunnerShape,
        AgentKind.Dropper => DropperShape,
        AgentKind.Armored => ArmoredShape,
        AgentKind.Boss => BossShape,
        _ => RunnerShape,
    };

    const double PunctureWobbleDegrees = 6, PunctureWobbleHz = 1.5;   // the same 1.5 Hz sway the car steers with (World.PuncturedIntent)
    static readonly double[] PlateHeights = { 0.78, 1.1 };
    static readonly Typeface TagFont = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    static void Body(DrawingContext dc, AgentCar car, double spinAngle, Point g, double s)
    {
        bool spinning = car.State == VehicleState.Spinning;
        double tilt = spinning ? Math.Sin(spinAngle) * 25 : 0;
        if (car.Punctured && !spinning)
            tilt += PunctureWobbleDegrees * Math.Sin(2 * Math.PI * PunctureWobbleHz * car.PunctureSeconds);
        double widthScale = spinning ? 0.65 + 0.35 * Math.Abs(Math.Cos(spinAngle)) : 1;
        double w = car.Stats.Width * s * widthScale;
        double wheel = 0.28 * s;
        double roof = car.Stats.Height * s;
        var kind = car.Kind;
        var boss = car as BossCar;
        var pen = car.HitFlash > 0 ? (kind == AgentKind.Boss ? Neon.BossFlash : Neon.CarFlash) : PenFor(kind);

        Point P(double x, double yMetres) => new(g.X + x * w / 2, g.Y - yMetres * s);

        dc.PushTransform(new RotateTransform(tilt, g.X, g.Y - roof / 2));

        double ww = Math.Max(1, 0.3 * s * widthScale);
        foreach (int side in Projector.Sides)
            dc.DrawRectangle(Neon.Black, pen.Core, new Rect(g.X + side * (w / 2 - ww / 2) - ww / 2, g.Y - wheel, ww, wheel));

        // The boss's two tall rear fins stand behind the body outline.
        StreamGeometry? fins = null;
        if (kind == AgentKind.Boss)
        {
            fins = new StreamGeometry();
            using (var c = fins.Open())
                foreach (int side in Projector.Sides)
                {
                    c.BeginFigure(P(side * BossFin[0].X, BossFin[0].Y), true, true);
                    for (int i = 1; i < BossFin.Length; i++) c.LineTo(P(side * BossFin[i].X, BossFin[i].Y), true, false);
                }
            fins.Freeze();
        }

        // Body and cabin as one filled outline, so it hides the road lines behind it.
        var shape = ShapeOf(kind);
        var body = new StreamGeometry();
        using (var c = body.Open())
        {
            c.BeginFigure(P(-shape[0].X, shape[0].Y), true, true);
            var pts = BodyBuffer(shape.Length * 2 - 1);
            int n = 0;
            for (int i = 0; i < shape.Length; i++) pts[n++] = P(shape[i].X, shape[i].Y);
            for (int i = shape.Length - 1; i >= 1; i--) pts[n++] = P(-shape[i].X, shape[i].Y);
            c.PolyLineTo(pts, true, false);
        }
        body.Freeze();

        if (boss is { Shield: > 0 })   // a pulsing cyan outline under the white one while it cannot be hurt
        {
            var shield = Math.Sin(boss.Shield * 25) > 0 ? Neon.ShieldBright : Neon.ShieldDim;
            dc.DrawGeometry(Neon.Black, shield.Glow, body);
            dc.DrawGeometry(null, shield.Core, body);
            if (fins is not null) { dc.DrawGeometry(null, shield.Glow, fins); dc.DrawGeometry(null, shield.Core, fins); }
        }
        if (fins is not null) { dc.DrawGeometry(Neon.Black, pen.Glow, fins); dc.DrawGeometry(null, pen.Core, fins); }
        dc.DrawGeometry(Neon.Black, pen.Glow, body);
        dc.DrawGeometry(null, pen.Core, body);

        var detail = new LineBatch();
        if (kind != AgentKind.Boss)
        {
            double bumper = g.Y - 0.45 * s;
            detail.Line(new Point(g.X - w / 2, bumper), new Point(g.X + w / 2, bumper));
        }
        switch (kind)
        {
            case AgentKind.Blocker:
                detail.Line(new Point(g.X, g.Y - 0.5 * s), new Point(g.X, g.Y - roof + 0.1 * s));   // cargo door line
                break;
            case AgentKind.Gunner:   // the rear window
                detail.Line(P(-0.8, 0.9), P(0.8, 0.9));
                detail.Line(P(0.8, 0.9), P(0.52, 1.22));
                detail.Line(P(0.52, 1.22), P(-0.52, 1.22));
                detail.Line(P(-0.52, 1.22), P(-0.8, 0.9));
                break;
            case AgentKind.Dropper:   // the tall flat hatch and its handle
                foreach (int side in Projector.Sides) detail.Line(P(side * 0.8, 0.58), P(side * 0.8, 1.62));
                detail.Line(P(-0.8, 1.62), P(0.8, 1.62));
                detail.Line(P(-0.8, 0.58), P(0.8, 0.58));
                detail.Line(P(-0.12, 1.1), P(0.12, 1.1));
                break;
            case AgentKind.Armored:   // two plate lines and a narrow slit window
                foreach (double y in PlateHeights)
                {
                    double x = WedgeHalfWidth(y);
                    detail.Line(P(-x, y), P(x, y));
                }
                detail.Line(P(-0.36, 1.2), P(0.36, 1.2));
                detail.Line(P(-0.36, 1.32), P(0.36, 1.32));
                detail.Line(P(-0.36, 1.2), P(-0.36, 1.32));
                detail.Line(P(0.36, 1.2), P(0.36, 1.32));
                break;
            case AgentKind.Boss:
                break;
            default:   // Runner and Pursuer: a window, and a rear spoiler on the Pursuer
                double bodyTop = kind == AgentKind.Pursuer ? 0.62 : 0.78;
                double inset = 0.22;
                double winBottom = g.Y - bodyTop * s - 0.06 * s, winTop = g.Y - roof + 0.1 * s;
                double wb = w / 2 * (1 - inset * 0.4) - 0.12 * s, wt = w / 2 * (1 - inset) - 0.12 * s;
                detail.Line(new Point(g.X - wb, winBottom), new Point(g.X + wb, winBottom));
                detail.Line(new Point(g.X + wb, winBottom), new Point(g.X + wt, winTop));
                detail.Line(new Point(g.X + wt, winTop), new Point(g.X - wt, winTop));
                detail.Line(new Point(g.X - wt, winTop), new Point(g.X - wb, winBottom));
                if (kind == AgentKind.Pursuer)
                {
                    double y = g.Y - 0.62 * s - 0.12 * s;
                    detail.Line(new Point(g.X - w / 2 - 0.05 * s, y), new Point(g.X + w / 2 + 0.05 * s, y));
                }
                break;
        }
        detail.Draw(dc, pen);

        switch (kind)
        {
            case AgentKind.Armored:   // rivets along the plate lines
                double rr = Math.Max(0.8, 0.05 * s);
                foreach (double y in PlateHeights)
                {
                    double x = WedgeHalfWidth(y) - 0.12;
                    foreach (double f in new[] { -1.0, -0.5, 0.5, 1.0 })
                        dc.DrawEllipse(pen.Core.Brush, null, P(f * x, y + 0.07), rr, rr);
                }
                break;
            case AgentKind.Gunner:
                // The gun port: dim while it rides along, glowing while it aims, white during the burst.
                double port = Math.Max(4, 0.42 * s);
                var portBrush = car.DriverState is GunnerDriver.State { Phase: GunnerPhase.Burst } ? Neon.PortBurst
                              : GunnerDriver.IsAiming(car) ? Neon.PortGlow : Neon.PortDim;
                var pc = P(0, 1.06);
                var portRect = new Rect(pc.X - port / 2, pc.Y - port / 2, port, port);
                if (!ReferenceEquals(portBrush, Neon.PortDim)) dc.DrawRectangle(portBrush, Neon.GunnerCar.Glow, portRect);
                dc.DrawRectangle(portBrush, pen.Core, portRect);
                break;
            case AgentKind.Dropper:
                // The chute under the bumper: a down chevron, bright and filled while arming.
                var chute = new StreamGeometry();
                using (var c = chute.Open())
                {
                    c.BeginFigure(P(-0.4, 0.42), true, true);
                    c.LineTo(P(0, 0.08), true, false);
                    c.LineTo(P(0.4, 0.42), true, false);
                }
                chute.Freeze();
                if (DropperDriver.IsArming(car))
                {
                    dc.DrawGeometry(Neon.ChuteFill, Neon.ChuteGlow.Glow, chute);
                    dc.DrawGeometry(null, Neon.ChuteGlow.Core, chute);
                }
                else dc.DrawGeometry(null, pen.Core, chute);
                break;
        }

        if (kind == AgentKind.Boss)
        {
            // A full-width tail strip trimmed in gold, and a central exhaust ring.
            var strip = new Rect(P(-0.94, 0.66), P(0.94, 0.5));
            dc.DrawRectangle(Neon.TailLight, Neon.BossTrim.Core, strip);
            dc.DrawEllipse(null, pen.Core, P(0, 0.33), Math.Max(2, 0.15 * s), Math.Max(1.5, 0.1 * s));
            if (boss is { ArmorOpen: true })   // the open armour: a jagged line across the back
            {
                var jag = new LineBatch();
                // The x range follows the roof outline, so the teeth never poke outside it.
                var prev = P(-0.92 * BossHalfWidth(0.8), 0.8);
                for (int i = 1; i <= 8; i++)
                {
                    double y = i % 2 == 1 ? 0.98 : 0.74;
                    var next = P((-1 + 2.0 * i / 8) * 0.92 * BossHalfWidth(y), y);
                    jag.Line(prev, next);
                    prev = next;
                }
                jag.Draw(dc, Neon.OpenArmor);
            }
        }
        else
        {
            double lw = Math.Max(2, 0.34 * s * widthScale), lh = Math.Max(1.5, 0.12 * s);
            foreach (int side in Projector.Sides)
                dc.DrawRectangle(Neon.TailLight, null, new Rect(g.X + side * (w / 2 - lw * 0.7) - lw / 2, g.Y - 0.62 * s, lw, lh));
        }

        if (car.Punctured) TyreSpark(dc, car, g, w, wheel);   // after the body fill, or the outline would hide it

        dc.Pop();
    }

    /// <summary>One reusable point buffer per outline length: drawing happens on the UI thread only, and PolyLineTo copies the points.</summary>
    static readonly Dictionary<int, Point[]> BodyBuffers = new();

    static Point[] BodyBuffer(int length)
    {
        if (!BodyBuffers.TryGetValue(length, out var buffer)) BodyBuffers[length] = buffer = new Point[length];
        return buffer;
    }

    /// <summary>The boss body's half-width (fraction of the car's half-width) at a height in metres, read off <see cref="BossShape"/>.</summary>
    static double BossHalfWidth(double y)
    {
        for (int i = 1; i < BossShape.Length; i++)
            if (y <= BossShape[i].Y)
            {
                var (a, b) = (BossShape[i - 1], BossShape[i]);
                return a.X + (b.X - a.X) * Math.Clamp((y - a.Y) / (b.Y - a.Y), 0, 1);
            }
        return BossShape[^1].X;
    }

    /// <summary>The Armored wedge's of the half-width of the car) at a height in metres.</summary>
    static double WedgeHalfWidth(double y) => 1 - 0.5 * (y - 0.62) / (1.6 - 0.62);

    /// <summary>The "BOSS" tag above a boss, centred on x; <paramref name="y"/> is its bottom edge, set above the missile LOCK label.</summary>
    static void BossTag(DrawingContext dc, double x, double y, double s)
    {
        var ft = new FormattedText("BOSS", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TagFont,
                                   Math.Clamp(0.55 * s, 12, 30), Neon.TextGold, 1.0);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height));
    }

    /// <summary>A few sparks thrown down and out from the bare rim under one wheel; the pattern comes from the puncture time, so it is the same every frame.</summary>
    static void TyreSpark(DrawingContext dc, AgentCar car, Point g, double w, double wheel)
    {
        var sparks = new LineBatch();
        double x = g.X + car.Side * (w / 2), y = g.Y - 0.25 * wheel;
        for (int k = 0; k < 4; k++)
        {
            double f = (k * 0.37 + car.PunctureSeconds * 9) % 1;
            double a = Math.PI * (0.05 + 0.3 * f), len = Math.Max(4, wheel * (1.2 + 1.8 * ((k * 0.61 + car.PunctureSeconds * 5) % 1)));
            sparks.Line(new Point(x, y), new Point(x + Math.Cos(a) * len * car.Side, y + Math.Sin(a) * len));
        }
        sparks.Draw(dc, Neon.Tracer);
    }
}
