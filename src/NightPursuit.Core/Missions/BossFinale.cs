namespace NightPursuit.Core.Missions;

/// <summary>How a beaten boss leaves the road: a flat spin onto the verge, a sideways roll, or an end-over-end flip.</summary>
public enum FinaleKind { SpinOff, Roll, Flip }

/// <summary>
/// One boss crash: its kind, the side it leaves by (-1 left, +1 right) and, for a roll or flip, how many whole turns
/// (1 or 2). A spin-off always has 0 turns.
/// </summary>
public readonly record struct FinaleChoice
{
    public FinaleChoice(FinaleKind kind, int side, int turns)
    {
        Kind = kind;
        Side = side < 0 ? -1 : 1;
        Turns = kind == FinaleKind.SpinOff ? 0 : Math.Clamp(turns, 1, 2);
    }

    public FinaleKind Kind { get; }
    public int Side { get; }
    public int Turns { get; }

    /// <summary>The crash used when nobody picks one (tests, and runs made without a game).</summary>
    public static FinaleChoice Default => new(FinaleKind.Roll, 1, 1);
}

/// <summary>
/// The beaten boss's crash, worked out from the moment it died: a spin-out toward the chosen edge, then off the road
/// (a roll or flip, or more spinning), then the explosion and a hold before the results. Every pose is a pure function
/// of the time since the kill and the start values, so the same choice and start always give the same crash.
/// </summary>
public sealed class BossFinale
{
    public const double SpinOutSeconds = 1.2;
    public const double OffRoadSeconds = 1.3;
    public const double ExplodeAt = SpinOutSeconds + OffRoadSeconds;   // 2.5 s
    public const double HoldSeconds = 1.5;
    public const double TotalSeconds = ExplodeAt + HoldSeconds;        // 4.0 s
    /// <summary>Where the car comes to rest, metres from the centre line: well off the 7.5 m half-width road, on the verge.</summary>
    public const double VergeLateral = 11.0;
    const double MaxLift = 1.2;
    /// <summary>The share of its start speed the car still has when it trips at the road edge.</summary>
    const double TripSpeedFraction = 0.9;
    const double SpinOffTurns = 2.5;
    const double Eps = 1e-9;

    readonly double _d0, _l0, _v0, _s0;

    public BossFinale(FinaleChoice choice, int bossId, double distance, double lateral, double speed, double spinAngle)
    {
        Choice = choice;
        BossId = bossId;
        _d0 = distance;
        _l0 = lateral;
        _v0 = Math.Max(0, speed);
        _s0 = spinAngle;
        Pose(0);
        PrevRoll = Roll;
        PrevPitch = Pitch;
        PrevLift = Lift;
    }

    public FinaleChoice Choice { get; }
    public int BossId { get; }

    /// <summary>Where the car will come to rest and blow up (distance along the road), known from the start.</summary>
    public double RestDistance =>
        _d0 + _v0 * (SpinOutSeconds * (1 + TripSpeedFraction) / 2 + TripSpeedFraction * OffRoadSeconds / 2);
    public double Elapsed { get; private set; }

    public double Distance { get; private set; }
    public double Lateral { get; private set; }
    public double Speed { get; private set; }
    public double SpinAngle { get; private set; }
    /// <summary>Turn about the car's long axis, radians (a sideways roll); positive rolls toward the right.</summary>
    public double Roll { get; private set; }
    /// <summary>Turn end over end, radians; grows as the tail rises over the nose.</summary>
    public double Pitch { get; private set; }
    /// <summary>Metres above the ground while it rolls or flips.</summary>
    public double Lift { get; private set; }
    public double PrevRoll { get; private set; }
    public double PrevPitch { get; private set; }
    public double PrevLift { get; private set; }

    /// <summary>The car has blown up (never true for a skipped finale).</summary>
    public bool Exploded { get; private set; }
    /// <summary>The hold after the explosion is over, or the finale was skipped.</summary>
    public bool Done { get; private set; }
    public bool Skipped { get; private set; }

    public double RollAt(double alpha) => PrevRoll + (Roll - PrevRoll) * alpha;
    public double PitchAt(double alpha) => PrevPitch + (Pitch - PrevPitch) * alpha;
    public double LiftAt(double alpha) => PrevLift + (Lift - PrevLift) * alpha;

    /// <summary>Advances the crash by <paramref name="dt"/>; true on the one step it explodes.</summary>
    public bool Step(double dt)
    {
        if (Done) return false;
        PrevRoll = Roll;
        PrevPitch = Pitch;
        PrevLift = Lift;
        Elapsed += dt;
        Pose(Elapsed);
        bool exploded = false;
        if (!Exploded && Elapsed >= ExplodeAt - Eps)
        {
            Exploded = true;
            exploded = true;
        }
        if (Elapsed >= TotalSeconds - Eps) Done = true;
        return exploded;
    }

    /// <summary>Ends the finale at once, with no explosion.</summary>
    public void Skip()
    {
        Done = true;
        Skipped = true;
    }

    void Pose(double elapsed)
    {
        // Within a hair of the explosion counts as the explosion, so a run of 1/120 s steps lands exactly on it.
        double t = elapsed >= ExplodeAt - Eps ? Math.Max(elapsed, ExplodeAt) : elapsed;
        double e = Math.Min(t, ExplodeAt);
        int side = Choice.Side;

        // The car keeps most of its speed through the spin-out (down to 90%), so it stays ahead of you, then slows
        // evenly to rest on the verge by the explosion. Distance is the integral of that speed, so it never goes back.
        double s = Math.Min(e, SpinOutSeconds), u0 = Math.Clamp((e - SpinOutSeconds) / OffRoadSeconds, 0, 1);
        Speed = t >= ExplodeAt ? 0
            : t <= SpinOutSeconds ? _v0 * (1 - (1 - TripSpeedFraction) * t / SpinOutSeconds)
            : TripSpeedFraction * _v0 * (1 - u0);
        Distance = _d0 + _v0 * (s - (1 - TripSpeedFraction) * s * s / (2 * SpinOutSeconds))
                       + TripSpeedFraction * _v0 * OffRoadSeconds * (u0 - u0 * u0 / 2);
        Lateral = _l0 + (side * VergeLateral - _l0) * Smooth(e / ExplodeAt);

        if (Choice.Kind == FinaleKind.SpinOff)
            SpinAngle = _s0 + side * SpinOffTurns * Math.Tau * EaseOut(e / ExplodeAt);
        else
            SpinAngle = _s0 + side * Math.Tau * EaseOut(Math.Min(t / SpinOutSeconds, 1));   // one whole spin: square on when it trips

        double u = Math.Clamp((t - SpinOutSeconds) / OffRoadSeconds, 0, 1);
        double turn = Choice.Turns * Math.Tau * Smooth(u);
        Roll = Choice.Kind == FinaleKind.Roll ? side * turn : 0;
        Pitch = Choice.Kind == FinaleKind.Flip ? turn : 0;
        Lift = Choice.Kind != FinaleKind.SpinOff && u > 0 && u < 1 ? MaxLift * Math.Sin(Math.PI * u) : 0;
    }

    static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    static double EaseOut(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return 1 - (1 - x) * (1 - x);
    }
}
