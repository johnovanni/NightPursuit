using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Enemy cars seen from behind. Each kind has its own outline as well as its own colour: a sedan (Runner),
/// a tall box truck with a door line (Blocker), a low car with a spoiler (Pursuer), an orange-red fastback with a gun port
/// (Gunner), a teal van with a hatch and chute (Dropper), a steel-blue plated wedge (Armored) and the white finned boss,
/// which in a Gunner or Dropper phase also carries the gun port or a pair of chutes.
/// </summary>
public static class VehicleRenderer
{
    public const double DrawDistance = 1200;
    const double BossTagDistance = 300;

    /// <summary>
    /// Draws every visible enemy car, wreck, roadblock barrier and speed trap, far to near, so nearer black bodies hide farther
    /// things (a far car must not paint over a nearer wreck or barrier). The flat road markings are painted before this call.
    /// </summary>
    public static void DrawAll(DrawingContext dc, Projector p, World w)
    {
        var items = Items;
        items.Clear();
        foreach (var car in w.Agents) items.Add(new DrawItem(p.Ahead(car), car, null, null));
        foreach (var h in w.Hazards)
        {
            if (h is Wreck wreck) items.Add(new DrawItem(p.Ahead(wreck), null, wreck, null));
            else if (RoadsideRenderer.InRange(p, h, out double ahead)) items.Add(new DrawItem(ahead, null, null, h));
        }
        items.Sort(FarToNear);
        foreach (var item in items)
        {
            if (item.Car is not null) DrawRear(dc, p, item.Car, w.Time);
            else if (item.Wreck is not null) HazardRenderer.DrawWreck(dc, p, item.Wreck);
            else RoadsideRenderer.Draw(dc, p, item.Roadside!, w.Time);
        }
        items.Clear();   // do not keep cars alive between frames
    }

    /// <summary>One thing to draw: a car, a wreck, or (neither) a roadblock or speed trap in <c>Roadside</c>.</summary>
    readonly record struct DrawItem(double Distance, AgentCar? Car, Wreck? Wreck, Hazard? Roadside);

    static readonly List<DrawItem> Items = new();   // reused every frame (drawing happens on the UI thread only)
    static readonly Comparison<DrawItem> FarToNear = static (a, b) => b.Distance.CompareTo(a.Distance);

