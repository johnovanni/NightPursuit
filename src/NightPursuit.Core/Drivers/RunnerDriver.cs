using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>Cruises at its own speed and hops to a random lane every 2.5 to 6.5 s, or at once when shot.</summary>
public sealed class RunnerDriver : IDriver
{
    const double ReactionDelay = 0.5;

    public sealed class State
    {
        public double Target { get; set; }
        public double LaneTimer { get; set; }
        public bool WasHit { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
        {
            s = new State { Target = self.Lateral, LaneTimer = NextInterval(view) };
            self.DriverState = s;
        }

        s.LaneTimer -= dt;
        bool hit = self.HitFlash > 0;
        if (s.LaneTimer <= 0 || (hit && !s.WasHit))
        {
            s.LaneTimer = NextInterval(view);
            s.Target = Track.LaneCentres[view.Rng.Next(Track.LaneCentres.Length)];
        }
        s.WasHit = hit;

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, ReactionDelay, dt);
        return new DriveIntent(self.CruiseSpeed, lateral);
    }

    static double NextInterval(WorldView view) => 2.5 + 4 * view.Rng.NextDouble();
}
