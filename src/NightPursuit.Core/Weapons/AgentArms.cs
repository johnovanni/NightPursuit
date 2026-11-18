namespace NightPursuit.Core.Weapons;

/// <summary>What an enemy car carries. A car without a weapon leaves that slot null.</summary>
public sealed class AgentArms
{
    public MachineGuns? RearGuns { get; init; }
    public OilDropper? Oil { get; init; }
    public CaltropDropper? Caltrops { get; init; }
    /// <summary>A boss's missile launcher (see <see cref="EnemyMissile"/>); only a boss carries one.</summary>
    public EnemyMissileRack? Missiles { get; init; }
    /// <summary>Which hazard a dropper-type driver lets go of next (they alternate).</summary>
    public bool NextDropIsOil { get; set; } = true;

    /// <summary>Set by the world when a requested drop was not made; the dropper's driver clears it and retries.</summary>
    public bool LastDropSkipped { get; set; }

    /// <summary>Advances the droppers' and the missile rack's timers; the guns time themselves in <see cref="MachineGuns.TryFire"/>.</summary>
    public void Update(double dt)
    {
        Oil?.Update(dt);
        Caltrops?.Update(dt);
        Missiles?.Update(dt);
    }
}
