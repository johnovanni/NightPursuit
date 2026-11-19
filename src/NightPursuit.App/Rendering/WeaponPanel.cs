using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>
/// The weapon readouts down the left edge: guns, the three rear weapons, missiles and the satellite strike.
/// Every value is a pip or bar plus a word, so nothing depends on colour alone.
/// </summary>
internal static class WeaponPanel
{
    static readonly Pen SelectPen = MakePen(Neon.TextWhite, 2), SmokePen = MakePen(Neon.TextGray, 1.2),
        CaltropPen = MakePen(Neon.TextSteel, 1.2), MissilePen = MakePen(Neon.TextWhite, 1.2);

    static Pen MakePen(Brush b, double w)
    {
        var p = new Pen(b, w);
        p.Freeze();
        return p;
    }

    /// <summary>Screen rectangle behind the panel: left edge, from 150 down, 250 wide (at 1280x720 scale).</summary>
    public static Rect Bounds(HudLayout l) => new(8 * l.K, 150 * l.K, 250 * l.K, 276 * l.K);

    public static void Draw(DrawingContext dc, HudLayout l, World w)
    {
        double k = l.K;
        var b = Bounds(l);
        dc.DrawRectangle(Neon.PanelFill, Neon.Hud.Core, b);
        double x = b.X + 10 * k, right = b.Right - 10 * k, y = b.Y + 6 * k;
        var lo = w.Loadout;

        // GUNS: heat bar, red and spelled out when locked.
        var guns = lo.Guns;
        if (guns.Locked) l.Text(dc, "GUNS LOCKED", x, y, 13, Neon.TextRed, bold: true);
        else l.Text(dc, "GUN HEAT", x, y, 13, Neon.TextGray);
        var heat = new Rect(x, y + 18 * k, right - x, 8 * k);
        dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, heat);
        dc.DrawRectangle(guns.Locked ? Neon.TextRed : Neon.TextAmber, null, new Rect(heat.X, heat.Y, heat.Width * Math.Clamp(guns.Heat, 0, 1), heat.Height));
        y += 36 * k;

        // REAR: one row per weapon.
        l.Text(dc, "REAR", x, y, 13, Neon.TextCyan, bold: true);
        l.Text(dc, "X drop   Tab next", right, y, 12, Neon.TextGray, TextAlignment.Right);
        y += 18 * k;
        DrawRearRow(dc, l, "OIL", lo.Oil, lo.SelectedRear == RearWeapon.Oil, Neon.OilFull, Neon.Oil.Core, x, right, y, showRecharge: true);
        y += 28 * k;
        DrawRearRow(dc, l, "SMOKE", lo.Smoke, lo.SelectedRear == RearWeapon.Smoke, Neon.TextGray, SmokePen, x, right, y, showRecharge: false);
        y += 28 * k;
        DrawRearRow(dc, l, "CALTROPS", lo.Caltrops, lo.SelectedRear == RearWeapon.Caltrops, Neon.TextSteel, CaltropPen, x, right, y, showRecharge: false);
        y += 32 * k;

        // MISSILES: pips, the key, and what a shot would lock onto.
        var missiles = lo.Missiles;
        l.Text(dc, "MISSILES", x, y, 13, Neon.TextCyan, bold: true);
        for (int i = 0; i < missiles.Capacity; i++)
            DrawPip(dc, l, new Point(x + 90 * k + i * 16 * k, y + 8 * k), i < missiles.Charges, Neon.TextWhite, MissilePen);
        l.Text(dc, "F", right, y, 13, Neon.TextAmber, TextAlignment.Right, true);
        y += 18 * k;
        var target = w.Player.State == VehicleState.Wrecked || missiles.Charges <= 0 ? null : w.FindMissileTarget();
        if (target is not null)
            l.Text(dc, $"LOCK: {target.Kind.ToString().ToUpperInvariant()} {target.Distance - w.Player.Distance:0} m", x, y, 13, Neon.TextGreen, bold: true);
        else
            l.Text(dc, missiles.Charges <= 0 ? "NO MISSILES" : "NO TARGET IN SIGHT", x, y, 13, Neon.TextGray);
        y += 28 * k;

        // SAT STRIKE
        var strike = lo.Strike;
        l.Text(dc, "SAT STRIKE", x, y, 13, Neon.TextCyan, bold: true);
        if (strike.Targeting)
        {
            l.Text(dc, $"TARGETING {strike.SweepLeft:0.0} s", right, y, 13, Neon.TextAmber, TextAlignment.Right, true);
            var bar = new Rect(x, y + 18 * k, right - x, 8 * k);
            dc.DrawRectangle(Neon.HudBack, Neon.Hud.Core, bar);
            double done = 1 - Math.Clamp(strike.SweepLeft / SatelliteStrike.SweepSeconds, 0, 1);
            dc.DrawRectangle(Neon.TextAmber, null, new Rect(bar.X, bar.Y, bar.Width * done, bar.Height));
        }
        else if (strike.Charges > 0) l.Text(dc, "V  READY", right, y, 13, Neon.TextGreen, TextAlignment.Right, true);
        else l.Text(dc, "NONE", right, y, 13, Neon.TextGray, TextAlignment.Right);
    }

    static void DrawRearRow(DrawingContext dc, HudLayout l, string name, ChargedWeapon weapon, bool selected, Brush fill, Pen pen,
                            double x, double right, double y, bool showRecharge)
    {
        double k = l.K;
        if (selected)
        {
            dc.DrawRectangle(null, SelectPen, new Rect(x - 4 * k, y - 3 * k, right - x + 8 * k, 24 * k));
            l.Text(dc, "▶", x, y, 13, Neon.TextWhite, bold: true);
        }
        l.Text(dc, name, x + 16 * k, y, 13, selected ? Neon.TextWhite : Neon.TextGray, bold: selected);
        for (int i = 0; i < weapon.Capacity; i++)
            DrawPip(dc, l, new Point(x + 92 * k + i * 14 * k, y + 8 * k), i < weapon.Charges, fill, pen, 5);
        if (showRecharge && weapon.Charges < weapon.Capacity)
        {
            var bar = new Rect(x + 86 * k, y + 17 * k, weapon.Capacity * 14 * k, 2.5 * k);
            dc.DrawRectangle(Neon.HudBack, null, bar);
            dc.DrawRectangle(Neon.TextCyan, null, new Rect(bar.X, bar.Y, bar.Width * weapon.RechargeProgress, bar.Height));
        }
        if (weapon.Charges <= 0) l.Text(dc, "EMPTY", right - 2 * k, y, 12, Neon.TextRed, TextAlignment.Right, true);
        else if (weapon.CooldownLeft > 0) l.Text(dc, "WAIT", right - 2 * k, y, 12, Neon.TextAmber, TextAlignment.Right, true);
    }

    static void DrawPip(DrawingContext dc, HudLayout l, Point c, bool filled, Brush fill, Pen pen, double radius = 6) =>
        dc.DrawEllipse(filled ? fill : null, pen, c, radius * l.K, radius * l.K);
}
