using NightPursuit.Core.Control;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Diagnostics;

/// <summary>Drives the car for diagnostics: cancels the road's turning and drifts back toward the centre lane.</summary>
public static class Autopilot
{
    public static ControlFrame Next(World w)
    {
        var p = w.Player;
        var feel = w.Feel;
        double rate = feel.TurnRateAt(p.Speed);
        double wantHeading = Math.Clamp(-0.04 * p.Lateral, -0.08, 0.08);

        // Turn rate needed to follow the bend, hold the wanted nose angle, and (since the straightening
        // assist also works while steering) overcome the assist's pull back toward straight.
        double needed = w.Track.CurvatureAt(p.Distance) * p.Speed + (wantHeading - p.Heading) * 4;
        if (feel.AssistWhileSteering) needed += p.Heading * feel.Straighten;

        return new ControlFrame
        {
            Gas = true,
            PadConnected = true,
            PadSteer = rate > 0.01 ? Math.Clamp(needed / rate, -1, 1) : 0,
        };
    }
}
