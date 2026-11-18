namespace NightPursuit.Core.Weapons;

/// <summary>A weapon with a limited number of charges, an optional use cooldown and an optional slow recharge.</summary>
public abstract class ChargedWeapon : Weapon
{
    const double Epsilon = 1e-9;

    double _recharge;

    /// <param name="rechargeTime">Seconds per charge regained; 0 means the weapon never recharges.</param>
    protected ChargedWeapon(int capacity, int startingCharges, double cooldown, double rechargeTime = 0)
    {
        Capacity = capacity;
        Charges = Math.Clamp(startingCharges, 0, capacity);
        Cooldown = cooldown;
        RechargeTime = rechargeTime;
    }

    public int Capacity { get; }
    public int Charges { get; private set; }
    public double Cooldown { get; }
    public double CooldownLeft { get; private set; }
    public double RechargeTime { get; }

    /// <summary>How far the next charge is, 0..1; 0 when full or when the weapon never recharges.</summary>
    public double RechargeProgress => RechargeTime > 0 && Charges < Capacity ? Math.Clamp(_recharge / RechargeTime, 0, 1) : 0;

    public bool Ready => CooldownLeft <= 0 && Charges > 0;

    /// <summary>Uses a charge and starts the cooldown; false when cooling down or empty.</summary>
    public bool TryUse()
    {
        if (!Ready) return false;
        Charges--;
        CooldownLeft = Cooldown;
        return true;
    }

    /// <summary>Adds up to <paramref name="amount"/> charges; returns how many actually fitted.</summary>
    public int Restock(int amount)
    {
        int added = Math.Clamp(amount, 0, Capacity - Charges);
        Charges += added;
        return added;
    }

    public override void Update(double dt)
    {
        if (CooldownLeft > 0)
        {
            CooldownLeft -= dt;
            if (CooldownLeft < Epsilon) CooldownLeft = 0;
        }
        if (RechargeTime <= 0) return;
        if (Charges >= Capacity) { _recharge = 0; return; }
        _recharge += dt;
        if (_recharge >= RechargeTime - Epsilon)
        {
            _recharge = 0;
            Charges++;
        }
    }
}
