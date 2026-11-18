using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// Cruises at its own speed and hops to a random lane every <c>minInterval</c> to <c>minInterval + extraInterval</c>
/// seconds (2.5 to 6.5 s by default), or at once when shot.
/// </summary>
/// <param name="minInterval">The shortest time between lane hops, seconds.</param>
/// <param name="extraInterval">Up to this many seconds more, picked at random for each hop.</param>
/// <param name="alwaysChangeLane">True: each hop goes to a different lane; false: the current lane may be picked again.</param>
public sealed class RunnerDriver(double minInterval = 2.5, double extraInterval = 4, bool alwaysChangeLane = false) : IDriver
{
    /// <summary>A boss in a Runner phase: hops every 1.0 to 1.8 s, always to another lane (call 28).</summary>
    public static readonly RunnerDriver Weaving = new(1.0, 0.8, alwaysChangeLane: true);

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
            s.Target = alwaysChangeLane ? OtherLane(s.Target, view) : Track.LaneCentres[view.Rng.Next(Track.LaneCentres.Length)];
        }
        s.WasHit = hit;

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        return new DriveIntent(self.CruiseSpeed, lateral);
    }

    double NextInterval(WorldView view) => minInterval + extraInterval * view.Rng.NextDouble();

    /// <summary>A random lane centre other than the one nearest <paramref name="current"/>.</summary>
    static double OtherLane(double current, WorldView view)
    {
        double home = DriverHelpers.NearestLane(current);
        int pick = view.Rng.Next(Track.LaneCentres.Length - 1);
        foreach (double lane in Track.LaneCentres)
        {
            if (lane == home) continue;
            if (pick-- == 0) return lane;
        }
        return home;   // unreachable with more than one lane
    }
}
