using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Common;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The always-visible readouts: driving (speed, pedals, steering) along the bottom, and car state
/// (score, armor, spare cars, the boss or distance to go, and the no-grip and puncture timers) at the top right. Each value is a bar or icon plus a word or number.
/// </summary>
internal static class Hud
{
    public static void Draw(DrawingContext dc, HudLayout l, MissionRun run)
    {
        DrawDriving(dc, l, run.World);
        DrawControls(dc, l, run);
        DrawCarState(dc, l, run);
        HeatMeterView.Draw(dc, l, run.World);
    }

    /// <summary>
    /// The SOUND OFF or NO SOUND mark, in the small HUD text, at the bottom left of the road: under the weapon panel and clear of the
    /// speed and steering readouts. When the left warning chevron (which sits under the weapon panel) would reach that row, as in a
    /// tiny window or at the largest text, no corner is free, so the mark is left out rather than cover play information.
    /// Only drawn in play (the menus show the setting).
    /// </summary>
    public static void DrawSoundMark(DrawingContext dc, HudLayout l, string text)
    {
        double x = 16 * l.K, y = l.Height - l.T(22);
        if (!Overlays.LeftChevronClear(l, y)) return;
        l.Text(dc, text, x, y, 13, Neon.TextAmber, bold: true);
    }

    static void DrawDriving(DrawingContext dc, HudLayout l, World w)
    {
        var p = w.Player;
        double k = l.K, hw = l.HalfWidth, top = l.HoodTop;
        var scheme = w.Scheme;

        double mph = Units.MsToMph(p.Speed);
        l.Text(dc, $"{mph:0}", hw + 8 * k, top + 6 * k, 40, Neon.TextAmber, TextAlignment.Right, true);
        l.Text(dc, "MPH", hw + 16 * k, top + l.T(26), 15, Neon.TextGray);

        // Speed bar with the target marker.
        var bar = new Rect(hw - 170 * k, l.Height - 24 * k, 340 * k, 9 * k);
        dc.DrawRectangle(Neon.HudBack, null, bar);
        LineArt.Box(dc, bar, LineArt.Gauge);
        dc.DrawRectangle(Neon.TextAmber, null, new Rect(bar.X, bar.Y, bar.Width * Math.Clamp(p.Speed / w.Feel.MaxSpeed, 0, 1), bar.Height));
        if (scheme.TargetSpeed is double target)
        {
            double x = bar.X + bar.Width * Math.Clamp(target / w.Feel.MaxSpeed, 0, 1);
            var marker = new StreamGeometry();
            using (var c = marker.Open())
            {
                c.BeginFigure(new Point(x, bar.Y - 1), true, true);
                c.PolyLineTo(new[] { new Point(x - 7 * k, bar.Y - 11 * k), new Point(x + 7 * k, bar.Y - 11 * k) }, true, false);
            }
            marker.Freeze();
            dc.DrawGeometry(Neon.TextCyan, null, marker);
        }

        // Pedals
        // The labels grow with the text, so the bars get shorter by the same amount (they hang from the screen's bottom edge) and the pair moves apart.
        double pedalH = 66 * k - l.Grow(16);
        DrawVerticalBar(dc, l, new Rect(hw - 236 * k - l.Grow(14), top + 12 * k, 14 * k, pedalH), p.Throttle, Neon.ThrottleFill, "GAS");
        DrawVerticalBar(dc, l, new Rect(hw - 206 * k, top + 12 * k, 14 * k, pedalH), p.Brake, Neon.BrakeFill, "BRK");

        // Steering and nose angle
        double noseY = top + 38 * k + l.Grow(18);   // under the status line, which grows with the text
        var steer = new Rect(hw + 150 * k + l.Grow(60), noseY + 20 * k + l.Grow(16), 150 * k, 9 * k);
        dc.DrawRectangle(Neon.HudBack, null, steer);
        LineArt.Box(dc, steer, LineArt.Gauge);
        double centre = steer.X + steer.Width / 2, sx = centre + p.Steer * steer.Width / 2;
        dc.DrawRectangle(Neon.TextCyan, null, new Rect(Math.Min(centre, sx), steer.Y, Math.Abs(sx - centre), steer.Height));
        dc.DrawRectangle(Neon.TextGray, null, new Rect(centre - 1, steer.Y - 3 * k, 2, steer.Height + 6 * k));
        double noseDeg = p.Heading * 180 / Math.PI;
        string nose = Math.Abs(noseDeg) < 0.5 ? "NOSE STRAIGHT" : $"NOSE {Math.Abs(noseDeg):0}° {(noseDeg > 0 ? "RIGHT" : "LEFT")}";
        l.Text(dc, (w.Steering.UsingStick ? "STICK  " : "") + nose, steer.X, noseY, 13, Neon.TextGray);

        string status = scheme.Status ?? "";
        if (p.OffRoad) status += "  OFF ROAD";
        l.Text(dc, status.Trim(), hw + 150 * k + l.Grow(60), top + 12 * k, 15, Neon.TextCyan, bold: true);
    }

    static void DrawVerticalBar(DrawingContext dc, HudLayout l, Rect r, double value, Brush fill, string label)
    {
        dc.DrawRectangle(Neon.HudBack, null, r);
        LineArt.Box(dc, r, LineArt.Gauge);
        double h = r.Height * Math.Clamp(value, 0, 1);
        dc.DrawRectangle(fill, null, new Rect(r.X, r.Bottom - h, r.Width, h));
        l.Text(dc, label, r.X + r.Width / 2, r.Bottom + 1 * l.K, 13, Neon.TextGray, TextAlignment.Center);
    }

