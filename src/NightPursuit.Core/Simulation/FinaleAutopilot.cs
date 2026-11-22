using NightPursuit.Core.Control;

namespace NightPursuit.Core.Simulation;

/// <summary>
/// Drives the player's car during the boss finale: it holds its lane and eases its speed so the crash stays ahead,
/// about 55 m away, braking up to an emergency stop when it is too close. Your own controls are ignored.
/// </summary>
internal static class FinaleAutopilot
{
    const double TargetGap = 55;
    const double GapGain = 0.8;
    /// <summary>How far short of the boss's resting place the car stops, metres.</summary>
    const double StopShort = 45;
    /// <summary>Total braking in the finale, m/s²: an emergency stop, harder than the brake pedal's 14.</summary>
    public const double HardBrake = 30;

    /// <summary>The pedals and steering for this step, and any braking beyond the pedal's (m/s²) the World adds on top.</summary>
    public static (PedalOutput Pedals, double Steer, double ExtraDecel) Drive(World w, double laneCentre)
    {
        var p = w.Player;
        var f = w.Finale!;
        double gap = f.Distance - p.Distance;
        double desired = Math.Clamp(f.Speed + GapGain * (gap - TargetGap), 0, w.Feel.MaxSpeed);
        // Never faster than lets the car stop StopShort before where the boss will come to rest and blow up.
        double room = f.RestDistance - StopShort - p.Distance;
        desired = Math.Min(desired, Math.Sqrt(2 * 0.9 * HardBrake * Math.Max(0, room)));

        PedalOutput pedals;
        double extra = 0;
        if (p.Speed < desired) pedals = new PedalOutput(Math.Clamp((desired - p.Speed) / 5, 0, 1), 0);
        else
        {
            double brake = Math.Clamp((p.Speed - desired) / 5, 0, 1);
            pedals = new PedalOutput(0, brake);
            extra = brake * Math.Max(0, HardBrake - w.Feel.BrakeDecel);
        }

        double steer = Math.Clamp(0.4 * (laneCentre - p.Lateral) - 2.0 * p.Heading, -1, 1);
        return (pedals, steer, extra);
    }
}
