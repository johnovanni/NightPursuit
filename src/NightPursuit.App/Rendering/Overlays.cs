using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Everything that tells the driver something happened or is about to: warnings with words and edge arrows,
/// the off-road banner, the damage flash, recent messages, the respawn and help overlays, and the flow's notice in play.
/// Blinking is a dim-to-bright pulse driven by world time, so a warning never disappears entirely.
/// </summary>
internal static class Overlays
{
    const double MessageLife = 2.5, MessageFade = 0.6;
    const double WarningTop = 108, WarningRow = 22, MessageRow = 22;
    const double BannerFadeIn = 0.2, BannerFadeOut = 0.4;

    static readonly int[] LeftOnly = { -1 }, RightOnly = { 1 };

    /// <summary>The controls help, shared by the intro box here and <see cref="Screens.ControlsScreen"/>. Key-help lines keep their mixed case.</summary>
    internal static IReadOnlyList<string> HelpLines { get; } = new[]
    {
        "NIGHT PURSUIT  -  CONTROLS",
        "",
        "DRIVE      W / Up gas     S / Down brake     A D / Left Right steer",
        "WEAPONS    Space guns (hold)   X / Ctrl drop rear weapon   Tab next rear weapon",
        "           F missile (locks on ahead)   V satellite strike",
        "           Steer into a car's side to knock it into a spin",
        "SPEED      C cruise     F2 ramp pedal     F3 speed lever",
        "           1-0 lever presets     mouse wheel moves the lever",
        "GAME       P / Esc pause   F1 controls (pauses)   R restart (practice only)",
        "HEAT       hurting civilians or police raises heat; drive clean to cool off",
        "GAMEPAD    right trigger gas     left trigger brake     left stick steer",
        "           A guns   B drop   X next rear   RB missile   LB strike   Y cruise",
        "           D-pad up/down lever   Start pause   restart: pause menu (practice)",
        "MENUS      A select   B back   Start resume",
        "",
        "Press gas to start.  F1 shows this again.",
    };

    /// <summary>The widest body line of the help (Consolas is monospaced, so the longest one); the bold title is shorter.</summary>
    static readonly string LongestHelpLine = HelpLines.Skip(1).MaxBy(s => s.Length)!;

    /// <summary>Draws exactly what <see cref="Warnings.Collect"/> says (the rules live in Core, where tests prove each one): words, and an edge arrow where it has one.</summary>
    public static void DrawWarnings(DrawingContext dc, HudLayout l, World w, bool bossBar)
    {
        bool bright = (w.Time * 3) % 1 < 0.5;
        double y = WarningsTop(l, bossBar);
        foreach (var warning in Warnings.Collect(w))
        {
            bool police = warning.Kind is WarningKind.PoliceAlongside or WarningKind.PoliceBehind;
            dc.PushOpacity(bright ? 1 : police ? 0.7 : 0.5);   // the police text stays easier to read in its dim half
            if (warning.ShowArrow) DrawEdgeArrow(dc, l, warning.Side);
            l.Centered(dc, warning.Text, y, 18, WarningBrush(warning.Kind));
            dc.Pop();
            y += l.T(WarningRow);
        }
    }

    /// <summary>Top of the first warning line in pixels: 108 units, or just under the boss bar while it shows (the bar grows with the text).</summary>
    static double WarningsTop(HudLayout l, bool bossBar) => WarningTop * l.K + (bossBar ? BossBar.BlockHeight(l) : 0);

    /// <summary>Where the messages start (pixels): below the last of the <see cref="Warnings.MaxShown"/> possible warning lines, so the two never overlap.</summary>
    static double MessagesTop(HudLayout l, bool bossBar) =>
        Math.Max(l.Height * 0.26, WarningsTop(l, bossBar) + Warnings.MaxShown * l.T(WarningRow) + 4 * l.K);

    /// <summary>How many message rows fit between <see cref="MessagesTop"/> and the aim mark (which starts near 0.44 of the height), 1 to 4; the oldest are left out.</summary>
    static int MessageRows(HudLayout l, bool bossBar) =>
        Math.Clamp((int)Math.Floor((l.Height * 0.43 - MessagesTop(l, bossBar)) / l.T(MessageRow)), 1, 4);

    static Brush WarningBrush(WarningKind kind) => kind switch
    {
        WarningKind.BlockerAhead => Neon.TextAmber,
        WarningKind.WreckAhead or WarningKind.HazardAhead => Neon.TextOrange,
        WarningKind.PoliceAlongside or WarningKind.PoliceBehind => Neon.TextLightBlue,
        WarningKind.SpeedTrapAhead => Neon.TextAmber,
        WarningKind.RoadblockAhead => Neon.TextRed,
        _ => Neon.TextRed,
    };

