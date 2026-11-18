using NightPursuit.Core.Drivers;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Vehicles;

public enum AgentKind { Runner, Blocker, Pursuer, Gunner }

/// <summary>An enemy car. Its driver (AI) is attached separately through <see cref="DriverState"/>.</summary>
public class AgentCar(AgentKind kind, VehicleStats stats) : Vehicle(stats)
{
    public override Faction Faction => Faction.Hostile;
    public AgentKind Kind { get; } = kind;
    /// <summary>-1 or +1: which way this car swerves or attacks from.</summary>
    public int Side { get; set; } = 1;
    /// <summary>The AI that steers this car; one driver instance may serve many cars.</summary>
    public IDriver Driver { get; set; } = null!;
    /// <summary>Opaque per-car memory owned by the driver.</summary>
    public object? DriverState { get; set; }
    /// <summary>The speed this car holds when nothing makes it do otherwise, m/s.</summary>
    public double CruiseSpeed { get; set; }
    /// <summary>Seconds this car stays unable to see the player after leaving smoke; 0 when it can see.</summary>
    public double Blinded { get; set; }
    /// <summary>Where a blinded car is currently steering, metres from the road centre.</summary>
    public double WanderLateral { get; set; }
    /// <summary>Seconds until a blinded car picks a new wander target.</summary>
    public double WanderTimer { get; set; }
    /// <summary>What this car is doing about the car ahead of it (see <see cref="DriverHelpers.KeepClearOfCarAhead"/>).</summary>
    public CarAheadMemory AheadMemory { get; } = new();
    /// <summary>A caltrop has burst a tyre: the car slows, wobbles and limps off the road.</summary>
    public bool Punctured { get; set; }
    /// <summary>Seconds since the puncture (counts up while the car is on the road).</summary>
    public double PunctureSeconds { get; set; }

    /// <summary>What this car carries: guns and droppers.</summary>
    public AgentArms Arms { get; init; } = new();

    static readonly IDriver RunnerDriver = new Drivers.RunnerDriver();
    static readonly IDriver BlockerDriver = new Drivers.BlockerDriver();
    static readonly IDriver PursuerDriver = new Drivers.PursuerDriver();
    static readonly IDriver GunnerDriver = new Drivers.GunnerDriver();

    public static AgentCar Create(int id, AgentKind kind, double distance, double lateral, double speed)
    {
        var stats = kind switch
        {
            AgentKind.Runner => VehicleStats.Runner,
            AgentKind.Blocker => VehicleStats.Blocker,
            AgentKind.Gunner => VehicleStats.Gunner,
            _ => VehicleStats.Pursuer,
        };
        var car = new AgentCar(kind, stats)
        {
            Id = id, Distance = distance, Lateral = lateral, Speed = speed, CruiseSpeed = speed,
            Driver = kind switch { AgentKind.Runner => RunnerDriver, AgentKind.Blocker => BlockerDriver,
                AgentKind.Gunner => GunnerDriver, _ => PursuerDriver },
            Arms = kind == AgentKind.Gunner ? new AgentArms { RearGuns = new MachineGuns() } : new AgentArms(),
            Armor = stats.Armor, State = VehicleState.Driving, Side = 1,
        };
        car.SnapPrev();   // a new car must not be drawn sweeping in from where its Prev fields start
        return car;
    }
}