    public static void DrawRear(DrawingContext dc, Projector p, AgentCar car, double time)
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
        Body(dc, car, p.SpinOf(car), g, s, time, LineArt.Fade(rear));
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
        AgentKind.Civilian => Neon.CivilianCar,
        AgentKind.PolicePatrol or AgentKind.PoliceCruiser or AgentKind.Interceptor or AgentKind.ArmoredSuv => Neon.PoliceCar,
        _ => Neon.RunnerCar,
    };

    // The right half of each body outline, bottom to roof: x as a fraction of the half-width, y in metres (the left half mirrors it).
    // The rolling tread on the strip of tyre under the body (the body's bottom edge is at 0.224 m).
    const double TreadVisibleMetres = 0.224, TreadCycle = 10.5, TreadMinPixels = 7;   // 3.5 m per bar: under half a bar per 60 Hz frame up to 100 m/s
    const int TreadBars = 3;

    static readonly (double X, double Y)[] RunnerShape = { (1, 0.224), (1, 0.78), (0.78, 1.4) };
    static readonly (double X, double Y)[] BlockerShape = { (1, 0.224), (1, 1.55), (0.96, 2.1) };
    static readonly (double X, double Y)[] PursuerShape = { (1, 0.224), (1, 0.62), (0.78, 1.15) };
    static readonly (double X, double Y)[] GunnerShape = { (1, 0.224), (1, 0.7), (0.9, 0.82), (0.6, 1.3) };    // fastback: the roof slopes down to the tail
    static readonly (double X, double Y)[] DropperShape = { (1, 0.224), (1, 1.62), (0.94, 1.8) };              // a tall flat-roofed van
    static readonly (double X, double Y)[] ArmoredShape = { (1, 0.224), (1, 0.62), (0.5, 1.6) };               // a wide wedge up to a narrow flat roof
    // The boss's body and fins come from BossShapes, by its look (Fin, Twin or Dome).

    // Civilians (all 1.45 m tall): a sedan with a clear trunk shelf under a narrow cabin (roof 0.55), a hatchback whose near-full-width
    // back slopes straight up to a wide roof (0.9), a boxy flat-roofed van.
    static readonly (double X, double Y)[] CivilianSedan = { (1, 0.224), (1, 0.68), (0.62, 0.72), (0.55, 1.45) };
    static readonly (double X, double Y)[] CivilianHatch = { (1, 0.224), (1, 0.8), (0.9, 1.45) };
    static readonly (double X, double Y)[] CivilianVan = { (1, 0.224), (1, 1.38), (0.96, 1.45) };
    // Police: a sedan, a low long interceptor with a wide stance, a tall boxy armored SUV with shoulders. The last point is the roof.
    static readonly (double X, double Y)[] PoliceSedan = { (1, 0.224), (1, 0.72), (0.9, 0.8), (0.72, 1.45) };
    static readonly (double X, double Y)[] InterceptorShape = { (1, 0.224), (1, 0.58), (0.92, 0.7), (0.62, 1.25) };
    static readonly (double X, double Y)[] SuvShape = { (1, 0.224), (1, 1.5), (0.92, 1.6), (0.88, 1.9) };

    /// <summary>Police kinds carry the roof light bar.</summary>
    static bool IsPolice(AgentKind kind) => KindRules.FactionOf(kind) == Faction.Police;

    static (double X, double Y)[] ShapeOf(AgentCar car) => car.Kind switch
    {
        AgentKind.Boss => BossShapes.Body(car is BossCar b ? b.Look : BossLook.Fin),
        AgentKind.Civilian => (car.Id % 3) switch { 0 => CivilianSedan, 1 => CivilianHatch, _ => CivilianVan },
        AgentKind.PolicePatrol or AgentKind.PoliceCruiser => PoliceSedan,
        AgentKind.Interceptor => InterceptorShape,
        AgentKind.ArmoredSuv => SuvShape,
        _ => ShapeOfEnemy(car.Kind),
    };

    static (double X, double Y)[] ShapeOfEnemy(AgentKind kind) => kind switch
    {
        AgentKind.Blocker => BlockerShape,
        AgentKind.Pursuer => PursuerShape,
        AgentKind.Gunner => GunnerShape,
        AgentKind.Dropper => DropperShape,
        AgentKind.Armored => ArmoredShape,
        AgentKind.Runner => RunnerShape,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no enemy outline for this kind"),
    };

    const double PunctureWobbleDegrees = 6, PunctureWobbleHz = 1.5;   // the same 1.5 Hz sway the car steers with (World.PuncturedIntent)
    static readonly double[] PlateHeights = { 0.78, 1.1 };
    static readonly Typeface TagFont = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    static void Body(DrawingContext dc, AgentCar car, double spinAngle, Point g, double s, double time, double fade)
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
        var colour = LineArt.ColorOf(pen);
        var outlinePen = LineArt.Outline(colour, fade);
        var detailPen = LineArt.Detail(colour, fade);

        Point P(double x, double yMetres) => new(g.X + x * w / 2, g.Y - yMetres * s);

        dc.PushTransform(new RotateTransform(tilt, g.X, g.Y - roof / 2));

        double ww = Math.Max(1, 0.3 * s * widthScale);
        var tyres = new LineBatch();
        var tread = new LineBatch();
        foreach (int side in Projector.Sides)
        {
            var tyre = new Rect(g.X + side * (w / 2 - ww / 2) - ww / 2, g.Y - wheel, ww, wheel);
            // Only the tyre's outer side: the edge that faces you. No line along the road (it read as a tank's tread).
            dc.DrawRectangle(Neon.Black, null, tyre);
            double outer = side < 0 ? tyre.Left : tyre.Right;
            tyres.Line(new Point(outer, g.Y - 0.224 * s), new Point(outer, tyre.Bottom));
            // The rolling tread. Seen from behind, the back of a turning wheel moves down, so short dim bars slide down
            // the strip of tyre under the body. Their phase comes from the distance the car has driven, so they stop when it
            // stops; a bar every 3.5 m driven keeps each step under half a bar per frame even at top speed (no backward roll).
            if (TreadVisibleMetres * s >= TreadMinPixels)
            {
                double phase = car.Distance / TreadCycle;
                phase -= Math.Floor(phase);
                double inset = ww * 0.18;
                for (int k = 0; k < TreadBars; k++)
                {
                    double u = (k + phase) / TreadBars;          // 0 at the top of the strip, 1 at the road
                    u -= Math.Floor(u);
                    double y = g.Y - (TreadVisibleMetres - 0.02) * (1 - u) * s - 0.01 * s;
                    tread.Line(new Point(tyre.Left + inset, y), new Point(tyre.Right - inset, y));
                }
            }
        }

        // The boss's rear fins (or the dome's cabin) stand behind the body outline, by its look.
        var look = boss?.Look ?? BossLook.Fin;
        StreamGeometry? fins = kind == AgentKind.Boss ? BossShapes.PiecesFill(look, P) : null;

        // Body and cabin as one filled outline, so it hides the road lines behind it.
        var shape = ShapeOf(car);
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

        var edge = new List<Point>(shape.Length * 2);
        for (int i = 0; i < shape.Length; i++) edge.Add(P(-shape[i].X, shape[i].Y));       // up the left side
        for (int i = shape.Length - 1; i >= 0; i--) edge.Add(P(shape[i].X, shape[i].Y));   // across the roof and down the right
        // Light from the upper right: a gap just past the right shoulder of the roof, and a small one low on the left.
        double total = 0, toShoulder = 0;
        for (int i = 1; i < edge.Count; i++)
        {
            total += (edge[i] - edge[i - 1]).Length;
            if (i == shape.Length) toShoulder = total;
        }
        double sh = total > 0 ? toShoulder / total : 0.6;
        if (boss is { Shield: > 0 })   // a pulsing cyan open edge under the white one while it cannot be hurt
        {
            bool bright = Math.Sin(boss.Shield * 25) > 0;
            var shieldColour = LineArt.ColorOf(bright ? Neon.ShieldBright : Neon.ShieldDim);
            // Both shield pens are the same cyan; the pulse is carried by the strength (dim phase at 0.45).
            var shieldPen = LineArt.Outline(shieldColour, bright ? fade : fade * 0.45);
            // The core is three times the outline's, drawn before the body fill: the fill hides the inner half, so a cyan
            // band shows outside the white line in both phases.
            var band = new Pen(shieldPen.Core.Brush, shieldPen.Core.Thickness * 3)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            band.Freeze();
            var shieldEdge = LineArt.Polyline(edge, (0.1, 0.14), (sh + 0.02, sh + 0.07));
            dc.DrawGeometry(null, shieldPen.Halo, shieldEdge);
            dc.DrawGeometry(null, shieldPen.Glow, shieldEdge);
            dc.DrawGeometry(null, band, shieldEdge);
        }
        // Black fills hide what is behind; only the presenting edges are stroked. There is no line along the bottom of
        // the body (the road meets it there) and none along the base of a fin (the body hides it).
        if (fins is not null)
        {
            dc.DrawGeometry(Neon.Black, null, fins);
            foreach (var line in BossShapes.PieceLines(look, P)) LineArt.Draw(dc, line, outlinePen);
        }
        dc.DrawGeometry(Neon.Black, null, body);
        LineArt.Draw(dc, LineArt.Polyline(edge, (0.1, 0.14), (sh + 0.02, sh + 0.07)), outlinePen);
        tyres.Draw(dc, detailPen);
        tread.Draw(dc, LineArt.Detail(colour, fade * 0.55));

        if (kind != AgentKind.Boss)
        {
            // A short bumper line, broken in the middle and stopping short of the sides, so the open bottom of the
            // body reads as a car standing on the road rather than an arch.
            const double bumperY = 0.30;   // just above the body's bottom edge (0.224 m)
            double outer = HalfWidthAt(shape, bumperY) * 0.9, inner = 0.1;
            var bumper = new LineBatch();
            foreach (int side in Projector.Sides) bumper.Line(P(side * outer, bumperY), P(side * inner, bumperY));
            bumper.Draw(dc, outlinePen);
        }

        var detail = new LineBatch();
        switch (kind)
        {
            case AgentKind.Blocker:
                detail.Line(new Point(g.X, g.Y - 0.5 * s), new Point(g.X, g.Y - roof + 0.1 * s));   // cargo door line
                break;
            case AgentKind.Gunner:   // the rear window (open at the bottom)
                detail.Line(P(0.8, 0.9), P(0.52, 1.22));
                detail.Line(P(0.52, 1.22), P(-0.52, 1.22));
                detail.Line(P(-0.52, 1.22), P(-0.8, 0.9));
                break;
            case AgentKind.Dropper:   // the tall flat hatch and its handle
                foreach (int side in Projector.Sides) detail.Line(P(side * 0.8, 0.58), P(side * 0.8, 1.62));
                detail.Line(P(-0.8, 1.62), P(0.8, 1.62));
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
                break;
            case AgentKind.Boss:
                break;
            case AgentKind.Civilian when car.Id % 3 == 2:   // the van: two rear doors and a window line
                detail.Line(new Point(g.X, g.Y - 0.5 * s), new Point(g.X, g.Y - roof + 0.1 * s));
                detail.Line(P(-0.7, 1.12), P(-0.12, 1.12));
                detail.Line(P(0.12, 1.12), P(0.7, 1.12));
                break;
            case AgentKind.ArmoredSuv:   // a slit window under the roof, and a rear rail
                detail.Line(P(-0.7, 1.35), P(0.7, 1.35));
                detail.Line(P(-0.7, 1.62), P(0.7, 1.62));
                detail.Line(P(-0.7, 1.35), P(-0.7, 1.62));
                detail.Line(P(0.7, 1.35), P(0.7, 1.62));
                detail.Line(P(-0.9, 0.95), P(0.9, 0.95));
                break;
            default:   // Runner, Pursuer, sedans and hatchbacks: a window, and a rear spoiler on the Pursuer and Interceptor
                double bodyTop = kind switch
                {
                    AgentKind.Pursuer => 0.62, AgentKind.Interceptor => 0.58, AgentKind.PolicePatrol or AgentKind.PoliceCruiser => 0.72,
                    AgentKind.Civilian => car.Id % 3 == 0 ? 0.8 : 0.55, _ => 0.78,   // the sedan's glass starts above its trunk shelf, the hatch's low down its back
                };
                double inset = 0.22;
                double winBottom = g.Y - bodyTop * s - 0.06 * s, winTop = g.Y - roof + 0.1 * s;
                double wb = w / 2 * (1 - inset * 0.4) - 0.12 * s, wt = w / 2 * (1 - inset) - 0.12 * s;
                // The glass never pokes outside the outline: the top stays under the roof, the bottom inside the body at that height.
                wt = Math.Min(wt, shape[^1].X * w / 2 - 0.05 * s);
                wb = Math.Min(wb, HalfWidthAt(shape, bodyTop + 0.06) * w / 2 - 0.05 * s);
                detail.Line(new Point(g.X + wb, winBottom), new Point(g.X + wt, winTop));
                detail.Line(new Point(g.X + wt, winTop), new Point(g.X - wt, winTop));
                detail.Line(new Point(g.X - wt, winTop), new Point(g.X - wb, winBottom));
                if (kind is AgentKind.Pursuer or AgentKind.Interceptor)
                {
                    double y = g.Y - bodyTop * s - 0.12 * s;
                    detail.Line(new Point(g.X - w / 2 - 0.05 * s, y), new Point(g.X + w / 2 + 0.05 * s, y));
                }
                break;
        }
        detail.Draw(dc, detailPen);
        if (IsPolice(kind)) LightBar(dc, P, shape[^1], time, s, fade);

        switch (kind)
        {
            case AgentKind.Armored:   // rivets along the plate lines
                double rr = Math.Max(0.8, 0.05 * s);
                foreach (double y in PlateHeights)
                {
                    double x = WedgeHalfWidth(y) - 0.12;
                    foreach (double f in new[] { -1.0, -0.5, 0.5, 1.0 })
                        dc.DrawEllipse(Neon.Solid(colour, fade), null, P(f * x, y + 0.07), rr, rr);
                }
                break;
        }

        // The gun port and the chute. A boss carries the ones of the car its current phase drives like (D67), placed
        // to suit its long, low body: the port on the rear deck above the tail strip, a chute either side of the exhaust.
        switch (boss is null ? kind : boss.Phase.Behaviour)
        {
            case AgentKind.Gunner:
                // The gun port: a line across the window's open bottom. Dim while it rides along (or is blinded by smoke),
                // bright while it aims, and a second white line above it during the burst.
                bool aiming = GunnerDriver.IsAiming(car);
                bool firing = aiming && car.DriverState is GunnerDriver.State { Phase: GunnerPhase.Burst };
                double py = boss is null ? 0.9 : 0.84, half = firing ? 0.8 : 0.3;   // firing: the line runs the whole window bottom
                var portLine = new LineBatch();
                portLine.Line(P(-half, py), P(half, py));
                if (!aiming) portLine.Draw(dc, LineArt.Detail(LineArt.ColorOf(Neon.GunnerCar), fade * 0.5));
                else if (!firing) portLine.Draw(dc, LineArt.Outline(LineArt.ColorOf(Neon.PortGlow), fade));
                else
                {
                    portLine.Draw(dc, LineArt.Outline(LineArt.ColorOf(Neon.PortGlow), fade));
                    // The second line clears the first by at least the pen's core width plus 3 px, even at 90 m where 0.08 * s is tiny.
                    var burstPen = LineArt.Outline(LineArt.ColorOf(Neon.PortBurst), fade);
                    double gap = Math.Max(0.08 * s, burstPen.Core.Thickness + 3);
                    var (bl, br) = (P(-half, py), P(half, py));
                    var burst = new LineBatch();
                    burst.Line(new Point(bl.X, bl.Y - gap), new Point(br.X, br.Y - gap));
                    burst.Draw(dc, burstPen);
                }
                break;
            case AgentKind.Dropper:
                // The chute under the bumper: a down chevron, bright and filled while arming.
                bool arming = DropperDriver.IsArming(car);
                if (boss is null) Chute(dc, P, 0, 0.4, arming, colour, fade);
                else foreach (int side in Projector.Sides) Chute(dc, P, side * 0.5, 0.22, arming, colour, fade);
                break;
        }

        if (kind == AgentKind.Boss)
        {
            // A full-width tail strip trimmed in gold, and a central exhaust ring.
            double stripHalf = 0.94 * BossShapes.Widening(look);   // the strip is as much wider as the body is
            var strip = new Rect(P(-stripHalf, 0.66), P(stripHalf, 0.5));
            dc.DrawRectangle(Neon.TailLight, null, strip);
            LineArt.Draw(dc, new RectangleGeometry(strip), LineArt.Detail(LineArt.ColorOf(Neon.BossTrim), fade));
            LineArt.Draw(dc, new EllipseGeometry(P(0, 0.33), Math.Max(2, 0.15 * s), Math.Max(1.5, 0.1 * s)), detailPen);
            if (boss is { ArmorOpen: true })   // the open armour: a jagged line across the back
            {
                var jag = new LineBatch();
                // The x range follows the roof outline, so the teeth never poke outside it.
                var prev = P(-0.92 * BossShapes.HalfWidth(look, 0.8), 0.8);
                for (int i = 1; i <= 8; i++)
                {
                    double y = i % 2 == 1 ? 0.98 : 0.74;
                    var next = P((-1 + 2.0 * i / 8) * 0.92 * BossShapes.HalfWidth(look, y), y);
                    jag.Line(prev, next);
                    prev = next;
                }
                jag.Draw(dc, LineArt.Detail(LineArt.ColorOf(Neon.OpenArmor), fade));
            }
        }
        else
        {
            double lw = Math.Max(2, 0.34 * s * widthScale), lh = Math.Max(1.5, 0.12 * s);
            foreach (int side in Projector.Sides)
                dc.DrawRectangle(Neon.TailLight, null, new Rect(g.X + side * (w / 2 - lw * 0.7) - lw / 2, g.Y - 0.62 * s, lw, lh));
        }

        if (car.Punctured) TyreSpark(dc, car, g, w, wheel, fade);   // after the body fill, or the outline would hide it

        dc.Pop();
    }

    /// <summary>
    /// The police light bar: 60% of the roof width, in two halves that swap blue and red on <see cref="Neon.LightPhase"/>.
    /// Both halves glow. Drawn after the body fill, so the outline cannot hide it. <paramref name="roof"/> is the outline's
    /// last point (the roof's half-width fraction and height).
    /// </summary>
    static void LightBar(DrawingContext dc, Func<double, double, Point> P, (double X, double Y) roof, double time, double s, double fade)
    {
        double half = 0.6 * roof.X, top = roof.Y + Math.Max(0.12, 3.0 / s);   // at least 3 px tall at any distance
        foreach (int side in Projector.Sides)
        {
            var rect = new Rect(P(side < 0 ? -half : 0, roof.Y), P(side < 0 ? 0 : half, top));
            var pen = Neon.LightPen(time, side);
            dc.DrawRectangle(Neon.LightFill(time, side), null, rect);
            LineArt.Draw(dc, new RectangleGeometry(rect), LineArt.Detail(LineArt.ColorOf(pen), fade));
        }
    }

    /// <summary>
    /// A dropper's chute: a down chevron centred on <paramref name="x"/> (a fraction of the half-width), from just above
    /// the road to under the bumper; bright and filled while <paramref name="arming"/>.
    /// </summary>
    static void Chute(DrawingContext dc, Func<double, double, Point> P, double x, double halfWidth, bool arming, Color colour, double fade)
    {
        var chute = new StreamGeometry();
        using (var c = chute.Open())
        {
            c.BeginFigure(P(x - halfWidth, 0.42), true, true);
            c.LineTo(P(x, 0.08), true, false);
            c.LineTo(P(x + halfWidth, 0.42), true, false);
        }
        chute.Freeze();
        if (arming)
        {
            dc.DrawGeometry(Neon.ChuteFill, null, chute);
            LineArt.Draw(dc, chute, LineArt.Detail(LineArt.ColorOf(Neon.ChuteGlow), fade));
        }
        else LineArt.Draw(dc, chute, LineArt.Detail(colour, fade));
    }

    /// <summary>One reusable point buffer per outline length: drawing happens on the UI thread only, and PolyLineTo copies the points.</summary>
    static readonly Dictionary<int, Point[]> BodyBuffers = new();

    static Point[] BodyBuffer(int length)
    {
        if (!BodyBuffers.TryGetValue(length, out var buffer)) BodyBuffers[length] = buffer = new Point[length];
        return buffer;
    }

    /// <summary>A body outline's half-width (fraction of the car's half-width) at a height in metres.</summary>
    static double HalfWidthAt((double X, double Y)[] shape, double y)
    {
        for (int i = 1; i < shape.Length; i++)
            if (y <= shape[i].Y)
            {
                var (a, b) = (shape[i - 1], shape[i]);
                return a.X + (b.X - a.X) * Math.Clamp((y - a.Y) / (b.Y - a.Y), 0, 1);
            }
        return shape[^1].X;
    }

    /// <summary>The Armored wedge's half-width (fraction of the car's half-width) at a height in metres.</summary>
    static double WedgeHalfWidth(double y) => 1 - 0.5 * (y - 0.62) / (1.6 - 0.62);

    /// <summary>The "BOSS" tag above a boss, centred on x; <paramref name="y"/> is its bottom edge, set above the missile LOCK label.</summary>
    static void BossTag(DrawingContext dc, double x, double y, double s)
    {
        var ft = new FormattedText("BOSS", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TagFont,
                                   Math.Clamp(0.55 * s, 12, 30), Neon.TextGold, 1.0);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height));
    }

    /// <summary>A few sparks thrown down and out from the bare rim under one wheel; the pattern comes from the puncture time, so it is the same every frame.</summary>
    static void TyreSpark(DrawingContext dc, AgentCar car, Point g, double w, double wheel, double fade)
    {
        var sparks = new LineBatch();
        double x = g.X + car.Side * (w / 2), y = g.Y - 0.25 * wheel;
        for (int k = 0; k < 4; k++)
        {
            double f = (k * 0.37 + car.PunctureSeconds * 9) % 1;
            double a = Math.PI * (0.05 + 0.3 * f), len = Math.Max(4, wheel * (1.2 + 1.8 * ((k * 0.61 + car.PunctureSeconds * 5) % 1)));
            sparks.Line(new Point(x, y), new Point(x + Math.Cos(a) * len * car.Side, y + Math.Sin(a) * len));
        }
        sparks.Draw(dc, LineArt.Outline(Neon.Amber, fade));
    }
}
