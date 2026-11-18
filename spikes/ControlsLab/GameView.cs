using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ControlsLab;

/// <summary>A pen pair for the glowing vector look: a wide faint stroke under a thin bright one.</summary>
internal sealed record NeonPen(Pen Glow, Pen Core);

/// <summary>Collects many line segments into one geometry so a frame needs only a few draw calls.</summary>
internal sealed class LineBatch
{
    readonly StreamGeometry _geo = new();
    readonly StreamGeometryContext _ctx;
    bool _any;

    public LineBatch() => _ctx = _geo.Open();

    public void Line(Point a, Point b)
    {
        const double Far = 50_000;   // wildly off-screen points (road swinging past the camera) are skipped
        if (!(Math.Abs(a.X) < Far && Math.Abs(a.Y) < Far && Math.Abs(b.X) < Far && Math.Abs(b.Y) < Far)) return;
        _ctx.BeginFigure(a, false, false);
        _ctx.LineTo(b, true, false);
        _any = true;
    }

    public void Draw(DrawingContext dc, NeonPen pen)
    {
        _ctx.Close();
        if (!_any) return;
        _geo.Freeze();
        dc.DrawGeometry(null, pen.Glow, _geo);
        dc.DrawGeometry(null, pen.Core, _geo);
    }
}

/// <summary>Draws the first-person vector view, rear mirror, radar and HUD.</summary>
public sealed class GameView : FrameworkElement
{
    public World? World { get; set; }

    const double CamHeight = 1.15;
    const double DrawDistance = 1500;

