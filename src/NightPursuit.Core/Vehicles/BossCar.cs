using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Vehicles;

/// <summary>
/// A mission's boss: one car that goes through the phases of its <see cref="BossDefinition"/>, driving as a different
/// kind of enemy in each. Its <see cref="Vehicle.Armor"/> is its health. It never leaves the road and is never removed
/// for distance, only by being destroyed.
/// </summary>
public sealed class BossCar(BossDefinition def) : AgentCar(AgentKind.Boss, VehicleStats.Boss with { Armor = def.Health })
{
    const double Cruise = 52;

    public BossDefinition Definition { get; } = def;
    /// <summary>Which phase it is in, 0-based.</summary>
    public int PhaseIndex { get; set; }
    public BossPhaseDefinition Phase => Definition.Phases[PhaseIndex];
    public double MaxHealth { get; } = def.Health;
    public double HealthFraction => Armor / MaxHealth;

    /// <summary>Seconds left in which damage is ignored (just after a phase change); 0 when it can be hurt.</summary>
    public double Shield { get; set; }
    /// <summary>Seconds left of the caltrops slow; 0 when not slowed.</summary>
    public double SlowLeft { get; set; }
    /// <summary>Spun out by oil: gunfire does double damage.</summary>
    public bool ArmorOpen => State == VehicleState.Spinning;
    /// <summary>The wreck it last ran into, which cannot hurt it again.</summary>
    public Wreck? LastWreck { get; set; }
    /// <summary>The caltrops that last slowed it; crossing them again cannot slow it again (preflight F7).</summary>
    public Caltrops? LastCaltrops { get; set; }
    /// <summary>False while it eases in after appearing; true once you come within 140 m or hit it, and its phase behaviour starts.</summary>
    public bool Engaged { get; set; }

    /// <summary>Wreck avoidance while it eases in (the phase drivers keep their own in <see cref="AgentCar.DriverState"/>).</summary>
    public AvoidMemory ArrivalAvoid { get; } = new();
    /// <summary>
    /// The lane it is passing you in while it is behind in a phase that never overtakes (Blocker, Armored), until it is
    /// 60 m ahead; null when not passing.
    /// </summary>
    public double? PassLane { get; set; }
    /// <summary>Wreck avoidance while passing.</summary>
    public AvoidMemory PassAvoid { get; } = new();

    public static BossCar Create(int id, BossDefinition def, double distance, double lateral, double speed)
    {
        var b = new BossCar(def)
        {
            Id = id, Distance = distance, Lateral = lateral, Speed = speed, CruiseSpeed = Cruise,
            Arms = new AgentArms { RearGuns = new MachineGuns(), Oil = new OilDropper(5), Caltrops = new CaltropDropper(3) },
            Driver = BossDriver.Instance, Engaged = false, State = VehicleState.Driving, Side = 1,
        };
        b.SnapPrev();   // drawn where it appears, not sweeping in
        return b;
    }
}
