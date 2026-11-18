using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

public enum PursuerPhase { Approach, Attack, Retreat }

/// <summary>Closes in from behind, rams alongside for a few seconds, then drops back and tries again from a random side.</summary>
public sealed class PursuerDriver : IDriver
{
    const double ReactionDelay = 0.3;

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
                speed = Math.Max(10, p.Speed - 12);
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
}