    static readonly Color Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
    static readonly Color Blue = Color.FromRgb(0x3D, 0x7B, 0xFF);
    static readonly Color Green = Color.FromRgb(0x39, 0xFF, 0x88);
    static readonly Color Amber = Color.FromRgb(0xFF, 0xC1, 0x3B);
    static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x3B);
    static readonly Color Magenta = Color.FromRgb(0xFF, 0x4D, 0xE1);
    static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x1F);
    static readonly Color Purple = Color.FromRgb(0xB0, 0x6B, 0xFF);
    static readonly Color Indigo = Color.FromRgb(0x5A, 0x4F, 0xD0);
    static readonly Color White = Color.FromRgb(0xEE, 0xF4, 0xFF);
    static readonly Color Gray = Color.FromRgb(0x9A, 0xA8, 0xB8);

    static readonly NeonPen RoadNear = Neon(Cyan), RoadMid = Neon(Cyan, 0.55), RoadFar = Neon(Cyan, 0.25);
    static readonly NeonPen LaneNear = Neon(Blue, 0.9, 1.3), LaneMid = Neon(Blue, 0.5, 1.0), LaneFar = Neon(Blue, 0.22, 1.0);
    static readonly NeonPen PostPen = Neon(Gray, 0.6, 1.2), LampPen = Neon(Amber, 0.9, 2.2);
    static readonly NeonPen SkyPen = Neon(Indigo, 0.8, 1.2), MoonPen = Neon(White, 0.6, 1.2);
    static readonly NeonPen HoodPen = Neon(Green, 0.8, 1.6), GunPen = Neon(Green, 1, 3);
    static readonly NeonPen RunnerPen = Neon(Magenta), BlockerPen = Neon(Amber), PursuerPen = Neon(Red), FlashPen = Neon(White, 1, 2.4);
    static readonly NeonPen BulletPen = Neon(Amber, 1, 2), OilPen = Neon(Purple, 0.9, 1.4), BoomPen = Neon(Orange, 1, 2);
    static readonly NeonPen HudPen = Neon(Cyan, 0.8, 1.2), RadarPen = Neon(Green, 0.55, 1.0), MirrorRoad = Neon(Cyan, 0.6, 1.0);

    static readonly Brush BlackBrush = Solid(Colors.Black), TailLight = Solid(Red), HeadLight = Solid(White);
    static readonly Brush TextCyan = Solid(Cyan), TextGray = Solid(Gray), TextAmber = Solid(Amber), TextRed = Solid(Red);
    static readonly Brush TextGreen = Solid(Green), TextWhite = Solid(White), StarBrush = Solid(White, 0.55), TextMagenta = Solid(Magenta);
    static readonly Brush ThrottleFill = Solid(Green, 0.85), BrakeFill = Solid(Red, 0.85), BarBack = Solid(Cyan, 0.08);
    static readonly Brush OilFill = Solid(Purple, 0.18), PanelFill = Solid(Color.FromRgb(4, 8, 14), 0.88);
    static readonly Brush RadarFill = Solid(Color.FromRgb(2, 14, 8), 0.85), HitBrush = Solid(Red, 0.5);

    static readonly Typeface Bold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Typeface Regular = new("Consolas");

    // Sky: ridge heights and stars, laid out by compass angle so they turn with the road.
    static readonly double[] Ridge = MakeRidge();
    static readonly (double Angle, double Up, double Size)[] Stars = MakeStars();

    double _w, _h, _halfW, _horizon, _dpi = 1, _firstD;
    double[] _clip = new double[320];
    int _clipCount;

    static NeonPen Neon(Color c, double alpha = 1, double width = 1.6)
    {
        var glow = new Pen(Solid(c, 0.27 * alpha), width + 4)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var core = new Pen(Solid(c, alpha), width)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        glow.Freeze();
        core.Freeze();
        return new NeonPen(glow, core);
    }

    static SolidColorBrush Solid(Color c, double alpha = 1)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

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

    // ---------------------------------------------------------------- projection

    // 70° horizontal field of view.
    static readonly double Focal = 1 / Math.Tan(35 * Math.PI / 180);

    double Ppm(double dz) => _halfW * Focal / dz;   // pixels per metre at distance dz

    Point Project(double relX, double relY, double dz)
    {
        double s = Ppm(dz);
        return new Point(_halfW + relX * s, _horizon - relY * s);
    }

    double PxPerRadian => _halfW * Focal;

    /// <summary>
    /// Screen position of a point <paramref name="d"/> m along the road, <paramref name="lateral"/> m right of
    /// its centre and <paramref name="up"/> m above it. False when the point is behind the camera.
    /// </summary>
    bool ToScreen(World world, double d, double lateral, double up, out Point pt, out double depth)
    {
        var r = world.View.At(d, lateral);
        var (x, f) = ToCar(world, r.X - world.Player.X, r.F);
        depth = f;
        if (depth < 0.8) { pt = default; return false; }
        pt = Project(x, r.Y - CamHeight + up, depth);
        return true;
    }

    /// <summary>Rotates a road-frame offset into the car's frame: the camera turns with the car's nose.</summary>
    static (double X, double F) ToCar(World world, double x, double f)
    {
        double c = Math.Cos(world.Player.Heading), s = Math.Sin(world.Player.Heading);
        return (x * c - f * s, x * s + f * c);
    }

    double ClipAt(double dz)
    {
        if (_clipCount == 0) return _h;
        int i = (int)Math.Ceiling((dz - _firstD) / Track.SegmentLength);
        return _clip[Math.Clamp(i, 0, _clipCount - 1)];
    }

    // ---------------------------------------------------------------- frame

    protected override void OnRender(DrawingContext dc)
    {
        var world = World;
        _w = ActualWidth; _h = ActualHeight;
        if (world is null || _w < 50 || _h < 50) return;
        _halfW = _w / 2;
        _horizon = _h * 0.44;
        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var p = world.Player;

        dc.DrawRectangle(BlackBrush, null, new Rect(0, 0, _w, _h));

        double shake = p.Shake * 14;
        dc.PushTransform(new TranslateTransform(Math.Sin(world.Time * 61) * shake, Math.Cos(world.Time * 53) * shake));
        DrawSky(dc, world);
        DrawRoad(dc, world);
        DrawSlicks(dc, world);
        DrawEnemies(dc, world);
        DrawBullets(dc, world);
        DrawExplosions(dc, world);
        dc.Pop();

        DrawHood(dc, world);
        DrawHud(dc, world);
        DrawMirror(dc, world);
        DrawRadar(dc, world);
        DrawWarnings(dc, world);
        DrawMessages(dc, world);
        if (p.HitFlash > 0)
            dc.DrawRectangle(null, new Pen(HitBrush, 18 * Math.Min(1, p.HitFlash / 0.35)), new Rect(0, 0, _w, _h));
        if (world.ShowHelp) DrawHelp(dc, world);
        if (world.Paused) DrawCentered(dc, "PAUSED  (P to resume)", _h * 0.36, 30, TextAmber);
        if (p.Wrecked) DrawCentered(dc, "WRECKED", _h * 0.30, 44, TextRed);
        else if (p.OffRoad)   // the rumble shake must have a visible cause
            DrawCentered(dc, p.X < 0 ? "<< OFF ROAD" : "OFF ROAD >>", _h * 0.62, 26, TextAmber);

        // Frame-rate readout (smoothed), to spot a slow renderer.
        double now = _frameClock.Elapsed.TotalSeconds, frame = now - _lastFrame;
        _lastFrame = now;
        if (frame > 0 && frame < 1) _fps = _fps * 0.95 + 0.05 / frame;
        Text(dc, $"{_fps:0} FPS", 16, _h - 22, 12, TextGray);
    }

    readonly System.Diagnostics.Stopwatch _frameClock = System.Diagnostics.Stopwatch.StartNew();
    double _lastFrame, _fps = 60;

    // ---------------------------------------------------------------- world

    void DrawSky(DrawingContext dc, World world)
    {
        double heading = world.Track.HeadingAt(world.Player.Z) + world.Player.Heading;
        double ppr = PxPerRadian;

        foreach (var (angle, up, size) in Stars)
        {
            double a = WrapPi(angle - heading);
            if (Math.Abs(a) > 1.0) continue;
            dc.DrawRectangle(StarBrush, null, new Rect(_halfW + a * ppr, _horizon - 60 - up * (_h / 900), size, size));
        }

        double moonA = WrapPi(0.5 - heading);
        if (Math.Abs(moonA) < 1.1)
        {
            var c = new Point(_halfW + moonA * ppr, _horizon - _h * 0.26);
            dc.DrawEllipse(null, MoonPen.Glow, c, 20, 20);
            dc.DrawEllipse(null, MoonPen.Core, c, 20, 20);
            dc.DrawEllipse(BlackBrush, null, new Point(c.X + 8, c.Y - 4), 17, 17);
        }

        var ridge = new LineBatch();
        double step = Math.Tau / Ridge.Length;
        for (int i = 0; i < Ridge.Length; i++)
        {
            double a = WrapPi(i * step - heading);
            if (Math.Abs(a) > 1.05) continue;
            var p1 = new Point(_halfW + a * ppr, _horizon - Ridge[i]);
            var p2 = new Point(_halfW + (a + step) * ppr, _horizon - Ridge[(i + 1) % Ridge.Length]);
            ridge.Line(p1, p2);
        }
        ridge.Draw(dc, SkyPen);
    }

    static double WrapPi(double a)
    {
        a %= Math.Tau;
        if (a > Math.PI) a -= Math.Tau;
        if (a < -Math.PI) a += Math.Tau;
        return a;
    }

    void DrawRoad(DrawingContext dc, World world)
    {
        var p = world.Player;
        var view = world.View;
        const double seg = Track.SegmentLength;
        double first = Math.Ceiling((p.Z + 1.5) / seg) * seg;   // samples sit on world segment boundaries
        _firstD = first - p.Z;
        int n = (int)(DrawDistance / seg);
        if (_clip.Length < n + 1) _clip = new double[n + 1];
        _clipCount = n + 1;

        LineBatch[] edges = { new(), new(), new() };
        LineBatch[] lanes = { new(), new(), new() };
        var posts = new LineBatch();
        var lamps = new LineBatch();

        double[] lat = { -Track.RoadHalfWidth, -Track.LaneLine, Track.LaneLine, Track.RoadHalfWidth };
        var prev = new Point[4];
        var cur = new Point[4];
        double maxY = _h, prevY = 0;
        bool prevOk = false;

        for (int i = 0; i <= n; i++)
        {
            double d = _firstD + i * seg;
            _clip[i] = maxY;
            bool ok = ToScreen(world, d, 0, 0, out var centre, out double depth);
            for (int k = 0; k < 4 && ok; k++) ok = ToScreen(world, d, lat[k], 0, out cur[k], out _);
            double sy = centre.Y;

            if (ok && prevOk && sy < maxY)
            {
                // Coming out from behind a crest: start the line where it emerges.
                double t = prevY > maxY ? (maxY - prevY) / (sy - prevY) : 0;
                int band = d < 250 ? 0 : d < 700 ? 1 : 2;
                Point From(int k) => prev[k] + (cur[k] - prev[k]) * t;

                edges[band].Line(From(0), cur[0]);
                edges[band].Line(From(3), cur[3]);
                long index = (long)Math.Round((p.Z + d) / seg);
                if (index % 4 < 2)
                {
                    lanes[band].Line(From(1), cur[1]);
                    lanes[band].Line(From(2), cur[2]);
                }
                if (index % 8 == 0 && d < 900)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        double px = side * (Track.RoadHalfWidth + 2.2);
                        if (!ToScreen(world, d, px, 0, out var foot, out _)) continue;
                        ToScreen(world, d, px, 1.1, out var top, out _);
                        posts.Line(foot, top);
                        if (index % 32 == 0 && ToScreen(world, d, px, 5.5, out var lamp, out _)
                                            && ToScreen(world, d, px - side * 1.2, 5.6, out var arm, out _))
                        {
                            posts.Line(foot, lamp);
                            lamps.Line(lamp, arm);
                        }
                    }
                }
            }
            if (ok)
            {
                // Only road still heading away from us can hide what lies beyond it. Where a tight bend
                // swings the road sideways it projects far off-screen and must not black out the rest.
                bool headingAway = depth >= 0.3 * d;
                if (headingAway && (i == 0 || (prevOk && sy < maxY))) maxY = Math.Max(0, Math.Min(maxY, sy));
                (prev, cur) = (cur, prev);
                prevY = sy;
            }
            prevOk = ok;
        }

        posts.Draw(dc, PostPen);
        lamps.Draw(dc, LampPen);
        lanes[2].Draw(dc, LaneFar); lanes[1].Draw(dc, LaneMid); lanes[0].Draw(dc, LaneNear);
        edges[2].Draw(dc, RoadFar); edges[1].Draw(dc, RoadMid); edges[0].Draw(dc, RoadNear);
    }

    void DrawSlicks(DrawingContext dc, World world)
    {
        var p = world.Player;
        foreach (var o in world.Slicks)
        {
            double dz = o.Z - p.Z;
            if (dz < 3 || dz > 600) continue;
            if (!ToScreen(world, dz, o.X, 0, out var c, out double depth)
                || !ToScreen(world, dz - o.Radius, o.X, 0, out var near, out _)
                || !ToScreen(world, dz + o.Radius, o.X, 0, out var far, out _)) continue;
            double rx = o.Radius * Ppm(depth);
            double ry = Math.Abs(near.Y - far.Y) / 2;
            double clip = ClipAt(dz);
            if (clip <= 0) continue;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, _w, clip)));
            dc.DrawEllipse(OilFill, OilPen.Glow, c, rx, Math.Max(1, ry));
            dc.DrawEllipse(null, OilPen.Core, c, rx, Math.Max(1, ry));
            dc.Pop();
        }
    }

    void DrawEnemies(DrawingContext dc, World world)
    {
        var p = world.Player;
        foreach (var e in world.Enemies.Where(e => e.Z - p.Z > 2).OrderByDescending(e => e.Z))
        {
            double dz = e.Z - p.Z;
            if (dz > DrawDistance || e.State == EnemyState.Wrecked) continue;
            if (!ToScreen(world, dz, e.X, 0, out var g, out double depth)) continue;
            double s = Ppm(depth);
            double clip = ClipAt(dz);
            if (clip <= 0 || g.Y - e.Height * s > clip) continue;   // hidden behind a hill

            dc.PushClip(new RectangleGeometry(new Rect(0, 0, _w, clip)));
            DrawCarRear(dc, e, g, s);
            if (e.Health < e.MaxHealth && dz < 220)
            {
                double bw = Math.Max(16, e.Width * s), frac = Math.Max(0, e.Health / e.MaxHealth);
                var r = new Rect(g.X - bw / 2, g.Y - e.Height * s - 10, bw, 4);
                dc.DrawRectangle(BarBack, null, r);
                dc.DrawRectangle(TextRed, null, new Rect(r.X, r.Y, bw * frac, 4));
            }
            dc.Pop();
        }
    }

    void DrawCarRear(DrawingContext dc, EnemyCar e, Point g, double s)
    {
        bool spinning = e.State == EnemyState.Spinning;
        double tilt = spinning ? Math.Sin(e.SpinAngle) * 25 : 0;
        double widthScale = spinning ? 0.65 + 0.35 * Math.Abs(Math.Cos(e.SpinAngle)) : 1;
        double w = e.Width * s * widthScale;
        double wheel = 0.28 * s;
        double bodyTop = (e.Kind switch { EnemyKind.Blocker => 1.55, EnemyKind.Pursuer => 0.62, _ => 0.78 }) * s;
        double roof = e.Height * s;
        double inset = e.Kind == EnemyKind.Blocker ? 0.04 : 0.22;
        var pen = e.HitFlash > 0 ? FlashPen : e.Kind switch
        {
            EnemyKind.Blocker => BlockerPen,
            EnemyKind.Pursuer => PursuerPen,
            _ => RunnerPen,
        };

        dc.PushTransform(new RotateTransform(tilt, g.X, g.Y - roof / 2));

        // Wheels
        double ww = Math.Max(1, 0.3 * s * widthScale);
        foreach (int side in new[] { -1, 1 })
        {
            var wr = new Rect(g.X + side * (w / 2 - ww / 2) - ww / 2, g.Y - wheel, ww, wheel);
            dc.DrawRectangle(BlackBrush, pen.Core, wr);
        }

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
        dc.DrawGeometry(BlackBrush, pen.Glow, body);
        dc.DrawGeometry(null, pen.Core, body);

        var detail = new LineBatch();
        double bumper = g.Y - 0.45 * s;
        detail.Line(new Point(g.X - w / 2, bumper), new Point(g.X + w / 2, bumper));
        if (e.Kind == EnemyKind.Blocker)
        {
            detail.Line(new Point(g.X, g.Y - 0.5 * s), new Point(g.X, g.Y - roof + 0.1 * s));   // cargo doors
        }
        else
        {
            double winBottom = g.Y - bodyTop - 0.06 * s, winTop = g.Y - roof + 0.1 * s;
            double wb = w / 2 * (1 - inset * 0.4) - 0.12 * s, wt = w / 2 * (1 - inset) - 0.12 * s;
            detail.Line(new Point(g.X - wb, winBottom), new Point(g.X + wb, winBottom));
            detail.Line(new Point(g.X + wb, winBottom), new Point(g.X + wt, winTop));
            detail.Line(new Point(g.X + wt, winTop), new Point(g.X - wt, winTop));
            detail.Line(new Point(g.X - wt, winTop), new Point(g.X - wb, winBottom));
        }
        if (e.Kind == EnemyKind.Pursuer)   // rear spoiler
        {
            detail.Line(new Point(g.X - w / 2 - 0.05 * s, g.Y - bodyTop - 0.12 * s), new Point(g.X + w / 2 + 0.05 * s, g.Y - bodyTop - 0.12 * s));
        }
        detail.Draw(dc, pen);

        double lw = Math.Max(2, 0.34 * s * widthScale), lh = Math.Max(1.5, 0.12 * s);
        foreach (int side in new[] { -1, 1 })
            dc.DrawRectangle(TailLight, null, new Rect(g.X + side * (w / 2 - lw * 0.7) - lw / 2, g.Y - 0.62 * s, lw, lh));

        dc.Pop();
    }

    void DrawBullets(DrawingContext dc, World world)
    {
        var p = world.Player;
        var batch = new LineBatch();
        foreach (var b in world.Bullets)
        {
            double dz = b.Z - p.Z;
            if (dz < 3) continue;
            double tailZ = dz - 7 * Math.Cos(b.Heading), tailX = b.X - 7 * Math.Sin(b.Heading);
            if (!ToScreen(world, dz, b.X, 0.6, out var c, out _) || !ToScreen(world, tailZ, tailX, 0.6, out var a, out _)) continue;
            if (a.Y > ClipAt(dz)) continue;
            batch.Line(a, c);
        }
        batch.Draw(dc, BulletPen);
    }

    void DrawExplosions(DrawingContext dc, World world)
    {
        var p = world.Player;
        foreach (var x in world.Explosions)
        {
            double dz = x.Z - p.Z;
            if (dz < 3) continue;
            if (!ToScreen(world, dz, x.X, 1, out var c, out double depth)) continue;
            double r = (1 + x.Age * 9) * Ppm(depth);
            var rng = new Random(x.Seed);
            var batch = new LineBatch();
            for (int i = 0; i < 18; i++)
            {
                double a = rng.NextDouble() * Math.Tau, len = 0.5 + rng.NextDouble() * 0.5;
                var dir = new Vector(Math.Cos(a), Math.Sin(a) * 0.8);
                batch.Line(c + dir * r * 0.35 * len, c + dir * r * len);
            }
            dc.PushOpacity(Math.Max(0, 1 - x.Age / 1.2));
            batch.Draw(dc, BoomPen);
            dc.Pop();
        }
    }

    // ---------------------------------------------------------------- cockpit and HUD

    double HoodTop => _h * 0.86;

    void DrawHood(DrawingContext dc, World world)
    {
        double top = HoodTop;
        var hood = new StreamGeometry();
        using (var c = hood.Open())
        {
            c.BeginFigure(new Point(-5, _h + 5), true, true);
            c.PolyLineTo(new[]
            {
                new Point(-5, top + 46), new Point(_w * 0.2, top + 10), new Point(_halfW, top),
                new Point(_w * 0.8, top + 10), new Point(_w + 5, top + 46), new Point(_w + 5, _h + 5),
            }, true, false);
        }
        hood.Freeze();
        dc.DrawGeometry(BlackBrush, HoodPen.Glow, hood);
        dc.DrawGeometry(null, HoodPen.Core, hood);

        var guns = new LineBatch();
        foreach (int side in new[] { -1, 1 })
            guns.Line(new Point(_halfW + side * 95, top + 6), new Point(_halfW + side * 78, top - 12));
        guns.Draw(dc, GunPen);

        var p = world.Player;
        if (p.MuzzleFlash > 0)
        {
            var flash = new LineBatch();
            var m = new Point(_halfW + (p.GunLeft ? -78 : 78), top - 14);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3 + world.Time * 7;
                flash.Line(m, m + new Vector(Math.Cos(a), Math.Sin(a)) * 12);
            }
            flash.Draw(dc, BulletPen);
        }

        // Aim mark where the tracers will be 60 m ahead (they follow the road, like the cars do).
        // Gun line 60 m ahead: straight out of the nose, which is always the screen centre.
        var aim = Project(0, world.View.At(60).Y - CamHeight + 0.6, 60);
        var cross = new LineBatch();
        cross.Line(aim + new Vector(-14, 0), aim + new Vector(-5, 0));
        cross.Line(aim + new Vector(5, 0), aim + new Vector(14, 0));
        cross.Line(aim + new Vector(0, -10), aim + new Vector(0, -4));
        cross.Draw(dc, HoodPen);
    }

    void DrawHud(DrawingContext dc, World world)
    {
        var p = world.Player;
        var t = world.Tuning;
        var scheme = world.Scheme;
        double top = HoodTop;

        // Speed
        double mph = p.Speed * Units.MphPerMs;
        Text(dc, $"{mph:0}", _halfW + 8, top + 8, 40, TextAmber, TextAlignment.Right, true);
        Text(dc, "MPH", _halfW + 16, top + 28, 15, TextGray);

        // Speed bar with the target marker
        var bar = new Rect(_halfW - 170, _h - 24, 340, 9);
        dc.DrawRectangle(BarBack, HudPen.Core, bar);
        dc.DrawRectangle(TextAmber, null, new Rect(bar.X, bar.Y, bar.Width * Math.Clamp(p.Speed / t.MaxSpeed, 0, 1), bar.Height));
        if (scheme.TargetSpeed is double target)
        {
            double x = bar.X + bar.Width * Math.Clamp(target / t.MaxSpeed, 0, 1);
            var marker = new StreamGeometry();
            using (var c = marker.Open())
            {
                c.BeginFigure(new Point(x, bar.Y - 1), true, true);
                c.PolyLineTo(new[] { new Point(x - 7, bar.Y - 11), new Point(x + 7, bar.Y - 11) }, true, false);
            }
            marker.Freeze();
            dc.DrawGeometry(TextCyan, null, marker);
        }
        if (scheme.GearTopSpeed is double gearTop)
        {
            double gx = bar.X + bar.Width * Math.Clamp(gearTop / t.MaxSpeed, 0, 1);
            dc.DrawRectangle(TextRed, null, new Rect(gx - 1, bar.Y - 4, 2, bar.Height + 8));
        }

        // Pedals
        DrawVerticalBar(dc, new Rect(_halfW - 236, top + 14, 14, 70), p.Throttle, ThrottleFill, "GAS");
        DrawVerticalBar(dc, new Rect(_halfW - 206, top + 14, 14, 70), p.Brake, BrakeFill, "BRK");

        // Steering
        var steer = new Rect(_halfW + 150, top + 52, 150, 9);
        dc.DrawRectangle(BarBack, HudPen.Core, steer);
        double sc = steer.X + steer.Width / 2, sx = sc + p.Steer * steer.Width / 2;
        dc.DrawRectangle(TextCyan, null, new Rect(Math.Min(sc, sx), steer.Y, Math.Abs(sx - sc), steer.Height));
        dc.DrawRectangle(TextGray, null, new Rect(sc - 1, steer.Y - 3, 2, steer.Height + 6));
        double noseDeg = p.Heading * 180 / Math.PI;
        string nose = Math.Abs(noseDeg) < 0.5 ? "NOSE STRAIGHT" : $"NOSE {Math.Abs(noseDeg):0}° {(noseDeg > 0 ? "RIGHT" : "LEFT")}";
        Text(dc, (world.PadSteering ? "STICK  " : "") + nose, steer.X, steer.Y - 20, 13, TextGray);

        string status = scheme.Status ?? "";
        if (p.OverRev) status += "  OVER-REV";
        if (p.OffRoad) status += "  OFF ROAD";
        Text(dc, status.Trim(), _halfW + 150, top + 14, 17, p.OverRev ? TextRed : TextCyan, bold: true);

        // Top-left: control scheme
        Text(dc, $"CONTROL  {scheme.Name}", 16, 12, 18, TextCyan, bold: true);
        double y = 38;
        foreach (var line in scheme.HelpLines) { Text(dc, line, 16, y, 13.5, TextGray); y += 18; }
        Text(dc, world.PadConnected ? "GAMEPAD CONNECTED: triggers are gas and brake" : "KEYBOARD  (no gamepad found)",
             16, y + 4, 13, world.PadConnected ? TextGreen : TextGray);
        Text(dc, "Tab / F2-F5 change control   F1 help   F12 tuning", 16, y + 24, 13, TextGray);

        // Top-right: score, armour, oil
        double rx = _w - 16;
        Text(dc, $"SCORE {world.Score:00000}", rx, 12, 18, TextAmber, TextAlignment.Right, true);
        var armour = new Rect(rx - 160, 42, 160, 10);
        dc.DrawRectangle(BarBack, HudPen.Core, armour);
        dc.DrawRectangle(p.Health > 30 ? TextGreen : TextRed, null, new Rect(armour.X, armour.Y, armour.Width * Math.Clamp(p.Health / 100, 0, 1), armour.Height));
        Text(dc, "ARMOR", armour.X - 8, 38, 13, TextGray, TextAlignment.Right);
        string oil = new string('#', p.OilCharges) + new string('.', World.MaxOil - p.OilCharges);
        Text(dc, $"OIL  {oil}", rx, 60, 15, TextCyan, TextAlignment.Right, true);
    }

    void DrawVerticalBar(DrawingContext dc, Rect r, double value, Brush fill, string label)
    {
        dc.DrawRectangle(BarBack, HudPen.Core, r);
        double h = r.Height * Math.Clamp(value, 0, 1);
        dc.DrawRectangle(fill, null, new Rect(r.X, r.Bottom - h, r.Width, h));
        Text(dc, label, r.X + r.Width / 2, r.Bottom + 2, 11, TextGray, TextAlignment.Center);
    }

    void DrawMirror(DrawingContext dc, World world)
    {
        var p = world.Player;
        var view = world.View;
        var m = new Rect(_halfW - 200, 10, 400, 86);
        dc.DrawRectangle(BlackBrush, HudPen.Glow, m);
        dc.DrawRectangle(null, HudPen.Core, m);
        dc.PushClip(new RectangleGeometry(m));

        double cx = m.X + m.Width / 2, horizon = m.Y + 30;
        // Looking backwards, depth is -F. Mirror image: a car behind you on your right shows on the right.
        bool MirrorPoint(double d, double lateral, out Point pt, out double scale)
        {
            var q = view.At(d, lateral);
            var (x, f) = ToCar(world, q.X - p.X, q.F);
            double depth = -f;
            scale = depth > 1 ? 140 / depth : 0;
            pt = new Point(cx + x * scale, horizon - (q.Y - CamHeight) * scale);
            return depth > 1;
        }

        var road = new LineBatch();
        Point? pl = null, pr = null;
        for (double d = -3; d > -300; d -= 10)
        {
            if (!MirrorPoint(d, -Track.RoadHalfWidth, out var l, out _) | !MirrorPoint(d, Track.RoadHalfWidth, out var r, out _))
            {
                pl = pr = null;
                continue;
            }
            if (pl is Point a && pr is Point b) { road.Line(a, l); road.Line(b, r); }
            pl = l; pr = r;
        }
        road.Draw(dc, MirrorRoad);

        foreach (var e in world.Enemies.Where(e => e.Z - p.Z < -2.5 && e.Z - p.Z > -300 && e.State != EnemyState.Wrecked)
                                      .OrderBy(e => e.Z))
        {
            if (!MirrorPoint(e.Z - p.Z, e.X, out var g, out double s)) continue;
            double w = e.Width * s, h = e.Height * s;
            var pen = e.Kind switch { EnemyKind.Blocker => BlockerPen, EnemyKind.Pursuer => PursuerPen, _ => RunnerPen };
            var body = new Rect(g.X - w / 2, g.Y - h, w, h);
            dc.DrawRectangle(BlackBrush, pen.Core, body);
            double lw = Math.Max(2, 0.3 * s);
            foreach (int side in new[] { -1, 1 })
                dc.DrawEllipse(HeadLight, null, new Point(g.X + side * (w / 2 - lw), g.Y - 0.5 * s), lw / 2 + 0.5, lw / 2 + 0.5);
        }
        dc.Pop();
        Text(dc, "REAR VIEW", m.X + 6, m.Bottom - 17, 11, TextGray);
    }

    void DrawRadar(DrawingContext dc, World world)
    {
        var p = world.Player;
        var view = world.View;
        var r = new Rect(_w - 214, _h - 300, 198, 284);
        dc.DrawRectangle(RadarFill, RadarPen.Core, r);
        dc.PushClip(new RectangleGeometry(r));

        double px = r.X + r.Width / 2, py = r.Bottom - 80;
        const double lateral = 1.6, longitudinal = 0.42;   // px per metre
        Point Map(double d, double x)
        {
            var q = view.At(d, x);
            return new Point(px + (q.X - p.X) * lateral, py - q.F * longitudinal);
        }

        var lines = new LineBatch();
        for (double d = -190; d < 500; d += 10)
        {
            lines.Line(Map(d, -Track.RoadHalfWidth), Map(d + 10, -Track.RoadHalfWidth));
            lines.Line(Map(d, Track.RoadHalfWidth), Map(d + 10, Track.RoadHalfWidth));
        }
        for (int ring = 100; ring <= 400; ring += 100)
            lines.Line(new Point(r.X + 4, py - ring * longitudinal), new Point(r.X + 14, py - ring * longitudinal));
        lines.Draw(dc, RadarPen);

        foreach (var o in world.Slicks)
            dc.DrawEllipse(null, OilPen.Core, Map(o.Z - p.Z, o.X), 4, 4);

        bool blink = (int)(world.Time * 4) % 2 == 0;
        foreach (var e in world.Enemies)
        {
            var at = Map(e.Z - p.Z, e.X);
            if (e.State == EnemyState.Wrecked)
            {
                var x = new LineBatch();
                x.Line(at + new Vector(-4, -4), at + new Vector(4, 4));
                x.Line(at + new Vector(-4, 4), at + new Vector(4, -4));
                x.Draw(dc, BoomPen);
                continue;
            }
            var brush = e.Kind switch { EnemyKind.Blocker => TextAmber, EnemyKind.Pursuer => TextRed, _ => TextMagenta };
            double size = e.Kind == EnemyKind.Pursuer && blink ? 6 : 4.5;
            dc.DrawRectangle(brush, null, new Rect(at.X - size / 2, at.Y - size, size, size * 2));
        }

        var me = new StreamGeometry();
        using (var c = me.Open())
        {
            c.BeginFigure(new Point(px, py - 9), true, true);
            c.PolyLineTo(new[] { new Point(px - 6, py + 6), new Point(px + 6, py + 6) }, true, false);
        }
        me.Freeze();
        dc.PushTransform(new RotateTransform(p.Heading * 180 / Math.PI, px, py));   // nose direction
        dc.DrawGeometry(TextGreen, null, me);
        dc.Pop();
        dc.Pop();
        Text(dc, "RADAR", r.X + 6, r.Y + 4, 11, TextGray);
    }

    void DrawWarnings(DrawingContext dc, World world)
    {
        var p = world.Player;
        bool blink = (int)(world.Time * 3) % 2 == 0;
        foreach (var e in world.Enemies)
        {
            if (e.State != EnemyState.Driving) continue;
            double dz = e.Z - p.Z;
            if (e.Kind == EnemyKind.Pursuer && dz < -3 && dz > -90)
            {
                string side = e.X < p.X - 1 ? "LEFT" : e.X > p.X + 1 ? "RIGHT" : "DIRECTLY";
                if (blink) DrawCentered(dc, $"PURSUER BEHIND {side}  {-dz:0} m", 102, 18, TextRed);
                double ax = e.X < p.X ? 18 : _w - 18;
                var arrow = new LineBatch();
                double dir = e.X < p.X ? -1 : 1;
                arrow.Line(new Point(ax - dir * 22, _h * 0.55 - 26), new Point(ax, _h * 0.55));
                arrow.Line(new Point(ax, _h * 0.55), new Point(ax - dir * 22, _h * 0.55 + 26));
                arrow.Draw(dc, PursuerPen);
            }
            else if (e.Kind == EnemyKind.Blocker && dz > 0 && dz < 70 && Math.Abs(e.X - p.X) < 2.5)
                DrawCentered(dc, "BLOCKER AHEAD", 124, 17, TextAmber);
        }
    }

    void DrawMessages(DrawingContext dc, World world)
    {
        double y = _h * 0.18;
        foreach (var m in world.Messages)
        {
            var brush = m.Tone switch { Tone.Good => TextGreen, Tone.Bad => TextRed, _ => TextCyan };
            dc.PushOpacity(Math.Clamp(m.Timer / 0.6, 0, 1));
            DrawCentered(dc, m.Text, y, 20, brush);
            dc.Pop();
            y += 26;
        }
    }

    void DrawHelp(DrawingContext dc, World world)
    {
        string[] lines =
        {
            "NIGHT PURSUIT  -  CONTROLS LAB  (throwaway prototype)",
            "",
            "DRIVE      Up/W gas    Down/S brake    Left Right / A D steer",
            "WEAPONS    Space: guns (hold)    X or Ctrl: oil slick behind you",
            "           Steer hard into a car's side to knock it into a spin",
            "CONTROLS   Tab cycles   F2 ramp pedal   F3 speed lever   F4 gearbox   F5 scroll wheel",
            "OTHER      C cruise   Q/E shift   1-9,0 lever presets   R reset   P pause   F12 tuning",
            "GAMEPAD    RT gas   LT brake   left stick steer   A guns   B oil   Y cruise",
            "           LB/RB shift   D-pad lever   Back: next control   Start: pause",
            "",
            "Press gas to start.  F1 shows this again.",
        };
        double width = 760, height = lines.Length * 22 + 30;
        var r = new Rect(_halfW - width / 2, _h * 0.30, width, height);
        dc.DrawRectangle(PanelFill, HudPen.Core, r);
        double y = r.Y + 16;
        for (int i = 0; i < lines.Length; i++)
        {
            Text(dc, lines[i], r.X + 22, y, i == 0 ? 17 : 14.5, i == 0 ? TextAmber : TextWhite, bold: i == 0);
            y += 22;
        }
    }

    // ---------------------------------------------------------------- text

    void DrawCentered(DrawingContext dc, string s, double y, double size, Brush brush) =>
        Text(dc, s, _halfW, y, size, brush, TextAlignment.Center, true);

    void Text(DrawingContext dc, string s, double x, double y, double size, Brush brush,
              TextAlignment align = TextAlignment.Left, bool bold = false)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                   bold ? Bold : Regular, size, brush, _dpi);
        double dx = align switch { TextAlignment.Center => -ft.Width / 2, TextAlignment.Right => -ft.Width, _ => 0 };
        dc.DrawText(ft, new Point(x + dx, y));
    }
}
