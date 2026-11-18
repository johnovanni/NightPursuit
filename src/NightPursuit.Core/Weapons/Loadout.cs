namespace NightPursuit.Core.Weapons;

/// <summary>What the player carries.</summary>
public sealed class Loadout(int startingOil = OilDropper.MaxCharges)
{
    public MachineGuns Guns { get; } = new();
    public OilDropper Oil { get; } = new(startingOil);
}
