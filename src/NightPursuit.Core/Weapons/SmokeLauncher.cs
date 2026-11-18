namespace NightPursuit.Core.Weapons;

/// <summary>Lays a smoke screen behind the car; up to 3 charges, no recharge.</summary>
public sealed class SmokeLauncher(int startingCharges = 2)
    : ChargedWeapon(MaxCharges, startingCharges, UseCooldown)
{
    public const int MaxCharges = 3;
    public const double UseCooldown = 1.0;

    public override string Name => "Smoke screen";
}
