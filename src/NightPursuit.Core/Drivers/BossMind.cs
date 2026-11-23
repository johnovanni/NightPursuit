using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// How a boss's cunning (1-6, from its mission file) shapes it: how soon it notices you hanging back and how fast it
/// runs. Mission 1's boss is slow to react; mission 6's reacts in a second.
/// </summary>
public static class BossCunning
{
    static readonly double[] React = [3.0, 2.5, 2.0, 1.5, 1.0, 1.0];
    static readonly double[] Pace = [56, 57, 59, 60, 62, 63];   // 125 -> 140 mph

    static int Index(int cunning) => Math.Clamp(cunning, 1, 6) - 1;

    /// <summary>Seconds you must hang back before it reacts.</summary>
    public static double ReactSeconds(int cunning) => React[Index(cunning)];
    /// <summary>Its speed when it makes a run for it, m/s.</summary>
    public static double RunPace(int cunning) => Pace[Index(cunning)];
}

/// <summary>
/// The boss's own will, between its phase behaviour and the road: while it is ahead of you it never follows you below
/// 90 mph, so slowing down or stopping can't park it in your sights (owner, 2026-11-23). Above that its phases pace you
/// as they always have. Level with you or behind, its phase drives as before: there it is out of your sights, and its
/// passing moves must be free to stay slower than you rather than drive into you.
/// </summary>
public sealed class BossMind
{
    /// <summary>An engaged boss never follows you below this, m/s (90 mph).</summary>
    public const double FloorSpeed = 40;
    /// <summary>Below this you are hanging back, m/s (45 mph).</summary>
    public const double HangBackSpeed = 20;

    /// <summary>
    /// The phase's intent, held at or above the floor while the boss is ahead of you. A Pursuer phase dropping back for
    /// its next attack may go 8 m/s under the floor, so it can still get behind a player driving at 90 mph.
    /// </summary>
    public DriveIntent Apply(BossCar b, WorldView view, DriveIntent phase, double dt)
    {
        if (b.Distance <= view.Player.Distance) return phase;
        double floor = b.DriverState is BossPursuitDriver.State { Phase: BossPursuitPhase.DropBack }
            ? FloorSpeed - BossPursuitDriver.DropBackSlower
            : FloorSpeed;
        return phase with { TargetSpeed = Math.Max(phase.TargetSpeed, floor) };
    }

    /// <summary>Forgets everything (a respawn puts the boss back to ease in again).</summary>
    public void Reset() { }
}
