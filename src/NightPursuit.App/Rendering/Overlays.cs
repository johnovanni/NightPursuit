using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Everything that tells the driver something happened or is about to: warnings with words and edge arrows,
/// the off-road banner, the damage flash, recent messages, and the pause / respawn / end / help overlays.
/// Blinking is a dim-to-bright pulse driven by world time, so a warning never disappears entirely.
/// </summary>
internal static class Overlays
{
    const double MessageLife = 2.5, MessageFade = 0.6;

    static readonly int[] LeftOnly = { -1 }, RightOnly = { 1 };

    static readonly string[] HelpLines =
    {
        "NIGHT PURSUIT  -  CONTROLS",
        "",
        "DRIVE      W / Up gas     S / Down brake     A D / Left Right steer",
        "WEAPONS    Space guns (hold)     X or Ctrl drop oil behind you",
        "           Steer into a car's side to knock it into a spin",
        "SPEED      C cruise     F2 ramp pedal     F3 speed lever",
        "           1-0 lever presets     mouse wheel moves the lever",
        "GAME       P / Esc pause   R restart (when paused or finished)   F1 this help",
        "GAMEPAD    right trigger gas     left trigger brake     left stick steer",
        "           A guns   B oil   Y cruise   D-pad up/down lever   Start pause   Back restart",
        "",
        "Press gas to start.  F1 shows this again.",
    };

    /// <summary>Draws exactly what <see cref="Warnings.Collect"/> says (the rules live in Core, where tests prove each one): words, and an edge arrow where it has one.</summary>
    public static void DrawWarnings(DrawingContext dc, HudLayout l, World w)
    {
        double k = l.K;
        double pulse = (w.Time * 3) % 1 < 0.5 ? 1 : 0.5;
        dc.PushOpacity(pulse);
        double y = 108 * k;
        foreach (var warning in Warnings.Collect(w))
        {
            if (warning.ShowArrow) DrawEdgeArrow(dc, l, warning.Side);
            l.Centered(dc, warning.Text, y, 18, WarningBrush(warning.Kind));
            y += 22 * k;
        }
        dc.Pop();
    }

    static Brush WarningBrush(WarningKind kind) => kind switch
    {
        WarningKind.BlockerAhead => Neon.TextAmber,
        WarningKind.WreckAhead or WarningKind.HazardAhead => Neon.TextOrange,
        _ => Neon.TextRed,
    };

    /// <summary>The big banner for a boss, a boss phase or a strike: a title and a line of detail, fading out in its last half second.</summary>
    public static void DrawAnnouncement(DrawingContext dc, HudLayout l, World w)
    {
        if (w.Announcement is not { } a) return;
        double k = l.K;
        dc.PushOpacity(Math.Clamp((a.Until - w.Time) / MessageFade, 0, 1));
        l.Centered(dc, a.Title, l.Height * 0.44, 30, Neon.TextAmber);
        if (a.Detail.Length > 0) l.Centered(dc, a.Detail, l.Height * 0.44 + 40 * k, 18, Neon.TextCyan);
        dc.Pop();
    }

    /// <summary>A chevron on the screen edge pointing out toward the side a warning comes from (both sides when it is dead behind): pursuers, missiles, blockers, the boss and hostiles.</summary>
    static void DrawEdgeArrow(DrawingContext dc, HudLayout l, int side)
    {
        double k = l.K, y = l.Height * 0.55;
        foreach (int dir in side == 0 ? Projector.Sides : side < 0 ? LeftOnly : RightOnly)
        {
            double ax = dir < 0 ? 18 * k : l.Width - 18 * k;
            var arrow = new LineBatch();
            arrow.Line(new Point(ax - dir * 22 * k, y - 26 * k), new Point(ax, y));
            arrow.Line(new Point(ax, y), new Point(ax - dir * 22 * k, y + 26 * k));
            arrow.Draw(dc, Neon.PursuerCar);
        }
    }

