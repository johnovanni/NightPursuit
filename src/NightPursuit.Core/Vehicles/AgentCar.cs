using NightPursuit.Core.Drivers;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Vehicles;

public enum AgentKind { Runner, Blocker, Pursuer, Gunner, Dropper, Armored, Boss, Civilian, PolicePatrol, PoliceCruiser, Interceptor, ArmoredSuv }

/// <summary>Which side a kind of car is on, and what it is called on screen.</summary>
public static class KindRules
{
    public static Faction FactionOf(AgentKind k) => k switch
    {
        AgentKind.Civilian => Faction.Civilian,
        AgentKind.PolicePatrol or AgentKind.PoliceCruiser or AgentKind.Interceptor or AgentKind.ArmoredSuv => Faction.Police,
        _ => Faction.Hostile,
    };

    public static bool IsPolice(AgentKind k) => FactionOf(k) == Faction.Police;

    public static string DisplayName(AgentKind k) => k switch
    {
        AgentKind.Civilian => "CIVILIAN",
        AgentKind.PolicePatrol or AgentKind.PoliceCruiser => "POLICE",
        AgentKind.Interceptor => "INTERCEPTOR",
        AgentKind.ArmoredSuv => "POLICE SUV",
        _ => k.ToString().ToUpperInvariant(),
    };
}

/// <summary>An enemy car. Its driver (AI) is attached separately through <see cref="DriverState"/>.</summary>
public class AgentCar(AgentKind kind, VehicleStats stats) : Vehicle(stats)
{
    public override Faction Faction => KindRules.FactionOf(Kind);
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

    /// <summary>The game time until which a hit on this car counts as the player's doing (heat, Task 3).</summary>
    public double PlayerBlameUntil { get; set; }
    /// <summary>Seconds until this car's next hit on it can add heat again (Task 3).</summary>
    public double HitHeatCooldown { get; set; }

    /// <summary>The "use a missile, a ram or a hazard" hint has been shown for this armored car.</summary>
    public bool HintShown { get; set; }

    /// <summary>What this car carries: guns and droppers.</summary>
    public AgentArms Arms { get; init; } = new();

    static readonly IDriver RunnerDriver = new Drivers.RunnerDriver();
    static readonly IDriver BlockerDriver = new Drivers.BlockerDriver();
    static readonly IDriver PursuerDriver = new Drivers.PursuerDriver();
    static readonly IDriver GunnerDriver = new Drivers.GunnerDriver();
    static readonly IDriver DropperDriver = new Drivers.DropperDriver();
    static readonly IDriver ArmoredDriver = new Drivers.ArmoredDriver();

    /// <summary>The one driver every car of this behaviour shares. A boss has its own (<see cref="BossDriver"/>), which uses these for its phases.</summary>
    public static IDriver DriverFor(AgentKind kind) => kind switch
    {
        AgentKind.Runner => RunnerDriver,
        AgentKind.Blocker => BlockerDriver,
        AgentKind.Pursuer => PursuerDriver,
        AgentKind.Gunner => GunnerDriver,
        AgentKind.Dropper => DropperDriver,
        AgentKind.Armored => ArmoredDriver,
        AgentKind.Civilian => Drivers.CivilianDriver.Instance,
        AgentKind.PolicePatrol or AgentKind.PoliceCruiser or AgentKind.Interceptor or AgentKind.ArmoredSuv => Drivers.PatrolDriver.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a boss is made with BossCar.Create"),
    };

    public static AgentCar Create(int id, AgentKind kind, double distance, double lateral, double speed)
    {
        var stats = kind switch
        {
            AgentKind.Runner => VehicleStats.Runner,
            AgentKind.Blocker => VehicleStats.Blocker,
            AgentKind.Gunner => VehicleStats.Gunner,
            AgentKind.Dropper => VehicleStats.Dropper,
            AgentKind.Armored => VehicleStats.Armored,
            AgentKind.Civilian => VehicleStats.Civilian,
            AgentKind.PolicePatrol => VehicleStats.PolicePatrol,
            AgentKind.PoliceCruiser => VehicleStats.PoliceCruiser,
            AgentKind.Interceptor => VehicleStats.Interceptor,
            AgentKind.ArmoredSuv => VehicleStats.ArmoredSuv,
            _ => VehicleStats.Pursuer,
        };
        var car = new AgentCar(kind, stats)
        {
            Id = id, Distance = distance, Lateral = lateral, Speed = speed, CruiseSpeed = speed,
            Driver = DriverFor(kind),
            Arms = kind switch
            {
                AgentKind.Gunner => new AgentArms { RearGuns = new MachineGuns() },
                AgentKind.Dropper => new AgentArms { Oil = new OilDropper(5), Caltrops = new CaltropDropper(3) },
                _ => new AgentArms(),
            },
            Armor = stats.Armor, State = VehicleState.Driving, Side = 1,
        };
        car.SnapPrev();   // a new car must not be drawn sweeping in from where its Prev fields start
        return car;
    }
}
