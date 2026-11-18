using NightPursuit.Core.Common;

namespace NightPursuit.Core.Control;

/// <summary>Up/Down move a target speed; the car speeds up or brakes to match it.</summary>
public sealed class SpeedLeverScheme : ThrottleSchemeBase
{
    double _target;

    public override string Name => "SPEED LEVER";
    public override string[] HelpLines => new[]
    {
        "Up/W raise target speed, Down/S lower it",
        "1-9, 0: jump to 10%-100% of top speed",
        "The car accelerates or brakes to match",
    };
    public override double? TargetSpeed => _target;
    public override string? Status => $"TARGET {Units.MsToMph(_target):0} MPH";

    public override void Reset(double currentSpeed) { base.Reset(currentSpeed); _target = currentSpeed; }

    public override void Handle(GameAction a, double currentSpeed, FeelSettings f)
    {
        _target = a.Kind switch
        {
            ActionKind.LeverPreset => a.Value / 10.0 * f.MaxSpeed,
            ActionKind.LeverUp => _target + Units.MphToMs(5),
            ActionKind.LeverDown => _target - Units.MphToMs(5),
            _ => _target,
        };
        _target = Math.Clamp(_target, 0, f.MaxSpeed);
    }

    public override PedalOutput Update(ControlFrame input, double currentSpeed, FeelSettings f, double dt)
    {
        double rate = Units.MphToMs(f.LeverRateMph);
        if (input.Gas) _target += rate * dt;
        if (input.Brake) _target -= rate * 1.6 * dt;
        _target = Math.Clamp(_target, 0, f.MaxSpeed);
        return HoldSpeed(currentSpeed, f, _target, dt);
    }
}
