using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>What a driver is currently doing about wrecks, kept in its per-car state.</summary>
public sealed class AvoidMemory
{
    /// <summary>True while the car is steering to <see cref="Lateral"/> instead of its own wish.</summary>
    public bool Active { get; set; }
    public double Lateral { get; set; }
    /// <summary>How long the new answer has been waiting to take over.</summary>
    public double Timer { get; set; }
}

public static class DriverHelpers
{
    const double Margin = 0.3;
    const double Same = 1e-9;

    /// <summary>
    /// Returns <paramref name="desiredLateral"/> if no wreck within <paramref name="lookAhead"/> metres ahead blocks it;
    /// otherwise the clear lane centre nearest to it (ties go to the one nearest the car), or the desire again if every lane is blocked.
    /// </summary>
    public static double AvoidWrecks(AgentCar self, WorldView view, double desiredLateral, double lookAhead = 120)
    {
        if (!IsBlocked(self, view, desiredLateral, lookAhead)) return desiredLateral;

        double best = desiredLateral;
        bool found = false;
        foreach (double lane in Track.LaneCentres)
        {
            if (IsBlocked(self, view, lane, lookAhead)) continue;
            if (!found || IsBetter(lane, best, desiredLateral, self.Lateral))
            {
                best = lane;
                found = true;
            }
        }
        return best;
    }

    static bool IsBetter(double lane, double best, double desired, double current)
    {
        double a = Math.Abs(lane - desired), b = Math.Abs(best - desired);
        if (Math.Abs(a - b) > Same) return a < b;
        return Math.Abs(lane - current) < Math.Abs(best - current);
    }

    static bool IsBlocked(AgentCar self, WorldView view, double lateral, double lookAhead)
    {
        foreach (var h in view.Hazards)
        {
            if (h is not Wreck wreck) continue;
            double ahead = wreck.Distance - self.Distance;
            if (ahead <= 0 || ahead >= lookAhead) continue;
            if (Math.Abs(wreck.Lateral - lateral) < (wreck.Width + self.Stats.Width) / 2 + Margin) return true;
        }
        return false;
    }

    /// <summary>
    /// Passes a wish through <see cref="AvoidWrecks"/> with a reaction delay: when the avoiding answer changes
    /// (a wreck appears or is passed) the car keeps its previous steering until <paramref name="reactionDelay"/> seconds have gone by.
    /// </summary>
    public static double SteerAroundWrecks(AgentCar self, WorldView view, double desiredLateral,
        AvoidMemory memory, double reactionDelay, double dt)
    {
        double avoided = AvoidWrecks(self, view, desiredLateral);
        bool swerving = Math.Abs(avoided - desiredLateral) > Same;
        bool unchanged = swerving == memory.Active && (!swerving || Math.Abs(avoided - memory.Lateral) < Same);
        if (unchanged)
            memory.Timer = 0;
        else
        {
            memory.Timer += dt;
            if (memory.Timer >= reactionDelay - Same)
            {
                memory.Active = swerving;
                memory.Lateral = avoided;
                memory.Timer = 0;
            }
        }
        return memory.Active ? memory.Lateral : desiredLateral;
    }
}
