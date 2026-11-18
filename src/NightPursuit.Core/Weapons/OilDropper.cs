namespace NightPursuit.Core.Weapons;

/// <summary>Drops oil behind the car: up to 5 charges, one drop per 0.6 s, one charge back every 8 s.</summary>
public sealed class OilDropper(int startingCharges = OilDropper.MaxCharges)
    : ChargedWeapon(MaxCharges, startingCharges, DropCooldown, RechargeSeconds)
{
    public const int MaxCharges = 5;
    public const double DropCooldown = 0.6;
    public const double RechargeSeconds = 8;

    public override string Name => "Oil slick";

    /// <summary>Uses a charge and starts the cooldown; false when cooling down or empty.</summary>
    public bool TryDrop() => TryUse();
}
