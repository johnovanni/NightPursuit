namespace NightPursuit.Core.Weapons;

/// <summary>Twin machine guns that alternate shots, heat up while firing, and lock out when they overheat.</summary>
public sealed class MachineGuns : Weapon
{
    public const double Interval = 0.075;
    public const double MuzzleSpeed = 260;
    public const double Range = 380;
    public const double HeatPerSecond = 0.25;
    public const double CoolPerSecond = 0.5;
    public const double LockSeconds = 2;

    double _cooldown;
    double _lockTimer;

    public override string Name => "Machine guns";

    /// <summary>0 (cool) to 1 (overheated).</summary>
    public double Heat { get; private set; }
    public bool Locked { get; private set; }
    /// <summary>True when the latest shot came from the left gun (the guns alternate).</summary>
    public bool LeftGun { get; private set; }

    /// <summary>
    /// Advances cooldown, heat and lock for one step and returns true when a shot is due (the guns then alternate).
    /// Deviation from the brief's <c>TryFire(dt)</c>: the trigger state is passed in, because heat depends on it.
    /// All gun timing lives here; <see cref="Update"/> has nothing left to do.
    /// </summary>
    public bool TryFire(bool triggerHeld, double dt)
    {
        _cooldown -= dt;
        if (Locked)
        {
            _lockTimer -= dt;
            if (_lockTimer <= 0) { Locked = false; Heat = 0; }
            return false;
        }

        Heat = triggerHeld ? Heat + HeatPerSecond * dt : Math.Max(0, Heat - CoolPerSecond * dt);
        if (Heat >= 1)
        {
            Heat = 1;
            Locked = true;
            _lockTimer = LockSeconds;
            return false;
        }

        if (!triggerHeld || _cooldown > 1e-9) return false;   // epsilon: 9 steps of 1/120 s must count as 0.075 s
        _cooldown = Interval;
        LeftGun = !LeftGun;
        return true;
    }

    public override void Update(double dt) { }
}
