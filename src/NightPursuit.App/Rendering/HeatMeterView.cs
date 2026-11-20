using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Police;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The heat meter, under the car-state readouts at the top right: the word HEAT with its number, the level name written out
/// (colour is never the only cue) and a five-segment bar with ticks at the level thresholds.
/// </summary>
internal static class HeatMeterView
{
    static readonly HeatLevel[] Levels = { HeatLevel.Calm, HeatLevel.Watched, HeatLevel.Patrol, HeatLevel.Pursuit, HeatLevel.Manhunt };
    static readonly double[] Edges = { 0, HeatRules.Watched, HeatRules.Patrol, HeatRules.Pursuit, HeatRules.Manhunt, HeatRules.Max };
    static readonly Brush[] Dim = Levels.Select(v => Neon.Solid(Colour(v), 0.22)).ToArray();
    static readonly Brush[] Fill = Levels.Select(v => Neon.Solid(Colour(v), 0.95)).ToArray();
    static readonly Brush[] Text = Levels.Select(v => Neon.Solid(Colour(v))).ToArray();
    static readonly Pen FlashOutline = MakePen(Neon.White, 3);
    static readonly Pen Tick = MakePen(Neon.White, 1);

    /// <summary>Where the meter draws: inside <see cref="Hud.ReadoutBounds"/>, clear of the car-state rows (which end near 152) and of the right edge arrow.</summary>
    public static Rect Bounds(HudLayout l) => new(l.Width - l.T(214), l.T(158), l.T(198), l.T(38));   // the readout block above it grows with the text, so this moves down with it

    public static Color Colour(HeatLevel level) => level switch
    {
        HeatLevel.Watched => Neon.HeatWatched,
        HeatLevel.Patrol => Neon.HeatPatrol,
        HeatLevel.Pursuit => Neon.HeatPursuit,
        HeatLevel.Manhunt => Neon.HeatManhunt,
        _ => Neon.HeatCalm,
    };

    public static void Draw(DrawingContext dc, HudLayout l, World w)
    {
        var heat = w.Heat;
        double k = l.T(1);
        var b = Bounds(l);
        int level = Array.IndexOf(Levels, heat.Level);

        l.Text(dc, $"HEAT {heat.Value:0}", b.X, b.Y, 15, Neon.TextGray, bold: true);
        l.Text(dc, HeatRules.LevelName(heat.Level), b.Right, b.Y, 15, Text[level], TextAlignment.Right, true);

        var bar = new Rect(b.X, b.Y + 22 * k, b.Width, 10 * k);
        double frac = Math.Clamp(heat.Value / HeatRules.Max, 0, 1);
        for (int i = 0; i < Levels.Length; i++)
        {
            double x0 = bar.X + bar.Width * Edges[i] / HeatRules.Max, x1 = bar.X + bar.Width * Edges[i + 1] / HeatRules.Max;
            dc.DrawRectangle(Dim[i], null, new Rect(x0, bar.Y, x1 - x0, bar.Height));
            double filled = Math.Min(x1, bar.X + bar.Width * frac) - x0;
            if (filled > 0) dc.DrawRectangle(Fill[i], null, new Rect(x0, bar.Y, filled, bar.Height));
        }
        for (int i = 1; i < Edges.Length - 1; i++)
        {
            double x = bar.X + bar.Width * Edges[i] / HeatRules.Max;
            dc.DrawLine(Tick, new Point(x, bar.Y - 2 * k), new Point(x, bar.Bottom + 2 * k));
        }
        // A rise: the outline goes bright and thick, blinking twice a second.
        bool flash = heat.RiseFlash > 0 && (int)(w.Time * 4) % 2 == 0;
        if (flash) dc.DrawRectangle(null, FlashOutline, bar);
        else LineArt.Box(dc, bar, LineArt.Gauge);
    }

    static Pen MakePen(Color c, double width)
    {
        var p = new Pen(Neon.Solid(c), width);
        p.Freeze();
        return p;
    }
}
