namespace NightPursuit.Core.Weapons;

/// <summary>What the player carries. Task 9 adds the oil dropper.</summary>
public sealed class Loadout
{
    public MachineGuns Guns { get; } = new();
}