    /// <summary>
    /// The big banner for a boss, a boss phase or a strike, on a dark backing box: a title (34) and a line of detail (18), fading in over 0.2 s
    /// and out over the last 0.4 s. It sits at half the height (decision D22): below the messages and the aim mark, which are both above it.
    /// </summary>
    public static void DrawAnnouncement(DrawingContext dc, HudLayout l, World w, bool bossBar)
    {
        if (w.Announcement is not { } a) return;
        double k = l.K, kt = l.T(1);
        double fade = Math.Clamp(Math.Min((w.Time - a.Start) / BannerFadeIn, (a.Until - w.Time) / BannerFadeOut), 0, 1);
        var box = AnnouncementBox(l, a, bossBar);
        double top = box.Y;
        dc.PushOpacity(fade);
        dc.DrawRectangle(Neon.PanelFill, null, box);
        LineArt.Brackets(dc, box, 14 * k, LineArt.Gauge);
        l.Centered(dc, a.Title, top + 6 * kt, 34, Neon.TextAmber);
        if (a.Detail.Length > 0) l.Centered(dc, a.Detail, top + 52 * kt, 18, Neon.TextCyan);
        dc.Pop();
    }

    /// <summary>Where the announcement's backing box is: its height and width grow with the text.</summary>
    static Rect AnnouncementBox(HudLayout l, Announcement a, bool bossBar)
    {
        double messagesBottom = MessagesTop(l, bossBar) + MessageRows(l, bossBar) * l.T(MessageRow) + 4 * l.K;
        double top = Math.Max(l.Height * 0.50, messagesBottom), boxWidth = Math.Max(0, Math.Min(l.Width - 20, l.T(640)));
        return new Rect(l.HalfWidth - boxWidth / 2, top, boxWidth, l.T(a.Detail.Length > 0 ? 82 : 56));
    }

    /// <summary>
    /// The busted countdown, on a dark backing box at 0.70 of the height: between the off-road banner (0.62) and the hood (0.86), so it
    /// covers neither. The whole seconds left are written out; the amber line says how to cancel it.
    /// </summary>
    public static void DrawBusted(DrawingContext dc, HudLayout l, World w)
    {
        if (!w.Busted.Counting) return;
        double k = l.K, kt = l.T(1), boxWidth = Math.Max(0, Math.Min(l.Width - 20, 470 * k + l.Grow(60))), boxHeight = 88 * kt;   // wider at large text, but not so wide it covers the weapon panel
        // 0.70 of the height, but never lower than the hood allows once the box has grown with the text.
        double boxTop = Math.Min(l.Height * 0.70 - 8 * k, l.HoodTop - boxHeight - 2 * k), top = boxTop + 8 * kt;
        var box = new Rect(l.HalfWidth - boxWidth / 2, boxTop, boxWidth, boxHeight);
        dc.DrawRectangle(Neon.PanelFill, null, box);
        LineArt.Brackets(dc, box, 14 * k, LineArt.Gauge);
        l.Centered(dc, $"BUSTED IN {w.Busted.SecondsLeft}", top, 44, Neon.TextRed);
        l.Centered(dc, "SPEED UP TO BREAK FREE", top + 56 * kt, 18, Neon.TextAmber);
    }

    /// <summary>A chevron on the screen edge pointing out toward the side a warning comes from (both sides when it is dead behind): pursuers, missiles, blockers, the boss and hostiles.</summary>
    static void DrawEdgeArrow(DrawingContext dc, HudLayout l, int side)
    {
        // The left chevron sits below the weapon panel, which owns the left edge down to its bottom; the right one clears the radar's top edge.
        double k = l.K, rightY = Math.Min(l.Height * 0.55, l.Height - 332 * k), leftY = Math.Max(rightY, WeaponPanel.Bounds(l).Bottom + 34 * k);
        foreach (int dir in side == 0 ? Projector.Sides : side < 0 ? LeftOnly : RightOnly)
        {
            double ax = dir < 0 ? 18 * k : l.Width - 18 * k, y = dir < 0 ? leftY : rightY;
            var arrow = new LineBatch();
            arrow.Line(new Point(ax - dir * 22 * k, y - 26 * k), new Point(ax, y));
            arrow.Line(new Point(ax, y), new Point(ax - dir * 22 * k, y + 26 * k));
            arrow.Draw(dc, Neon.PursuerCar);
        }
    }

