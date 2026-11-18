using NightPursuit.Core.Common;

namespace NightPursuit.Core.Control;

/// <summary>Hold the key and the throttle builds; let go and it falls away. Cruise holds the current speed.</summary>
public sealed class RampPedalScheme : ThrottleSchemeBase
{
    bool _cruise;
    double _cruiseSpeed;

    public override string Name => "RAMP PEDAL";
    public override string[] HelpLines => new[]
    {
        "Hold Up/W: throttle builds gradually",
        "Let go: throttle falls away and you coast",
        "Down/S: brake    C: cruise control",
    };
    public override double? TargetSpeed => _cruise ? _cruiseSpeed : null;
    public override string? Status => _cruise ? $"CRUISE {Units.MsToMph(_cruiseSpeed):0} MPH" : null;

    public override void Reset(double currentSpeed) { base.Reset(currentSpeed); _cruise = false; }

    public override void Handle(GameAction a, double currentSpeed, FeelSettings f)
    {
        if (a.Kind != ActionKind.Cruise) return;
        _cruise = !_cruise;
        _cruiseSpeed = Math.Max(currentSpeed, 5);
    }

    public override PedalOutput Update(ControlFrame input, double currentSpeed, FeelSettings f, double dt)
    {
        if (_cruise && input.Brake) _cruise = false;
        if (_cruise && !input.Gas) return HoldSpeed(currentSpeed, f, _cruiseSpeed, dt);
        Pedal = Ramp(Pedal, input.Gas, f.ThrottleRampUp, f.ThrottleRampDown, dt);
        BrakePedal = Ramp(BrakePedal, input.Brake, 6, 12, dt);
        return Output();
    }
}
