using NightPursuit.Core.Common;

namespace NightPursuit.Core.Control;

/// <summary>One way of turning keys into throttle and brake.</summary>
public interface IThrottleScheme
{
    string Name { get; }
    string[] HelpLines { get; }
    /// <summary>Speed the scheme is aiming for, m/s (drawn as a marker on the speed bar), or null.</summary>
    double? TargetSpeed { get; }
    string? Status { get; }
    void Reset(double currentSpeed);
    void Handle(GameAction a, double currentSpeed, FeelSettings f);
    PedalOutput Update(ControlFrame input, double currentSpeed, FeelSettings f, double dt);
}

public abstract class ThrottleSchemeBase : IThrottleScheme
{
    protected double Pedal, BrakePedal;

    public abstract string Name { get; }
    public abstract string[] HelpLines { get; }
    public virtual double? TargetSpeed => null;
    public virtual string? Status => null;

    public virtual void Reset(double currentSpeed) { Pedal = 0; BrakePedal = 0; }
    public virtual void Handle(GameAction a, double currentSpeed, FeelSettings f) { }
    public abstract PedalOutput Update(ControlFrame input, double currentSpeed, FeelSettings f, double dt);

    protected PedalOutput Output() => new(Pedal, BrakePedal);

    protected static double Ramp(double value, bool held, double up, double down, double dt) =>
        held ? Math.Min(1, value + up * dt) : Math.Max(0, value - down * dt);

    /// <summary>Works the pedals to hold a target speed: steady-state throttle plus a correction.</summary>
    protected PedalOutput HoldSpeed(double speed, FeelSettings f, double target, double dt)
    {
        double r = speed / f.MaxSpeed;
        double steady = (0.6 + 1.6 * r) / (f.MaxAccel * Math.Max(0.05, 1 - r * r) + 1.6 * r);
        double err = target - speed;
        double want = Math.Clamp(steady + err * 0.35, 0, 1);
        Pedal = MathX.MoveToward(Pedal, want, 3.0 * dt);
        BrakePedal = err < -1.5 ? Math.Clamp((-err - 1.5) * 0.12, 0, 1) : 0;
        if (BrakePedal > 0) Pedal = 0;
        return Output();
    }
}
