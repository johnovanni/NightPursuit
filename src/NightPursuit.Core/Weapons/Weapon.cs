namespace NightPursuit.Core.Weapons;

/// <summary>Something the player carries and uses.</summary>
public abstract class Weapon
{
    public abstract string Name { get; }

    /// <summary>Advances any timers that run regardless of the trigger.</summary>
    public abstract void Update(double dt);
}
