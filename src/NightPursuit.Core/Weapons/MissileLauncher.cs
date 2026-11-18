namespace NightPursuit.Core.Weapons;

/// <summary>Fires a missile ahead; up to 2 charges, no recharge.</summary>
public sealed class MissileLauncher(int startingCharges = 1)
    : ChargedWeapon(MaxCharges, startingCharges, UseCooldown)
{
    public const int MaxCharges = 2;
    public const double UseCooldown = 1.0;

    public override string Name => "Missile";
}
