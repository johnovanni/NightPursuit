namespace ControlsLab;

/// <summary>One way of turning keys into throttle and brake. Switch between them with Tab or F2–F5.</summary>
public interface IThrottleScheme
{
    string Name { get; }
    string[] HelpLines { get; }
    /// <summary>Speed the scheme is aiming for, m/s (drawn as a marker on the speed bar).</summary>
    double? TargetSpeed { get; }
    /// <summary>Top speed of the current gear, m/s, or null when there are no gears.</summary>
    double? GearTopSpeed { get; }
    double AccelMultiplier { get; }
    string? Status { get; }
    void Reset(PlayerCar car);
    void Handle(GameAction a, PlayerCar car, Tuning t);
    void Update(InputFrame input, PlayerCar car, Tuning t, double dt);
}

public abstract class ThrottleSchemeBase : IThrottleScheme
{
    protected double Pedal, BrakePedal;

    public abstract string Name { get; }
    public abstract string[] HelpLines { get; }
    public virtual double? TargetSpeed => null;
    public virtual double? GearTopSpeed => null;
    public virtual double AccelMultiplier => 1.0;
    public virtual string? Status => null;

    public virtual void Reset(PlayerCar car) { Pedal = 0; BrakePedal = 0; }
    public virtual void Handle(GameAction a, PlayerCar car, Tuning t) { }
    public abstract void Update(InputFrame input, PlayerCar car, Tuning t, double dt);

    protected static double Ramp(double value, bool held, double up, double down, double dt) =>
        held ? Math.Min(1, value + up * dt) : Math.Max(0, value - down * dt);

    protected void Output(PlayerCar car) { car.Throttle = Pedal; car.Brake = BrakePedal; }

    /// <summary>Works the pedals to hold a target speed: steady-state throttle plus a correction.</summary>
    protected void HoldSpeed(PlayerCar car, Tuning t, double target, double dt)
    {
        double v = car.Speed, r = v / t.MaxSpeed;
        double steady = (0.6 + 1.6 * r) / (t.MaxAccel * Math.Max(0.05, 1 - r * r) + 1.6 * r);
        double err = target - v;
        double want = Math.Clamp(steady + err * 0.35, 0, 1);
        Pedal = MathX.MoveToward(Pedal, want, 3.0 * dt);
        BrakePedal = err < -1.5 ? Math.Clamp((-err - 1.5) * 0.12, 0, 1) : 0;
        if (BrakePedal > 0) Pedal = 0;
        Output(car);
    }
}

/// <summary>1: hold the key and the throttle builds; let go and it falls away. C = cruise control.</summary>
public sealed class RampPedalScheme : ThrottleSchemeBase
{
    bool _cruise;
    double _cruiseSpeed;

    public override string Name => "1 RAMP PEDAL";
    public override string[] HelpLines => new[]
    {
        "Hold Up/W: throttle builds gradually",
        "Let go: throttle falls away and you coast",
        "Down/S: brake    C: cruise control",
    };
    public override double? TargetSpeed => _cruise ? _cruiseSpeed : null;
    public override string? Status => _cruise ? $"CRUISE {_cruiseSpeed * Units.MphPerMs:0} MPH" : null;

    public override void Reset(PlayerCar car) { base.Reset(car); Pedal = car.Throttle; _cruise = false; }

    public override void Handle(GameAction a, PlayerCar car, Tuning t)
    {
        if (a.Kind != ActionKind.Cruise) return;
        _cruise = !_cruise;
        _cruiseSpeed = Math.Max(car.Speed, 5);
    }

    public override void Update(InputFrame input, PlayerCar car, Tuning t, double dt)
    {
        if (_cruise && input.Brake) _cruise = false;
        if (_cruise && !input.Gas) { HoldSpeed(car, t, _cruiseSpeed, dt); return; }
        Pedal = Ramp(Pedal, input.Gas, t.ThrottleRampUp, t.ThrottleRampDown, dt);
        BrakePedal = Ramp(BrakePedal, input.Brake, 6, 12, dt);
        Output(car);
    }
}

/// <summary>2: Up/Down move a target speed; the car speeds up or brakes to match it.</summary>
public sealed class SpeedLeverScheme : ThrottleSchemeBase
{
    double _target;

    public override string Name => "2 SPEED LEVER";
    public override string[] HelpLines => new[]
    {
        "Up/W raise target speed, Down/S lower it",
        "1-9, 0: jump to 10%-100% of top speed",
        "The car accelerates or brakes to match",
    };
    public override double? TargetSpeed => _target;
    public override string? Status => $"TARGET {_target * Units.MphPerMs:0} MPH";

