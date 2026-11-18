using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

public enum PursuerPhase { Approach, Attack, Retreat }

/// <summary>Closes in from behind, rams alongside for a few seconds, then drops back and tries again from a random side.</summary>
public sealed class PursuerDriver : IDriver
{
    const double ReactionDelay = 0.3;
    const double RetreatClearance = 20;

    public sealed class State
    {
        public PursuerPhase Phase { get; set; } = PursuerPhase.Approach;
        public double Timer { get; set; }
        public double Target { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
            self.DriverState = s = new State { Target = self.Lateral };

        var p = view.Player;
        double dz = self.Distance - p.Distance;
        double speed;
        switch (s.Phase)
        {
            case PursuerPhase.Approach:
                speed = Math.Min(85, p.Speed + 14);
                s.Target = Math.Clamp(p.Lateral + self.Side * 2.7, -6.5, 6.5);
                if (dz > -7) { s.Phase = PursuerPhase.Attack; s.Timer = 3.5; }
                break;
            case PursuerPhase.Attack:
                speed = p.Speed + Math.Clamp(-dz * 1.5, -8, 8);
                s.Timer -= dt;
                s.Target = s.Timer < 2.4
                    ? p.Lateral + self.Side * 0.8
                    : Math.Clamp(p.Lateral + self.Side * 2.7, -6.5, 6.5);
                if (s.Timer <= 0 || dz > 30 || dz < -40 || self.ContactCooldown > 0) s.Phase = PursuerPhase.Retreat;
                break;
            default:
                // Always slower than the player, at every distance, so it really drops back (to -80 m) and never creeps up again.
                speed = Math.Max(0, Math.Min(Math.Max(10, p.Speed - 12), p.Speed - 2));
                if (dz > -RetreatClearance)
                {
                    // Too close to ram-proof: keep to its own side, so a slow or stopped player isn't run into while the pursuer "retreats".
                    // With no safe lane on its own side (road edge) it holds its line rather than crossing the player's path.
                    s.Target = SafeLane(self, p.Lateral) ?? Math.Clamp(self.Lateral, -6.5, 6.5);
                }
                else if (SafeLane(self, p.Lateral) is null)
                    s.Target = Track.LaneCentres.MaxBy(lane => Math.Abs(lane - p.Lateral));   // dropped back past 20 m: crossing is safe now
                if (dz < -80)
                {
                    s.Phase = PursuerPhase.Approach;
                    self.Side = view.Rng.Next(2) == 0 ? -1 : 1;
                }
                break;
        }

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, ReactionDelay, dt);
        return new DriveIntent(speed, lateral);
    }

    const double MinLaneGapFromPlayer = 2.5;

    /// <summary>
    /// The lane on the pursuer's own side of the player that is at least 2.5 m from the player and nearest to the pursuer;
    /// if none qualifies (null) the caller decides. Never a lane that crosses the player's path.
    /// </summary>
    static double? SafeLane(AgentCar self, double playerLateral)
    {
        double offset = self.Lateral - playerLateral;
        int side = offset < 0 ? -1 : offset > 0 ? 1 : self.Side;
        double? best = null;
        foreach (double lane in Track.LaneCentres)
        {
            double fromPlayer = lane - playerLateral;
            if (Math.Sign(fromPlayer) != side || Math.Abs(fromPlayer) < MinLaneGapFromPlayer) continue;
            if (best is null || Math.Abs(lane - self.Lateral) < Math.Abs(best.Value - self.Lateral)) best = lane;
        }
        return best;
    }
}