    /// <summary>Top left: which throttle control is active and its help lines.</summary>
    static void DrawControls(DrawingContext dc, HudLayout l, MissionRun run)
    {
        var w = run.World;
        // Lines are drawn smaller (never below the Normal size) rather than run into the mirror or the boss bar.
        double k = l.K, x = 16 * k, y = 12 * k;
        double limit = Mirror.Bounds(l).Left - 10 * k;
        if (BossBar.Showing(run) is not null) limit = Math.Min(limit, BossBar.PanelBounds(l).Left - 6 * k);
        double room = limit - x;
        string title = $"CONTROL  {w.Scheme.Name}";
        l.Text(dc, title, x, y, l.FitSize(title, 18, room, true), Neon.TextCyan, bold: true);
        y += l.T(26);
        double lineSize = 13.5;   // one size for all the lines, the largest at which the longest still fits
        foreach (var line in w.Scheme.HelpLines) lineSize = Math.Min(lineSize, l.FitSize(line, 13.5, room));
        foreach (var line in w.Scheme.HelpLines) { l.Text(dc, line, x, y, lineSize, Neon.TextGray); y += l.T(18); }
        l.Text(dc, "F1 controls  P pause", x, y + l.T(4), 13, Neon.TextGray);
    }

    /// <summary>Screen rectangle covered by the top-right readouts (score, armor, spare cars, boss or distance, no-grip and puncture timers, the heat meter), so scenery can keep clear of it.</summary>
    public static Rect ReadoutBounds(HudLayout l) =>
        new(l.Width - l.T(290), 0, l.T(290), l.T(200));

    static void DrawCarState(DrawingContext dc, HudLayout l, MissionRun run)
    {
        var w = run.World;
        var p = w.Player;
        double k = l.K * l.TextScale, rx = l.Width - 16 * k, barW = 160 * l.K + l.Grow(80), barX = rx - barW;
        double labelX = barX - 8 * k;

        l.Text(dc, $"SCORE {w.Score:00000}", rx, 12 * k, 18, Neon.TextAmber, TextAlignment.Right, true);

        // Armor: bar plus the number, red below 30 %.
        double armorFrac = Math.Clamp(p.Armor / p.Stats.Armor, 0, 1);
        var armor = new Rect(barX, 44 * k, barW, 10 * k);
        dc.DrawRectangle(Neon.HudBack, null, armor);
        LineArt.Box(dc, armor, LineArt.Gauge);
        dc.DrawRectangle(armorFrac > 0.3 ? Neon.TextGreen : Neon.TextRed, null, new Rect(armor.X, armor.Y, armor.Width * armorFrac, armor.Height));
        l.Text(dc, $"ARMOR {armorFrac * 100:0}", labelX, 40 * k, 13, armorFrac > 0.3 ? Neon.TextGray : Neon.TextRed, TextAlignment.Right);

        // Spare cars as little car icons.
        l.Text(dc, "SPARE CARS", labelX, 62 * k, 13, Neon.TextGray, TextAlignment.Right);
        int spareIcons = run.State == RunState.Respawning ? Math.Max(0, p.SpareCars - 1) : p.SpareCars;   // the banner already counts the car being replaced
        for (int i = 0; i < spareIcons; i++)
            DrawCarIcon(dc, l, new Point(barX + i * 26 * k, 64 * k));

        double yy = 84 * k;
        string distance = run.Director.Stage == MissionStage.Boss
            ? $"BOSS: {run.Director.Boss?.Definition.Name.ToUpperInvariant() ?? "..."}"
            : $"DISTANCE TO GO {run.Director.DistanceRemaining(w) / 1000:0.0} km";
        l.Text(dc, distance, rx, yy, 15, Neon.TextCyan, TextAlignment.Right, true);
        yy += 22 * k;

        // What enemy oil and caltrops did to you: words, plus a shrinking bar for the puncture.
        if (p.GripLoss > 0)
        {
            l.Text(dc, "NO GRIP", rx, yy, 15, Neon.TextOrange, TextAlignment.Right, true);
            yy += 20 * k;
        }
        if (p.PunctureLeft > 0)
        {
            l.Text(dc, $"PUNCTURED {p.PunctureLeft:0.0} s", rx, yy, 15, Neon.TextOrange, TextAlignment.Right, true);
            var bar = new Rect(barX, yy + 20 * k, barW, 6 * k);
            dc.DrawRectangle(Neon.HudBack, null, bar);
            LineArt.Box(dc, bar, LineArt.Gauge);
            dc.DrawRectangle(Neon.TextOrange, null, new Rect(bar.X, bar.Y, bar.Width * Math.Clamp(p.PunctureLeft / PlayerCar.PunctureSeconds, 0, 1), bar.Height));
        }
    }

    /// <summary>A small car seen from behind: body, cabin line and two wheels.</summary>
    static void DrawCarIcon(DrawingContext dc, HudLayout l, Point at)
    {
        double k = l.K;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(at.X, at.Y + 14 * k), true, true);
            c.PolyLineTo(new[]
            {
                new Point(at.X + 20 * k, at.Y + 14 * k), new Point(at.X + 20 * k, at.Y + 8 * k), new Point(at.X + 16 * k, at.Y),
                new Point(at.X + 4 * k, at.Y), new Point(at.X, at.Y + 8 * k),
            }, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(Neon.HoodFill, Neon.Hood.Core, g);
        dc.DrawRectangle(Neon.TextGreen, null, new Rect(at.X + 1 * k, at.Y + 12 * k, 4 * k, 4 * k));
        dc.DrawRectangle(Neon.TextGreen, null, new Rect(at.X + 15 * k, at.Y + 12 * k, 4 * k, 4 * k));
    }
}
