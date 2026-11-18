using NightPursuit.Core.Common;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>A read-only window onto the live world for drivers.</summary>
public sealed class WorldView(World world)
{
    public PlayerCar Player => world.Player;
    public IReadOnlyList<AgentCar> Agents => world.Agents;
    public IReadOnlyList<Hazard> Hazards => world.Hazards;
    public IRng Rng => world.Rng;

    /// <summary>False while the car is blinded by smoke.</summary>
    public bool CanSeePlayer(AgentCar self) => self.Blinded <= 0;
}
