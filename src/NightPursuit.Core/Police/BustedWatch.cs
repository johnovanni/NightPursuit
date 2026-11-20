using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Police;

/// <summary>
/// The busted countdown: at Pursuit or above, a working police car within <see cref="Radius"/> m while you are slower
/// than 5 mph for <see cref="Seconds"/> s busts you. It counts whole steps, so 3 s is exactly 360 steps, and it does
/// not count until the player has first moved above 5 mph in this car (<see cref="World.BustedArmed"/>).
/// </summary>
public sealed class BustedWatch
{
    public const double Seconds = 3, SpeedLimit = 2.235, Radius = 15;
    static readonly int BustSteps = (int)Math.Round(Seconds / World.Step);

    int _steps;

    /// <summary>Seconds held so far; 0 when not counting.</summary>
    public double Held => _steps * World.Step;
    public bool Counting => _steps > 0;
    public int SecondsLeft => (int)Math.Ceiling(Seconds - Held);

    /// <summary>Runs <paramref name="dt"/> seconds (whole steps); true on the step the bust happens.</summary>
    public bool Update(World w, double dt)
    {
        int steps = (int)Math.Round(dt / World.Step);
        for (int i = 0; i < steps; i++)
            if (UpdateStep(w)) return true;
        return false;
    }

    bool UpdateStep(World w)
    {
        var p = w.Player;
        bool driving = p.State == VehicleState.Driving;
        if (driving && p.Speed > SpeedLimit) w.BustedArmed = true;

        bool counts = driving && w.BustedArmed && w.Heat.Level >= HeatLevel.Pursuit && p.Speed < SpeedLimit && PoliceClose(w);
        if (!counts)
        {
            // Wrecked, busted or respawning is not "breaking free"; only a car that was still on the road is.
            if (_steps > 0 && driving) w.Publish(new Message(w.Time, "BROKE FREE", Tone.Good));
            _steps = 0;
            return false;
        }
        _steps++;
        if (_steps < BustSteps) return false;
        _steps = 0;
        return true;
    }

    static bool PoliceClose(World w)
    {
        foreach (var a in w.Agents)
        {
            if (a.Faction != Faction.Police || a.State != VehicleState.Driving || a.Blinded > 0) continue;
            double dz = a.Distance - w.Player.Distance, dl = a.Lateral - w.Player.Lateral;
            if (Math.Sqrt(dz * dz + dl * dl) <= Radius) return true;
        }
        return false;
    }

    /// <summary>Stops counting without a message (a respawn).</summary>
    public void Reset() => _steps = 0;

    /// <summary>DIAGNOSTICS ONLY (stress, scenes, tests): places the timer at <paramref name="seconds"/>, rounded to whole steps and below the bust.</summary>
    public void SetHeldForDiagnostics(double seconds) =>
        _steps = Math.Clamp((int)Math.Round(seconds / World.Step), 0, BustSteps - 1);
}
