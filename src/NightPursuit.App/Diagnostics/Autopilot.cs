using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Diagnostics;

/// <summary>
/// Drives the car for diagnostics: cancels the road's turning and heads for the nearest lane with no wreck or car just
/// ahead, crabbing sideways toward it when nearly stopped, so a stopped wreck in its lane can't hold it up for good.
/// </summary>
public static class Autopilot
{
    public static ControlFrame Next(World w)
    {
        var p = w.Player;
        var feel = w.Feel;
        double lane = ClearLane(w);
        double steer;
        if (p.Speed < 3)
            steer = Math.Abs(lane - p.Lateral) > 0.3 ? Math.Sign(lane - p.Lateral) : 0;   // no turning power: slide round (D5)
        else
        {
            double rate = feel.TurnRateAt(p.Speed);
            double wantHeading = Math.Clamp(0.04 * (lane - p.Lateral), -0.08, 0.08);

            // Turn rate needed to follow the bend, hold the wanted nose angle, and (since the straightening
            // assist also works while steering) overcome the assist's pull back toward straight.
            double needed = w.Track.CurvatureAt(p.Distance) * p.Speed + (wantHeading - p.Heading) * 4;
            if (feel.AssistWhileSteering) needed += p.Heading * feel.Straighten;
            steer = rate > 0.01 ? Math.Clamp(needed / rate, -1, 1) : 0;
        }

        return new ControlFrame { Gas = true, PadConnected = true, PadSteer = steer };
    }

    /// <summary>The lane nearest the player (the centre one on a tie) with no wreck within 90 m ahead and no car within 60 m ahead; the centre lane when none is clear.</summary>
    static double ClearLane(World w)
    {
        var p = w.Player;
        bool Clear(double lane) =>
            !w.Hazards.Any(h => h is Wreck && h.Distance - p.Distance is > -3 and < 90 && Math.Abs(h.Lateral - lane) < 3)
            && !w.Agents.Any(a => a.Distance - p.Distance is > -3 and < 60 && Math.Abs(a.Lateral - lane) < 3);
        foreach (double lane in Track.LaneCentres.OrderBy(l => Math.Abs(l - p.Lateral)).ThenBy(Math.Abs))
            if (Clear(lane)) return lane;
        return 0;
    }
}
