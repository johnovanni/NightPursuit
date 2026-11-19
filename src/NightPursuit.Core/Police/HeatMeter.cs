using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Police;

/// <summary>
/// How much the police want the player: 0-100, with a level. Offences add heat; it dies down after a quiet spell.
/// All timers count whole simulation steps, so 5 s is exactly 600 steps and nothing drifts.
/// </summary>
public sealed class HeatMeter
{
    const double RiseFlashSeconds = 0.8;
    static readonly int DelaySteps = (int)Math.Round(HeatRules.DecayDelay / World.Step);
    static readonly int HoldSteps = (int)Math.Round(HeatRules.MinPoliceLevelSeconds / World.Step);

    int _sinceSteps;
    int _levelSteps;

    public double Value { get; private set; }
    public HeatLevel Level { get; private set; }
    /// <summary>Seconds since the last offence.</summary>
    public double SinceOffence => _sinceSteps * World.Step;
    /// <summary>1 when heat has just risen, counting down to 0 (for the HUD flash).</summary>
    public double RiseFlash { get; private set; }
    /// <summary>Seconds since the level was entered.</summary>
    public double LevelTime => _levelSteps * World.Step;

    /// <summary>Overrides the heat and the level (no hysteresis, no hold); returns the new level if it changed.</summary>
    public HeatLevel? Set(double value)
    {
        Value = Math.Clamp(value, 0, HeatRules.Max);
        var level = HeatRules.LevelOf(Value);
        if (level == Level) return null;
        Level = level;
        _levelSteps = 0;
        return level;
    }

    /// <summary>Adds (or removes) heat, restarts the quiet-spell timer and returns the new level if it changed.</summary>
    public HeatLevel? Add(double amount)
    {
        double before = Value;
        Value = Math.Clamp(Value + amount, 0, HeatRules.Max);
        _sinceSteps = 0;
        if (Value > before) RiseFlash = 1;
        return Reevaluate();
    }

    /// <summary>Runs <paramref name="dt"/> seconds (whole steps) of decay; returns the new level if it changed.</summary>
    public HeatLevel? Update(double dt, bool policeNear)
    {
        int steps = (int)Math.Round(dt / World.Step);
        HeatLevel? change = null;
        for (int i = 0; i < steps; i++)
        {
            // The quiet-spell wait is tested before the step is counted, so decay begins on step 601.
            if (_sinceSteps >= DelaySteps && Value > 0)
                Value = Math.Max(0, Value - (policeNear ? HeatRules.Decay : HeatRules.DecayNoPolice) * World.Step);
            _sinceSteps++;
            _levelSteps++;
            RiseFlash = Math.Max(0, RiseFlash - World.Step / RiseFlashSeconds);
            if (Reevaluate() is { } c) change = c;
        }
        return change;
    }

    /// <summary>Rising follows the thresholds; falling goes one level a step, and needs heat 5 below the level's threshold (Patrol and above also 20 s in the level).</summary>
    HeatLevel? Reevaluate()
    {
        var target = HeatRules.LevelOf(Value);
        if (target > Level)
        {
            Level = target;
            _levelSteps = 0;
            return Level;
        }
        if (Level == HeatLevel.Calm) return null;
        bool below = Value < HeatRules.ThresholdOf(Level) - HeatRules.LevelHysteresis;
        bool held = Level < HeatLevel.Patrol || _levelSteps >= HoldSteps;
        if (!below || !held) return null;
        Level--;
        _levelSteps = 0;
        return Level;
    }
}
