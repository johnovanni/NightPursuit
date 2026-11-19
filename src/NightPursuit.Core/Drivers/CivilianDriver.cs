using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// An ordinary road user: keeps its lane at its cruise speed, steers round wrecks, and keeps clear of the player when
/// the player is ahead of it. It never fires or drops anything. (Reactions to the player's weapons come later.)
/// </summary>
public sealed class CivilianDriver : IDriver
{
    public static readonly CivilianDriver Instance = new();

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
        return DriverHelpers.DriveInLane(self, view, s.Lane, s.Avoid, s.PlayerAhead, dt);
    }
}
