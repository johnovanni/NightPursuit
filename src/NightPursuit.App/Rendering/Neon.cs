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

    // Cockpit and HUD.
    public static readonly NeonPen Hood = Pen(Green, 0.8, 1.6), Gun = Pen(Green, 1, 3);
    public static readonly NeonPen Hud = Pen(Cyan, 0.8, 1.2), RadarLine = Pen(Green, 0.55, 1.0), MirrorRoad = Pen(Cyan, 0.6, 1.0);
    public static readonly NeonPen RadarWreck = Pen(Orange, 1, 1.6), RadarRunner = Pen(Magenta, 1, 1.5), RadarBlocker = Pen(Amber, 1, 1.5), RadarPursuer = Pen(Red, 1, 1.5);
    public static readonly Brush TextCyan = Solid(Cyan), TextGray = Solid(Gray), TextAmber = Solid(Amber), TextRed = Solid(Red);
    public static readonly Brush TextGreen = Solid(Green), TextWhite = Solid(White), TextMagenta = Solid(Magenta);
    public static readonly Brush ThrottleFill = Solid(Green, 0.85), BrakeFill = Solid(Red, 0.85), HudBack = Solid(Cyan, 0.08);
    public static readonly Brush PanelFill = Solid(Color.FromRgb(4, 8, 14), 0.9), RadarFill = Solid(Color.FromRgb(2, 14, 8), 0.85);
    public static readonly Brush HitEdge = Solid(Red, 0.55);
    public static readonly Brush BarFill = Solid(Red), OilFull = Solid(Purple), HoodFill = Solid(Green, 0.25);
    public static readonly Brush RadarRunnerFill = Solid(Magenta), TextOrange = Solid(Orange);

    public static NeonPen Pen(Color c, double alpha = 1, double width = 1.6)
    {
        var glow = new Pen(Solid(c, 0.27 * alpha), width + 4)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var core = new Pen(Solid(c, alpha), width)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
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