    public static void DrawMessages(DrawingContext dc, HudLayout l, World w)
    {
        double y = Math.Max(l.Height * 0.26, 200 * l.K);   // below the four warning lines
        foreach (var e in w.RecentMessages)
        {
            if (e is not Message m) continue;
            var brush = m.Tone switch { Tone.Good => Neon.TextGreen, Tone.Bad => Neon.TextRed, _ => Neon.TextCyan };
            double left = MessageLife - (w.Time - m.Time);
            dc.PushOpacity(Math.Clamp(left / MessageFade, 0, 1));
            l.Centered(dc, m.Text, y, 20, brush);
            dc.Pop();
            y += 26 * l.K;
        }
    }

    /// <summary>The red damage edge and the off-road banner (the rumble shake must have a visible cause).</summary>
    public static void DrawDamageAndOffRoad(DrawingContext dc, HudLayout l, MissionRun run)
    {
        var p = run.World.Player;
        if (p.HitFlash > 0)
        {
            double t = l.S(18) * Math.Min(1, p.HitFlash / 0.35);
            dc.DrawRectangle(null, new Pen(Neon.HitEdge, t), new Rect(t / 2, t / 2, Math.Max(1, l.Width - t), Math.Max(1, l.Height - t)));
        }
        if (run.State == RunState.Playing && p.State == VehicleState.Driving && p.OffRoad)
            l.Centered(dc, p.Lateral < 0 ? "<< OFF ROAD" : "OFF ROAD >>", l.Height * 0.62, 26, Neon.TextAmber);
    }

    public static void DrawState(DrawingContext dc, HudLayout l, MissionRun run)
    {
        var w = run.World;
        // A dark backing box keeps the words readable over the help panel or any bright scenery behind them.
        if (run.State != RunState.Playing)
        {
            double boxTop = run.State == RunState.Paused ? l.Height * 0.36 : l.Height * 0.30;
            double boxHeight = run.State == RunState.Paused ? l.S(54) : l.S(110);
            double boxWidth = Math.Min(l.Width - 20, l.S(760));
            dc.DrawRectangle(Neon.PanelFill, null, new Rect(l.HalfWidth - boxWidth / 2, boxTop - l.S(12), boxWidth, boxHeight));
        }
        switch (run.State)
        {
            case RunState.Paused:
                l.Centered(dc, "PAUSED  (P to resume)", l.Height * 0.36, 30, Neon.TextAmber);
                break;
            case RunState.Respawning:
                l.Centered(dc, "WRECKED — RESPAWNING", l.Height * 0.30, 40, Neon.TextRed);
                l.Centered(dc, $"NEW CAR IN {Math.Max(0, run.StateTimer):0.0} s     SPARE CARS LEFT: {Math.Max(0, w.Player.SpareCars - 1)}",
                           l.Height * 0.30 + l.S(54), 20, Neon.TextWhite);
                break;
            case RunState.Completed:
                l.Centered(dc, "MISSION COMPLETE  (R to drive again)", l.Height * 0.30, 34, Neon.TextGreen);
                l.Centered(dc, $"SCORE {w.Score:00000}", l.Height * 0.30 + l.S(50), 26, Neon.TextAmber);
                break;
            case RunState.GameOver:
                l.Centered(dc, "GAME OVER  (R to restart)", l.Height * 0.30, 36, Neon.TextRed);
                l.Centered(dc, $"SCORE {w.Score:00000}", l.Height * 0.30 + l.S(50), 26, Neon.TextAmber);
                break;
        }
    }

    public static void DrawHelp(DrawingContext dc, HudLayout l)
    {
        double k = l.K, lineH = 22 * k;
        double width = Math.Min(l.Width - 20, 800 * k), height = HelpLines.Length * lineH + 30 * k;
        var r = new Rect(l.HalfWidth - width / 2, l.Height * 0.28, width, height);
        dc.DrawRectangle(Neon.PanelFill, Neon.Hud.Core, r);
        double y = r.Y + 16 * k;
        for (int i = 0; i < HelpLines.Length; i++)
        {
            l.Text(dc, HelpLines[i], r.X + 22 * k, y, i == 0 ? 17 : 14.5, i == 0 ? Neon.TextAmber : Neon.TextWhite, bold: i == 0);
            y += lineH;
        }
    }
}
