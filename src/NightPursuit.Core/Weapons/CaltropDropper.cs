namespace NightPursuit.Core.Weapons;

/// <summary>Scatters caltrops behind the car; up to 3 charges, no recharge.</summary>
public sealed class CaltropDropper(int startingCharges = 2)
    : ChargedWeapon(MaxCharges, startingCharges, UseCooldown)
{
    public const int MaxCharges = 3;
    public const double UseCooldown = 0.8;

    public override string Name => "Caltrops";
}
