namespace NightPursuit.Core.Weapons;

/// <summary>A single-charge orbital strike: using it starts a short targeting sweep that ends in the strike.</summary>
public sealed class SatelliteStrike(int startingCharges = 0)
    : ChargedWeapon(MaxCharges, startingCharges, 0)
{
    public const int MaxCharges = 1;
    public const double SweepSeconds = 1.5;
    const double Epsilon = 1e-9;

    public override string Name => "Satellite strike";

    public bool Targeting { get; private set; }
    public double SweepLeft { get; private set; }

    /// <summary>Uses the charge and starts the sweep; false when empty or already sweeping.</summary>
    public bool TryBegin()
    {
        if (Targeting || !TryUse()) return false;
        Targeting = true;
        SweepLeft = SweepSeconds;
        return true;
    }

    /// <summary>Runs the sweep; true exactly once, on the step it ends. <see cref="ChargedWeapon.Update"/> leaves the sweep alone.</summary>
    public bool AdvanceSweep(double dt)
    {
        if (!Targeting) return false;
        SweepLeft -= dt;
        if (SweepLeft >= Epsilon) return false;
        SweepLeft = 0;
        Targeting = false;
        return true;
    }
}
