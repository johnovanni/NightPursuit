using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>The AI of one kind of enemy car. A driver keeps no per-car data itself (it uses <see cref="AgentCar.DriverState"/>), so one instance can serve many cars.</summary>
public interface IDriver
{
    DriveIntent Decide(AgentCar self, WorldView view, double dt);
}
