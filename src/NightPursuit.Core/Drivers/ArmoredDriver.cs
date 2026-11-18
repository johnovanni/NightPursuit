using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

public enum ArmoredPhase { Lumber, Chase, BackOff }

/// <summary>
/// A slow, heavy car. Ahead of the player it lumbers along in its lane; once the player has passed it, it chases and
/// rams from behind, backing off for 2 s after each hit. Bullets barely scratch it.
/// </summary>
public sealed class ArmoredDriver : IDriver
{
    const double SnapBelow = 1e-9;
    const double LumberAbove = 5;
    const double ChaseTopSpeed = 64, ChaseExtra = 8;
    const double BackOffSeconds = 2, BackOffSlower = 6;

    public sealed class State
    {
        public ArmoredPhase Phase { get; set; } = ArmoredPhase.Lumber;
        public double Timer { get; set; }
        public double Target { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
            self.DriverState = s = new State { Target = self.Lateral };

        var p = view.Player;
        double dD = self.Distance - p.Distance;

        if (s.Phase == ArmoredPhase.BackOff)
        {
            s.Timer -= dt;
            if (s.Timer < SnapBelow) { s.Timer = 0; s.Phase = ArmoredPhase.Chase; }
        }
        else if (self.ContactCooldown > 0)       // it has just touched the player
        {
            s.Phase = ArmoredPhase.BackOff;
            s.Timer = BackOffSeconds;
        }
        else
            s.Phase = dD > LumberAbove ? ArmoredPhase.Lumber : ArmoredPhase.Chase;

        double speed;
        switch (s.Phase)
        {
            case ArmoredPhase.Lumber:
                speed = self.CruiseSpeed;
                s.Target = self.Lateral;
                break;
            case ArmoredPhase.Chase:
                speed = Math.Min(ChaseTopSpeed, p.Speed + ChaseExtra);
                s.Target = p.Lateral;
                break;
            default:
                speed = p.Speed - BackOffSlower;
                break;
        }

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        return new DriveIntent(speed, lateral);
    }
}
