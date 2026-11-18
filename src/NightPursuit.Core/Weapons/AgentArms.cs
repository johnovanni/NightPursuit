namespace NightPursuit.Core.Weapons;

/// <summary>What an enemy car carries. A car without a weapon leaves that slot null.</summary>
public sealed class AgentArms
{
    public MachineGuns? RearGuns { get; init; }
    public OilDropper? Oil { get; init; }
    public CaltropDropper? Caltrops { get; init; }
    /// <summary>Which hazard a dropper-type driver lets go of next (they alternate).</summary>
    public bool NextDropIsOil { get; set; } = true;

    /// <summary>Advances the droppers' timers; the guns time themselves in <see cref="MachineGuns.TryFire"/>.</summary>
    public void Update(double dt)
    {
        Oil?.Update(dt);
        Caltrops?.Update(dt);
    }
}
