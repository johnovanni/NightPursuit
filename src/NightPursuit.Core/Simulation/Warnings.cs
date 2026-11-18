using System.Globalization;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

/// <summary>What a warning is about. The order of declaration is the priority: earlier kinds win when more than four apply.</summary>
public enum WarningKind { MissileIncoming, PursuerAlongside, GunnerAiming, HazardAhead, WreckAhead, BlockerAhead, BossBehind, HostileBehind, PursuerBehind }

/// <summary>One warning for the HUD. <paramref name="Side"/> is -1 left, +1 right, or 0 for both edges; the edge arrow shows only when <paramref name="ShowArrow"/>.</summary>
public readonly record struct Warning(WarningKind Kind, string Text, int Side, bool ShowArrow);

/// <summary>Works out which warnings are on screen. This is the single source of truth: the HUD draws exactly this list.</summary>
public static class Warnings
{
    public const int MaxShown = 4;
    const double PursuerRange = 120, AlongsideAhead = 4, AlongsideZone = 3, AheadRange = 150;
    const double WreckLane = 2.2, BlockerLane = 2, OilLane = 2.5, GunnerLane = 2, SideDeadZone = 1;
    const double GunnerRange = 120, BossRange = 120, HostileRange = 60, HostileMinGap = 3, HostileClosing = 3;

    /// <summary>At most <see cref="MaxShown"/> warnings, highest priority first (one per kind).</summary>
    public static IReadOnlyList<Warning> Collect(World w)
    {
        var p = w.Player;
        var found = new Warning?[Enum.GetValues<WarningKind>().Length];

        // The soonest enemy missile.
        EnemyMissile? missile = null;
        foreach (var m in w.EnemyMissiles)
            if (!m.Dead && (missile is null || m.TimeToImpact(p) < missile.TimeToImpact(p))) missile = m;
        if (missile is not null)
        {
            int side = SideOf(missile.Lateral - p.Lateral);
            string where = missile.Dir > 0 ? "AHEAD" : "BEHIND";
            string text = $"MISSILE INCOMING {where}{(side == 0 ? "" : side < 0 ? " LEFT" : " RIGHT")}  IMPACT {missile.TimeToImpact(p).ToString("0.0", CultureInfo.InvariantCulture)} s";
            found[(int)WarningKind.MissileIncoming] = new Warning(WarningKind.MissileIncoming, text, side, true);
        }

        AgentCar? pursuer = null, blocker = null, gunner = null, hostile = null;
        BossCar? boss = null;
        foreach (var e in w.Agents)
        {
            if (e.State != VehicleState.Driving) continue;
            double dz = e.Distance - p.Distance;
            if (e is BossCar b)
            {
                if (dz < -HostileMinGap && dz > -BossRange && (boss is null || dz > boss.Distance - p.Distance)) boss = b;
            }
            else if (e.Kind == AgentKind.Pursuer)
            {
                if (dz < AlongsideAhead && dz > -PursuerRange && (pursuer is null || dz > pursuer.Distance - p.Distance)) pursuer = e;
            }
            else
            {
                if (e.Kind == AgentKind.Blocker && dz > 0 && dz < AheadRange && Math.Abs(e.Lateral - p.Lateral) < BlockerLane
                    && (blocker is null || dz < blocker.Distance - p.Distance)) blocker = e;
                if (e.Kind == AgentKind.Gunner && GunnerDriver.IsAiming(e) && dz > 0 && dz <= GunnerRange
                    && Math.Abs(e.Lateral - p.Lateral) < GunnerLane && (gunner is null || dz < gunner.Distance - p.Distance)) gunner = e;
                if (dz <= -HostileMinGap && dz >= -HostileRange && e.Speed - p.Speed > HostileClosing
                    && (hostile is null || dz > hostile.Distance - p.Distance)) hostile = e;
            }
        }

        if (pursuer is not null)
        {
            double dx = pursuer.Lateral - p.Lateral;
            int side = SideOf(dx);
            double dist = p.Distance - pursuer.Distance;
            if (dist < AlongsideZone)     // beside you or slightly ahead: it is about to ram, so no distance
            {
                string where = side == 0 ? (dx < 0 ? "LEFT" : "RIGHT") : side < 0 ? "LEFT" : "RIGHT";
                found[(int)WarningKind.PursuerAlongside] = new Warning(WarningKind.PursuerAlongside, $"PURSUER {where}", side, true);
            }
            else
            {
                string text = side == 0 ? $"PURSUER BEHIND  {Metres(dist)} m" : $"PURSUER BEHIND {(side < 0 ? "LEFT" : "RIGHT")}  {Metres(dist)} m";
                found[(int)WarningKind.PursuerBehind] = new Warning(WarningKind.PursuerBehind, text, side, true);
            }
        }
        if (gunner is not null)
            found[(int)WarningKind.GunnerAiming] = new Warning(WarningKind.GunnerAiming, "GUNNER AIMING - LEAVE ITS LANE", 0, false);
        if (blocker is not null)
        {
            int side = SideOf(blocker.Lateral - p.Lateral);
            found[(int)WarningKind.BlockerAhead] = new Warning(WarningKind.BlockerAhead, "BLOCKER AHEAD", side, true);
        }
        if (boss is not null)
            found[(int)WarningKind.BossBehind] = Behind(WarningKind.BossBehind, "BOSS", boss, p);
        if (hostile is not null)
            found[(int)WarningKind.HostileBehind] = Behind(WarningKind.HostileBehind, hostile.Kind.ToString().ToUpperInvariant(), hostile, p);

        double hazardGap = double.MaxValue, wreckGap = double.MaxValue;
        string? hazardText = null;
        foreach (var h in w.Hazards)
        {
            double dz = h.Distance - p.Distance;
            if (dz <= 0 || dz >= AheadRange) continue;
            double dl = Math.Abs(h.Lateral - p.Lateral);
            if (h is Wreck) { if (dl < WreckLane && dz < wreckGap) wreckGap = dz; }
            else if (h.Hostile && h is OilSlick) { if (dl < OilLane && dz < hazardGap) { hazardGap = dz; hazardText = "OIL AHEAD"; } }
            else if (h.Hostile && h is Caltrops && dl < Caltrops.HalfWidth + 1 && dz < hazardGap) { hazardGap = dz; hazardText = "CALTROPS AHEAD"; }
        }
        if (hazardText is not null) found[(int)WarningKind.HazardAhead] = new Warning(WarningKind.HazardAhead, hazardText, 0, false);
        if (wreckGap < double.MaxValue) found[(int)WarningKind.WreckAhead] = new Warning(WarningKind.WreckAhead, "WRECK AHEAD", 0, false);

        var result = new List<Warning>(MaxShown);
        foreach (var f in found)
            if (f is { } warning && result.Count < MaxShown) result.Add(warning);
        return result;
    }

    static Warning Behind(WarningKind kind, string name, AgentCar car, PlayerCar p)
    {
        int side = SideOf(car.Lateral - p.Lateral);
        string text = side == 0 ? $"{name} BEHIND {Metres(p.Distance - car.Distance)} m"
            : $"{name} BEHIND {(side < 0 ? "LEFT" : "RIGHT")} {Metres(p.Distance - car.Distance)} m";
        return new Warning(kind, text, side, true);
    }

    static string Metres(double d) => d.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>-1 left, +1 right, 0 within 1 m of the player's lane (the dead zone).</summary>
    static int SideOf(double dx) => dx < -SideDeadZone ? -1 : dx > SideDeadZone ? 1 : 0;
}
