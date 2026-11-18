using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>Slides into the player's lane when it is within 140 m ahead, and brake-checks inside 30 m.</summary>
public sealed class BlockerDriver : IDriver
{
    public sealed class State
    {
        public double Target { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
            self.DriverState = s = new State { Target = self.Lateral };

        var p = view.Player;
        double dz = self.Distance - p.Distance;
        double speed = self.CruiseSpeed;
        if (dz > 0 && dz < 140)
        {
            s.Target = Math.Clamp(p.Lateral, -5.5, 5.5);
            if (dz < 30) speed = Math.Max(15, Math.Min(self.CruiseSpeed, p.Speed - 5));
        }

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        return new DriveIntent(speed, lateral);
    }
}