    public override void Reset(PlayerCar car) { base.Reset(car); _target = car.Speed; }

    public override void Handle(GameAction a, PlayerCar car, Tuning t)
    {
        _target = a.Kind switch
        {
            ActionKind.LeverPreset => a.Value / 10.0 * t.MaxSpeed,
            ActionKind.WheelUp => _target + 5 / Units.MphPerMs,
            ActionKind.WheelDown => _target - 5 / Units.MphPerMs,
            _ => _target,
        };
        _target = Math.Clamp(_target, 0, t.MaxSpeed);
    }

    public override void Update(InputFrame input, PlayerCar car, Tuning t, double dt)
    {
        double rate = t.LeverRateMph / Units.MphPerMs;
        if (input.Gas) _target += rate * dt;
        if (input.Brake) _target -= rate * 1.6 * dt;
        _target = Math.Clamp(_target, 0, t.MaxSpeed);
        HoldSpeed(car, t, _target, dt);
    }
}

/// <summary>3: four gears. Low gears pull hard but top out early; Q/E shift.</summary>
public sealed class GearboxScheme : ThrottleSchemeBase
{
    static readonly double[] Tops = { 0.30, 0.52, 0.75, 1.0 };
    static readonly double[] Pull = { 1.6, 1.25, 1.0, 0.85 };
    double _maxSpeed = 70;

    public int Gear { get; private set; } = 1;
    public override string Name => "3 GEARBOX";
    public override string[] HelpLines => new[]
    {
        "Up/W gas (builds like the ramp pedal)   Down/S brake",
        "E or PgUp: shift up    Q or PgDn: shift down",
        "Low gears pull hard but top out early",
    };
    public override double? GearTopSpeed => Tops[Gear - 1] * _maxSpeed;
    public override double AccelMultiplier => Pull[Gear - 1];
    public override string? Status => $"GEAR {Gear}";

    public override void Reset(PlayerCar car)
    {
        base.Reset(car);
        Gear = 1;
        while (Gear < 4 && car.Speed > Tops[Gear - 1] * _maxSpeed * 0.9) Gear++;
    }

    public override void Handle(GameAction a, PlayerCar car, Tuning t)
    {
        if (a.Kind == ActionKind.ShiftUp) Gear = Math.Min(4, Gear + 1);
        if (a.Kind == ActionKind.ShiftDown) Gear = Math.Max(1, Gear - 1);
    }

    public override void Update(InputFrame input, PlayerCar car, Tuning t, double dt)
    {
        _maxSpeed = t.MaxSpeed;
        Pedal = Ramp(Pedal, input.Gas, t.ThrottleRampUp * 1.5, t.ThrottleRampDown, dt);
        BrakePedal = Ramp(BrakePedal, input.Brake, 6, 12, dt);
        Output(car);
    }
}

/// <summary>4: the mouse wheel sets a target speed; gas = boost, brake = slow down, both resume on release.</summary>
public sealed class WheelLeverScheme : ThrottleSchemeBase
{
    double _target;

    public override string Name => "4 SCROLL-WHEEL LEVER";
    public override string[] HelpLines => new[]
    {
        "Mouse wheel: target speed (5 mph per notch)",
        "Hold Up/W: full boost, let go to fall back to target",
        "Hold Down/S: brake, let go to resume target",
    };
    public override double? TargetSpeed => _target;
    public override string? Status => $"TARGET {_target * Units.MphPerMs:0} MPH";

    public override void Reset(PlayerCar car) { base.Reset(car); _target = car.Speed; }

    public override void Handle(GameAction a, PlayerCar car, Tuning t)
    {
        _target = a.Kind switch
        {
            ActionKind.WheelUp => _target + 5 / Units.MphPerMs,
            ActionKind.WheelDown => _target - 5 / Units.MphPerMs,
            ActionKind.LeverPreset => a.Value / 10.0 * t.MaxSpeed,
            _ => _target,
        };
        _target = Math.Clamp(_target, 0, t.MaxSpeed);
    }

    public override void Update(InputFrame input, PlayerCar car, Tuning t, double dt)
    {
        if (input.Brake)
        {
            Pedal = 0;
            BrakePedal = Ramp(BrakePedal, true, 6, 12, dt);
            Output(car);
        }
        else if (input.Gas)
        {
            BrakePedal = 0;
            Pedal = Ramp(Pedal, true, t.ThrottleRampUp * 2, t.ThrottleRampDown, dt);
            Output(car);
        }
        else
            HoldSpeed(car, t, _target, dt);
    }
}
