using NightPursuit.Core.Police;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// A police car at Patrol level: the same plain lane driving as a civilian, including keeping clear of the player.
/// It never fires or drops anything. Its lane goes through <see cref="PoliceLanes.MayEnter"/> (the three-lane rule):
/// a refused line keeps its current lane, or the nearest allowed one.
/// </summary>
public sealed class PatrolDriver : IDriver
{
    public static readonly PatrolDriver Instance = new();

    public sealed class State
    {
        public double Lane { get; set; }
        public AvoidMemory Avoid { get; } = new();
        /// <summary>What this car is doing about the player ahead of it (its own memory, apart from <see cref="AgentCar.AheadMemory"/>).</summary>
        public CarAheadMemory PlayerAhead { get; } = new();
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
        {
            s = new State { Lane = DriverHelpers.NearestLane(self.Lateral) };
            self.DriverState = s;
        }
        var intent = DriverHelpers.DriveInLane(self, view, s.Lane, s.Avoid, s.PlayerAhead, dt);
        return PoliceLanes.MayEnter(view, self, intent.TargetLateral)
            ? intent
            : intent with { TargetLateral = PoliceLanes.AllowedLateral(view, self) };
    }
}
