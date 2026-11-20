using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>A pen pair for the glowing vector look: a wide faint stroke under a thin bright one.</summary>
internal sealed record NeonPen(Pen Glow, Pen Core);

/// <summary>The neon palette and the frozen pens and brushes built from it.</summary>
internal static class Neon
{
    public static readonly Color Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
    public static readonly Color Blue = Color.FromRgb(0x3D, 0x7B, 0xFF);
    public static readonly Color Green = Color.FromRgb(0x39, 0xFF, 0x88);
    public static readonly Color Amber = Color.FromRgb(0xFF, 0xC1, 0x3B);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x3B);
    public static readonly Color Magenta = Color.FromRgb(0xFF, 0x4D, 0xE1);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x1F);
    public static readonly Color Purple = Color.FromRgb(0xB0, 0x6B, 0xFF);
    public static readonly Color Indigo = Color.FromRgb(0x5A, 0x4F, 0xD0);
    public static readonly Color White = Color.FromRgb(0xEE, 0xF4, 0xFF);
    public static readonly Color Gray = Color.FromRgb(0x9A, 0xA8, 0xB8);
    public static readonly Color OrangeRed = Color.FromRgb(0xFF, 0x6A, 0x3D);   // Gunner
    public static readonly Color Teal = Color.FromRgb(0x2E, 0xE6, 0xC8);        // Dropper
    public static readonly Color SteelBlue = Color.FromRgb(0x6F, 0x9B, 0xFF);   // Armored
    public static readonly Color Gold = Color.FromRgb(0xFF, 0xD2, 0x4A);        // boss trim
    public static readonly Color Steel = Color.FromRgb(0xD0, 0xD8, 0xE0);       // caltrops
    public static readonly Color CivilianGray = Color.FromRgb(0x7F, 0x8C, 0x99);
    public static readonly Color PoliceSilver = Color.FromRgb(0xC9, 0xD6, 0xEA);
    public static readonly Color LightBlue = Color.FromRgb(0x2E, 0x6B, 0xFF);   // police light bar
    public static readonly Color LightRed = Color.FromRgb(0xFF, 0x2E, 0x4D);
    // The heat meter, calm to manhunt (the level name is always written too).
    public static readonly Color HeatCalm = Color.FromRgb(0x39, 0xFF, 0x88), HeatWatched = Color.FromRgb(0xFF, 0xE1, 0x4D),
        HeatPatrol = Color.FromRgb(0xFF, 0xC1, 0x3B), HeatPursuit = Color.FromRgb(0xFF, 0x8A, 0x1F), HeatManhunt = Color.FromRgb(0xFF, 0x3B, 0x3B);

    public static readonly Brush Black = Solid(Colors.Black);

    // Road and scenery.
    public static readonly NeonPen RoadNear = Pen(Cyan), RoadMid = Pen(Cyan, 0.55), RoadFar = Pen(Cyan, 0.25);
    public static readonly NeonPen LaneNear = Pen(Blue, 0.9, 1.3), LaneMid = Pen(Blue, 0.5, 1.0), LaneFar = Pen(Blue, 0.22, 1.0);
    public static readonly NeonPen Post = Pen(Gray, 0.6, 1.2), Lamp = Pen(Amber, 0.9, 2.2);
    public static readonly NeonPen Sky = Pen(Indigo, 0.8, 1.2), Moon = Pen(White, 0.6, 1.2);
    public static readonly Brush Star = Solid(White, 0.55);

    // Vehicles, hazards and effects.
    public static readonly NeonPen RunnerCar = Pen(Magenta), BlockerCar = Pen(Amber), PursuerCar = Pen(Red), CarFlash = Pen(White, 1, 2.4);
    public static readonly NeonPen Tracer = Pen(Amber, 1, 2), Oil = Pen(Purple, 0.9, 1.4), Boom = Pen(Orange, 1, 2);
    public static readonly NeonPen WreckBody = Pen(Orange, 0.45, 1.4), FlameOuter = Pen(Orange, 1, 2), FlameInner = Pen(Amber, 1, 1.4);
    public static readonly Brush OilFill = Solid(Purple, 0.16), TailLight = Solid(Red), BarBack = Solid(Colors.Black, 0.7);

    // Civilians and police.
    public static readonly NeonPen CivilianCar = Pen(CivilianGray), PoliceCar = Pen(PoliceSilver);
    public static readonly NeonPen LightBluePen = Pen(LightBlue, 1, 2.4), LightRedPen = Pen(LightRed, 1, 2.4);
    public static readonly Brush LightBlueFill = Solid(LightBlue), LightRedFill = Solid(LightRed);
    public static readonly Brush RadarCivilian = Solid(CivilianGray);
    public static readonly NeonPen RadarPolice = Pen(White, 1, 1.5), GapPen = Pen(Green, 1, 2);

    // Roadblocks and speed traps.
    public static readonly NeonPen BarrierPen = Pen(Red, 0.9, 1.6), SignPen = Pen(Amber, 1, 2), SignCaughtPen = Pen(Red, 1, 2.4);
    public static readonly Brush StripeRed = Solid(Red, 0.85), StripeWhite = Solid(White, 0.85);
    public static readonly Brush SignFill = Solid(Amber, 0.1), SignCaughtFill = Solid(Red, 0.35);
    public static readonly NeonPen RadarBarrier = Pen(Red, 1, 3.2), RadarGap = Pen(Green, 1, 2);

    /// <summary>Seconds each colour of a police light bar shows before the halves swap.</summary>
    public const double LightSwapSeconds = 0.2;

    /// <summary>
    /// The one light-bar clock (0 or 1), flipping every <see cref="LightSwapSeconds"/> of <c>World.Time</c>. Light bars, the
    /// mirror's top line and the radar's police squares all read it, so they flash together.
    /// </summary>
    public static int LightPhase(double time) => (int)(time / LightSwapSeconds) % 2;

    /// <summary>The blue/red pen of a light-bar half: <paramref name="side"/> is -1 (left) or 1 (right); the halves are always opposite.</summary>
    public static NeonPen LightPen(double time, int side) => (LightPhase(time) == 0) == (side < 0) ? LightBluePen : LightRedPen;

    /// <summary>The blue/red brush of a light-bar half; see <see cref="LightPen"/>.</summary>
    public static Brush LightFill(double time, int side) => (LightPhase(time) == 0) == (side < 0) ? LightBlueFill : LightRedFill;

    // Arsenal, enemy types and the boss.
    public static readonly NeonPen GunnerCar = Pen(OrangeRed), DropperCar = Pen(Teal), ArmoredCar = Pen(SteelBlue);
    public static readonly NeonPen BossCar = Pen(White), BossTrim = Pen(Gold), BossFlash = Pen(Red, 1, 2.4);
    public static readonly NeonPen EnemyTracer = Pen(Color.FromRgb(0xFF, 0x5A, 0x36), 1, 2);
    public static readonly NeonPen PlayerMissile = Pen(White, 1, 2.2), EnemyMissile = Pen(Red, 1, 2.2);
    public static readonly NeonPen Crate = Pen(White, 1, 1.5), Caltrop = Pen(Steel, 1, 1.3);
    public static readonly NeonPen StrikeBeam = Pen(Color.FromRgb(0x9F, 0xF6, 0xFF), 1, 2);
    /// <summary>The outline of anything an enemy dropped: red and dashed, so the colour is not the only cue.</summary>
    public static readonly NeonPen HostileOutline = Pen(Red, 0.8, 1.6, DashStyles.Dash);
    public static readonly NeonPen RadarGunner = Pen(OrangeRed, 1, 1.8), RadarDropper = Pen(Teal, 1, 1.5), RadarArmored = Pen(SteelBlue, 1, 1.5), RadarBossOuter = Pen(White, 1, 1.8);
    public static readonly NeonPen ShieldBright = Pen(Cyan, 1, 3.4), ShieldDim = Pen(Cyan, 0.45, 2.4), OpenArmor = Pen(Gold, 1, 1.8), ChuteGlow = Pen(Teal, 1, 3.4);
    public static readonly Brush PortGlow = Solid(OrangeRed), PortBurst = Solid(White), PortDim = Solid(OrangeRed, 0.35);
    public static readonly Brush ChuteFill = Solid(Teal, 0.9), TextGold = Solid(Gold), TextTeal = Solid(Teal), TextOrangeRed = Solid(OrangeRed), TextSteel = Solid(Steel), TextSteelBlue = Solid(SteelBlue);
    public static readonly Brush SmokeFill = Solid(Gray, 0.22), CrateFill = Solid(Cyan, 0.12);

    // Cockpit and HUD.
    public static readonly NeonPen Hood = Pen(Green, 0.8, 1.6), Gun = Pen(Green, 1, 3);
    public static readonly NeonPen Hud = Pen(Cyan, 0.8, 1.2), RadarLine = Pen(Green, 0.55, 1.0), MirrorRoad = Pen(Cyan, 0.6, 1.0);
    public static readonly NeonPen RadarWreck = Pen(Orange, 1, 1.6), RadarRunner = Pen(Magenta, 1, 1.5), RadarBlocker = Pen(Amber, 1, 1.5), RadarPursuer = Pen(Red, 1, 1.5);
    public static readonly Brush TextLightBlue = Solid(Color.FromRgb(0x6F, 0xA0, 0xFF)), TextCyan = Solid(Cyan), TextGray = Solid(Gray), TextAmber = Solid(Amber), TextRed = Solid(Red);
    public static readonly Brush TextGreen = Solid(Green), TextWhite = Solid(White), TextMagenta = Solid(Magenta);
    public static readonly Brush ThrottleFill = Solid(Green, 0.85), BrakeFill = Solid(Red, 0.85), HudBack = Solid(Cyan, 0.08);
    public static readonly Brush PanelFill = Solid(Color.FromRgb(4, 8, 14), 0.9), RadarFill = Solid(Color.FromRgb(2, 14, 8), 0.85);
    public static readonly Brush HitEdge = Solid(Red, 0.55);
    public static readonly Brush BarFill = Solid(Red), OilFull = Solid(Purple), HoodFill = Solid(Green, 0.25);
    public static readonly Brush RadarRunnerFill = Solid(Magenta), TextOrange = Solid(Orange);

    public static NeonPen Pen(Color c, double alpha = 1, double width = 1.6, DashStyle? dash = null)
    {
        var glow = new Pen(Solid(c, 0.27 * alpha), width + 4)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var core = new Pen(Solid(c, alpha), width)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (dash is not null) { glow.DashStyle = dash; core.DashStyle = dash; }
        glow.Freeze();
        core.Freeze();
        return new NeonPen(glow, core);
    }

    public static SolidColorBrush Solid(Color c, double alpha = 1)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
