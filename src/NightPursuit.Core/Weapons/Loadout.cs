namespace NightPursuit.Core.Weapons;

/// <summary>What the player carries.</summary>
public sealed class Loadout
{
    public Loadout(LoadoutDefinition? start = null)
    {
        start ??= LoadoutDefinition.Default;
        Oil = new OilDropper(start.Oil);
        Smoke = new SmokeLauncher(start.Smoke);
        Caltrops = new CaltropDropper(start.Caltrops);
        Missiles = new MissileLauncher(start.Missiles);
        Strike = new SatelliteStrike(start.Strikes);
    }

    public Loadout(int startingOil) : this(LoadoutDefinition.Default with { Oil = startingOil }) { }

    public MachineGuns Guns { get; } = new();
    public OilDropper Oil { get; }
    public SmokeLauncher Smoke { get; }
    public CaltropDropper Caltrops { get; }
    public MissileLauncher Missiles { get; }
    public SatelliteStrike Strike { get; }

    public RearWeapon SelectedRear { get; private set; } = RearWeapon.Oil;

    public ChargedWeapon Rear(RearWeapon w) => w switch
    {
        RearWeapon.Oil => Oil,
        RearWeapon.Smoke => Smoke,
        _ => Caltrops,
    };

    public ChargedWeapon SelectedRearWeapon => Rear(SelectedRear);

    /// <summary>Oil, then smoke, then caltrops, then oil again.</summary>
    public void CycleRear() => SelectedRear = (RearWeapon)(((int)SelectedRear + 1) % 3);

    /// <summary>Advances every weapon except the guns (the world updates those with the trigger).</summary>
    public void Update(double dt)
    {
        Oil.Update(dt);
        Smoke.Update(dt);
        Caltrops.Update(dt);
        Missiles.Update(dt);
        Strike.Update(dt);
    }
}
