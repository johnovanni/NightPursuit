namespace NightPursuit.Core.Audio;

/// <summary>
/// What the continuous sounds should be doing right now. The game builds one of these each frame and hands it
/// to <see cref="LiveVoices.Set"/>. Levels are 0 to 1; <see cref="SirenPan"/> is -1 (left) to +1 (right).
/// </summary>
public sealed record LiveState
{
    public bool EngineOn { get; init; }

    /// <summary>A fraction of the player's top speed.</summary>
    public double Speed01 { get; init; }

    public double Throttle01 { get; init; }

    /// <summary>A punctured tyre makes the engine pitch wobble.</summary>
    public bool Punctured { get; init; }

    public double OffRoad01 { get; init; }
    public double Skid01 { get; init; }
    public double Siren01 { get; init; }
    public double SirenPan { get; init; }
    public double Whine01 { get; init; }

    /// <summary>How close the missile is: 0 far (low whine) to 1 about to hit (high whine).</summary>
    public double WhineCloseness01 { get; init; }

    /// <summary>Everything off.</summary>
    public static LiveState Silent { get; } = new();
}
