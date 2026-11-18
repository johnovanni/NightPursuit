using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Common;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The always-visible readouts: driving (speed, pedals, steering) along the bottom, and car state
/// (armor, spare cars, guns, oil, score, distance to go) at the top right. Each value is a bar or icon plus a word or number.
/// </summary>
internal static class Hud
{
    public static void Draw(DrawingContext dc, HudLayout l, MissionRun run)
    {
        DrawDriving(dc, l, run.World);
        DrawControls(dc, l, run.World);
        DrawCarState(dc, l, run);
    }

    static void DrawDriving(DrawingContext dc, HudLayout l, World w)
    {
        var p = w.Player;
        double k = l.K, hw = l.HalfWidth, top = l.HoodTop;
        var scheme = w.Scheme;

        double mph = Units.MsToMph(p.Speed);
        l.Text(dc, $"{mph:0}", hw + 8 * k, top + 6 * k, 40, Neon.TextAmber, TextAlignment.Right, true);
        l.Text(dc, "MPH", hw + 16 * k, top + 26 * k, 15, Neon.TextGray);

        // Speed bar with the target marker.
        var bar = new Rect(hw - 170 * k, l.Height - 24 * k, 340 * k, 9 * k);
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, bar);
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
        DrawVerticalBar(dc, l, new Rect(hw - 236 * k, top + 12 * k, 14 * k, 66 * k), p.Throttle, Neon.ThrottleFill, "GAS");
        DrawVerticalBar(dc, l, new Rect(hw - 206 * k, top + 12 * k, 14 * k, 66 * k), p.Brake, Neon.BrakeFill, "BRK");

        // Steering and nose angle
        var steer = new Rect(hw + 150 * k, top + 58 * k, 150 * k, 9 * k);
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, steer);
        double centre = steer.X + steer.Width / 2, sx = centre + p.Steer * steer.Width / 2;
        dc.DrawRectangle(Neon.TextCyan, null, new Rect(Math.Min(centre, sx), steer.Y, Math.Abs(sx - centre), steer.Height));
        dc.DrawRectangle(Neon.TextGray, null, new Rect(centre - 1, steer.Y - 3 * k, 2, steer.Height + 6 * k));
        double noseDeg = p.Heading * 180 / Math.PI;
        string nose = Math.Abs(noseDeg) < 0.5 ? "NOSE STRAIGHT" : $"NOSE {Math.Abs(noseDeg):0}° {(noseDeg > 0 ? "RIGHT" : "LEFT")}";
        l.Text(dc, (w.Steering.UsingStick ? "STICK  " : "") + nose, steer.X, steer.Y - 20 * k, 13, Neon.TextGray);

        string status = scheme.Status ?? "";
        if (p.OffRoad) status += "  OFF ROAD";
        l.Text(dc, status.Trim(), hw + 150 * k, top + 12 * k, 15, Neon.TextCyan, bold: true);
    }

    static void DrawVerticalBar(DrawingContext dc, HudLayout l, Rect r, double value, Brush fill, string label)
    {
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, r);
        double h = r.Height * Math.Clamp(value, 0, 1);
        dc.DrawRectangle(fill, null, new Rect(r.X, r.Bottom - h, r.Width, h));
        l.Text(dc, label, r.X + r.Width / 2, r.Bottom + 1 * l.K, 13, Neon.TextGray, TextAlignment.Center);
    }

    /// <summary>Top left: which throttle control is active and its help lines.</summary>
    static void DrawControls(DrawingContext dc, HudLayout l, World w)
    {
        double k = l.K, y = 12 * k;
        l.Text(dc, $"CONTROL  {w.Scheme.Name}", 16 * k, y, 18, Neon.TextCyan, bold: true);
        y += 26 * k;
        foreach (var line in w.Scheme.HelpLines) { l.Text(dc, line, 16 * k, y, 13.5, Neon.TextGray); y += 18 * k; }
        l.Text(dc, "F1 help   P pause   R restart (when paused or finished)", 16 * k, y + 4 * k, 13, Neon.TextGray);
    }

    static void DrawCarState(DrawingContext dc, HudLayout l, MissionRun run)
    {
        var w = run.World;
        var p = w.Player;
        double k = l.K, rx = l.Width - 16 * k, barW = 160 * k, barX = rx - barW;
        double labelX = barX - 8 * k;

        l.Text(dc, $"SCORE {w.Score:00000}", rx, 12 * k, 18, Neon.TextAmber, TextAlignment.Right, true);

        // Armor: bar plus the number, red below 30 %.
        double armorFrac = Math.Clamp(p.Armor / p.Stats.Armor, 0, 1);
        var armor = new Rect(barX, 44 * k, barW, 10 * k);
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, armor);
        dc.DrawRectangle(armorFrac > 0.3 ? Neon.TextGreen : Neon.TextRed, null, new Rect(armor.X, armor.Y, armor.Width * armorFrac, armor.Height));
        l.Text(dc, $"ARMOR {armorFrac * 100:0}", labelX, 40 * k, 13, armorFrac > 0.3 ? Neon.TextGray : Neon.TextRed, TextAlignment.Right);

        // Spare cars as little car icons.
        l.Text(dc, "SPARE CARS", labelX, 62 * k, 13, Neon.TextGray, TextAlignment.Right);
        for (int i = 0; i < p.SpareCars; i++)
            DrawCarIcon(dc, l, new Point(barX + i * 26 * k, 64 * k));

        // Oil charges as pips: filled = ready.
        l.Text(dc, "OIL", labelX, 86 * k, 13, Neon.TextGray, TextAlignment.Right);
        int charges = w.Loadout.Oil.Charges;
        for (int i = 0; i < OilDropper.MaxCharges; i++)
        {
            var c = new Point(barX + 8 * k + i * 24 * k, 93 * k);
            dc.DrawEllipse(i < charges ? Neon.OilFull : null, Neon.Oil.Core, c, 7 * k, 7 * k);
        }

        // Gun heat: amber while firing, red and spelled out when locked.
        var guns = w.Loadout.Guns;
        var heat = new Rect(barX, 112 * k, barW, 10 * k);
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, heat);
        dc.DrawRectangle(guns.Locked ? Neon.TextRed : Neon.TextAmber, null, new Rect(heat.X, heat.Y, heat.Width * Math.Clamp(guns.Heat, 0, 1), heat.Height));
        if (guns.Locked) l.Text(dc, "GUNS LOCKED", labelX, 108 * k, 13, Neon.TextRed, TextAlignment.Right, true);
        else l.Text(dc, "GUN HEAT", labelX, 108 * k, 13, Neon.TextGray, TextAlignment.Right);

        double km = run.Director.DistanceRemaining(w) / 1000;
        l.Text(dc, $"DISTANCE TO GO {km:0.0} km", rx, 132 * k, 15, Neon.TextCyan, TextAlignment.Right, true);
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