    public static void DrawMessages(DrawingContext dc, HudLayout l, World w, bool bossBar)
    {
        double y = MessagesTop(l, bossBar);
        int rows = MessageRows(l, bossBar);
        foreach (var e in w.RecentMessages)
        {
            if (e is not Message m) continue;
            if (rows-- <= 0) break;
            var brush = m.Tone switch { Tone.Good => Neon.TextGreen, Tone.Bad => Neon.TextRed, _ => Neon.TextCyan };
            double left = MessageLife - (w.Time - m.Time);
            dc.PushOpacity(Math.Clamp(left / MessageFade, 0, 1));
            l.Centered(dc, m.Text, y, 18, brush);
            dc.Pop();
            y += l.T(MessageRow);
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
        {
            // 0.62 of the height, moved up as the busted box (below it) grows with the text, and kept under an announcement box if one is showing.
            double y = l.Height * 0.62 - l.Grow(26);
            if (run.World.Announcement is { } a) y = Math.Max(y, AnnouncementBox(l, a, BossBar.Showing(run) is not null).Bottom + 4 * l.K);
            l.Centered(dc, p.Lateral < 0 ? "<< OFF ROAD" : "OFF ROAD >>", y, 26, Neon.TextAmber);
        }
    }

    /// <summary>
    /// The respawn countdown, on a dark backing box so it stays readable over the help panel or bright scenery. Paused, mission
    /// complete and game over are screens now (ScreenHost), so nothing is drawn for them here.
    /// </summary>
    public static void DrawState(DrawingContext dc, HudLayout l, MissionRun run)
    {
        if (run.State != RunState.Respawning) return;
        var w = run.World;
        double boxWidth = Math.Min(l.Width - 20, l.T(760));
        dc.DrawRectangle(Neon.PanelFill, null, new Rect(l.HalfWidth - boxWidth / 2, l.Height * 0.30 - l.T(12), boxWidth, l.T(110)));
        l.Centered(dc, "WRECKED — RESPAWNING", l.Height * 0.30, 40, Neon.TextRed);
        l.Centered(dc, $"NEW CAR IN {Math.Max(0, run.StateTimer):0.0} s     SPARE CARS LEFT: {Math.Max(0, w.Player.SpareCars - 1)}",
                   l.Height * 0.30 + l.T(54), 20, Neon.TextWhite);
    }

    /// <summary>
    /// The screen flow's notice in play (a failed save, for example), in amber on a dark box just above the hood: below the busted
    /// countdown's box, so the two do not overlap (in any window 288 px tall or more, where K is not clamped). A notice too wide for the window is drawn smaller rather than cut off.
    /// </summary>
    public static void DrawNotice(DrawingContext dc, HudLayout l, string notice)
    {
        double k = l.T(1), size = 18, room = Math.Max(1, l.Width - 40);
        double width = l.TextWidth(notice, size, bold: true);
        if (width > room) { size = Math.Max(8 / k, size * room / width); width = l.TextWidth(notice, size, bold: true); }
        double y = l.HoodTop - l.T(30), boxWidth = Math.Min(l.Width - 20, width + 40 * k);
        dc.DrawRectangle(Neon.PanelFill, null, new Rect(l.HalfWidth - boxWidth / 2, y - 4 * k, Math.Max(0, boxWidth), 30 * k));
        l.Centered(dc, notice, y, size, Neon.TextAmber);
    }

    /// <summary>
    /// The controls help. Its box sits between the weapon panel and (where they share rows) the radar, 10 px clear of each,
    /// as near the centre as it can, and up to 800 px wide. When that gap is too narrow for the longest line plus its
    /// padding (tiny windows), it is centred over everything instead, so no line is ever cut off.
    /// </summary>
    public static void DrawHelp(DrawingContext dc, HudLayout l)
    {
        double k = l.K, kt = l.T(1), lineH = 22 * k * (1 + 0.6 * (l.TextScale - 1)), pad = 22 * kt, margin = 10 * k;   // lines grow less than the text, so the taller box still fits
        // 0.28 of the height; at a large text size the taller box moves up so its bottom stays on screen.
        double height = HelpLines.Count * lineH + 30 * kt;
        double top = Math.Max(Math.Min(l.Height * 0.28, l.Height - height - margin), Mirror.Bounds(l).Bottom + 4 * k);
        double left = WeaponPanel.Bounds(l).Right + margin, right = l.Width - margin;
        var radar = Radar.Bounds(l);
        if (top + height > radar.Top) right = Math.Min(right, radar.Left - margin);
        double needed = l.TextWidth(LongestHelpLine, 14.5) + 2 * pad;
        Rect r;
        if (right - left >= needed)
        {
            double width = Math.Min(right - left, 800 * k);
            r = new Rect(Math.Clamp(l.HalfWidth - width / 2, left, right - width), top, width, height);
        }
        else
        {
            double width = Math.Min(l.Width - 20, Math.Max(800 * k, needed));   // wide enough for the longest line at any text size
            r = new Rect(l.HalfWidth - width / 2, top, width, height);
        }
        dc.DrawRectangle(Neon.PanelFill, null, r);
        LineArt.Brackets(dc, r, 14 * k, LineArt.Gauge);
        double y = r.Y + 16 * kt;
        for (int i = 0; i < HelpLines.Count; i++)
        {
            l.Text(dc, HelpLines[i], r.X + pad, y, i == 0 ? 17 : 14.5, i == 0 ? Neon.TextAmber : Neon.TextWhite, bold: i == 0);
            y += lineH;
        }
    }
}
