using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>
/// Watches for the boss getting away: more than 600 m ahead (just past your guns' reach) it warns and counts down 20 s;
/// it escapes when the count runs out or the moment it is 1,200 m ahead. Back within 600 m the count starts over. It only
/// watches an engaged boss; the run steps it only while Playing, so respawns, pauses and the finale never count.
/// </summary>
public sealed class BossEscapeWatch
{
    public const double WarnBeyond = 600, EscapeAt = 1200, Seconds = 20;

    /// <summary>True while the boss is beyond 600 m and the countdown runs.</summary>
    public bool Warning { get; private set; }
    /// <summary>Seconds left before it escapes (20 when not warning).</summary>
    public double SecondsLeft { get; private set; } = Seconds;
    /// <summary>How far ahead of you the boss is, metres (0 with no engaged boss).</summary>
    public double Gap { get; private set; }

    /// <summary>One fixed step. True on the step the boss escapes.</summary>
    public bool Update(World w, BossCar? boss, double dt)
    {
        if (boss is null || !boss.Engaged || boss.State == VehicleState.Wrecked || w.InFinale)
        {
            Gap = 0;
            Clear();
            return false;
        }
        Gap = boss.Distance - w.Player.Distance;
        if (Gap >= EscapeAt)
        {
            Warning = true;
            SecondsLeft = 0;
            return true;
        }
        if (Gap <= WarnBeyond)
        {
            Clear();
            return false;
        }
        Warning = true;
        SecondsLeft = Math.Max(0, SecondsLeft - dt);
        return SecondsLeft <= 1e-9;
    }

    void Clear()
    {
        Warning = false;
        SecondsLeft = Seconds;
    }
}
