using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>Stands in for a car's own driver while smoke hides the player: slows down and wanders about the road, never firing or dropping anything.</summary>
public sealed class BlindDriver : IDriver
{
    const double WanderInterval = 0.8, WanderReach = 4, RoadLimit = 6, SnapBelow = 1e-9;

    public static readonly BlindDriver Instance = new();

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        self.WanderTimer = Math.Max(0, self.WanderTimer - dt);
        if (self.WanderTimer < SnapBelow)
        {
            self.WanderTimer = WanderInterval;
            self.WanderLateral = Math.Clamp(self.Lateral + (2 * view.Rng.NextDouble() - 1) * WanderReach, -RoadLimit, RoadLimit);
        }
        return new DriveIntent(Math.Max(15, 0.6 * self.CruiseSpeed), self.WanderLateral);
    }
}
