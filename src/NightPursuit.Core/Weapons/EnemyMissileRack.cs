namespace NightPursuit.Core.Weapons;

/// <summary>A boss's missile launcher: no ammunition limit, but at most one launch every 6 s.</summary>
public sealed class EnemyMissileRack
{
    public const double Interval = 6;
    const double SnapBelow = 1e-9;

    /// <summary>Seconds until it can launch again; 0 when ready.</summary>
    public double CooldownLeft { get; private set; }
    public bool Ready => CooldownLeft <= 0;

    /// <summary>Starts the 6 s cooldown and returns true, or returns false while cooling down.</summary>
    public bool TryLaunch()
    {
        if (!Ready) return false;
        CooldownLeft = Interval;
        return true;
    }

    public void Update(double dt)
    {
        if (CooldownLeft <= 0) return;
        CooldownLeft -= dt;
        if (CooldownLeft < SnapBelow) CooldownLeft = 0;   // lands on 0, not a rounding error above it (preflight F6)
    }
}
