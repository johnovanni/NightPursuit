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

    /// <summary>While easing in (not <see cref="Engaged"/>) it drives this much slower than you, m/s (call 21).</summary>
    public const double ArrivalSlower = 10;

    public BossDefinition Definition { get; } = def;
    /// <summary>How it is drawn, from its definition.</summary>
    public BossLook Look => Definition.Look;
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
    /// <summary>The caltrops that last slowed it.</summary>
    public Caltrops? LastCaltrops { get; set; }
    /// <summary>
    /// Every wreck, caltrops set and oil slick that has already acted on it. Each acts once, ever, even when several
    /// overlap (preflight F7); expired ones, and those a respawn clears away, are dropped.
    /// </summary>
    public HashSet<Hazard> HazardsMet { get; } = new();
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

    /// <summary>
    /// Puts it back on the road at <paramref name="distance"/> in the centre lane, driving straight at
    /// <paramref name="speed"/> and easing in again (not engaged; its drivers start fresh). Used when the player
    /// respawns: its health, phase and phase shield are kept (MSN-3), and any spin or caltrops slow ends.
    /// </summary>
    public void ReturnTo(double distance, double speed)
    {
        Distance = distance;
        Lateral = 0;
        Heading = 0;
        Speed = speed;
        PushVel = 0;
        LateralVel = 0;
        if (State == VehicleState.Spinning) State = VehicleState.Driving;
        StateTimer = 0;
        SpinAngle = 0;
        SpinRate = 0;
        SlowLeft = 0;          // a slow left over would engage it again at once
        Engaged = false;
        DriverState = null;
        PassLane = null;
        ArrivalAvoid.Active = false;
        PassAvoid.Active = false;
        AheadMemory.Active = false;
        SnapPrev();            // a teleport: not drawn sweeping in
    }

    public static BossCar Create(int id, BossDefinition def, double distance, double lateral, double speed)
    {
        var b = new BossCar(def)
        {
            Id = id, Distance = distance, Lateral = lateral, Speed = speed, CruiseSpeed = Cruise,
            Arms = new AgentArms
            {
                RearGuns = new MachineGuns(), Oil = new OilDropper(5), Caltrops = new CaltropDropper(3),
                Missiles = new EnemyMissileRack(),
            },
            Driver = BossDriver.Instance, Engaged = false, State = VehicleState.Driving, Side = 1,
        };
        b.SnapPrev();   // drawn where it appears, not sweeping in
        return b;
    }
}
