using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The boss's health bar under the mirror: name and phase above it, a tick at each phase threshold, the percentage as a number,
/// a red flash just after a hit, and words for shielded, armour open and closing in. Every state is a word, not just a colour.
/// </summary>
internal static class BossBar
{
    /// <summary>Height of the bar block in HUD units; the warning lines start below it.</summary>
    public const double Height = 34;
    const double Top = 100, Width = 520, FlashSeconds = 0.15;

    static readonly Pen TickPen = MakePen(Neon.TextWhite, 1.5);
    static World? _seenWorld;
    static double _lastDamage = double.NegativeInfinity;

    static Pen MakePen(Brush b, double w)
    {
        var p = new Pen(b, w);
        p.Freeze();
        return p;
    }

    /// <summary>The boss to show, or null when the bar is not on screen (not the boss stage, or the boss is destroyed).</summary>
    public static BossCar? Showing(MissionRun run) =>
        run.Director.Stage == MissionStage.Boss && run.Director.Boss is { State: not VehicleState.Wrecked } boss ? boss : null;

    public static void Draw(DrawingContext dc, HudLayout l, MissionRun run)
    {
        if (Showing(run) is not { } boss) return;
        var w = run.World;
        NoteDamage(w);
        double k = l.K;
        var box = new Rect(l.HalfWidth - Width / 2 * k, Top * k, Width * k, Height * k);
        dc.DrawRectangle(Neon.PanelFill, null, new Rect(box.X - 4 * k, box.Y - 2 * k, box.Width + 8 * k, box.Height + 4 * k));

        int n = boss.PhaseIndex + 1, m = boss.Definition.Phases.Count;
        l.Text(dc, $"{boss.Definition.Name.ToUpperInvariant()}  PHASE {n}/{m}: {boss.Phase.Behaviour.ToString().ToUpperInvariant()}",
               box.X, box.Y, 12, Neon.TextWhite, bold: true);

        // Status words, right-aligned and stacked from the right edge (Consolas is about 0.55 em wide).
        double right = box.Right;
        void Status(string text, Brush brush)
        {
            l.Text(dc, text, right, box.Y, 12, brush, TextAlignment.Right, true);
            right -= (text.Length + 2) * 0.55 * 12 * k;
        }
        if (boss.Shield > 0) Status("SHIELDED", Neon.TextCyan);
        if (boss.ArmorOpen) Status("ARMOR OPEN ×2", Neon.TextGold);
        if (!boss.Engaged) Status("CLOSING IN", Neon.TextAmber);

        double percentWidth = 54 * k;
        var bar = new Rect(box.X, box.Y + 18 * k, box.Width - percentWidth, 12 * k);
        bool flash = w.Time - _lastDamage is >= 0 and <= FlashSeconds;
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, bar);
        double frac = Math.Clamp(boss.HealthFraction, 0, 1);
        dc.DrawRectangle(flash ? Neon.TextWhite : Neon.TextRed, null, new Rect(bar.X, bar.Y, bar.Width * frac, bar.Height));
        if (boss.Shield > 0) dc.DrawRectangle(null, new Pen(Neon.TextCyan, 2 * k), bar);
        for (int i = 0; i < m - 1; i++)   // a tick where each phase ends
        {
            double x = bar.X + bar.Width * boss.Definition.Phases[i].EndsBelow;
            dc.DrawLine(TickPen, new Point(x, bar.Y - 2 * k), new Point(x, bar.Bottom + 2 * k));
        }
        l.Text(dc, $"{frac * 100:0}%", box.Right, bar.Y - 3 * k, 14, Neon.TextWhite, TextAlignment.Right, true);
    }

    /// <summary>Remembers when the boss last lost health (events live for one advance only, so the bar keeps the time).</summary>
    static void NoteDamage(World w)
    {
        if (!ReferenceEquals(w, _seenWorld))
        {
            _seenWorld = w;
            _lastDamage = double.NegativeInfinity;
        }
        foreach (var e in w.Events)
            if (e is BossDamaged { Amount: > 0 } d) _lastDamage = d.Time;
    }
}
