namespace NightPursuit.Core.Weapons;

/// <summary>Drops oil behind the car: up to 5 charges, one drop per 0.6 s, one charge back every 8 s.</summary>
public sealed class OilDropper : Weapon
{
    public const int MaxCharges = 5;
    public const double DropCooldown = 0.6;
    public const double RechargeSeconds = 8;

    double _cooldown;
    double _recharge;

    public OilDropper(int startingCharges = MaxCharges) => Charges = Math.Clamp(startingCharges, 0, MaxCharges);

    public override string Name => "Oil slick";

    public int Charges { get; private set; }

    /// <summary>Uses a charge and starts the cooldown; false when cooling down or empty.</summary>
    public bool TryDrop()
    {
        if (_cooldown > 0 || Charges <= 0) return false;
        Charges--;
        _cooldown = DropCooldown;
        return true;
    }

    public override void Update(double dt)
    {
        if (_cooldown > 0) _cooldown -= dt;
        if (Charges >= MaxCharges) { _recharge = 0; return; }
        _recharge += dt;
        if (_recharge >= RechargeSeconds - 1e-9)
        {
            _recharge = 0;
            Charges++;
        }
    }
}
