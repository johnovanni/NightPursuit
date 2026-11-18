namespace NightPursuit.Core.Control;

/// <summary>Held controls for one frame (immutable). One-off presses arrive separately as <see cref="GameAction"/>s.</summary>
public sealed class ControlFrame
{
    /// <summary>A frame with nothing held.</summary>
    public static readonly ControlFrame Empty = new();

    /// <summary>-1 left, 0 straight, +1 right.</summary>
    public int SteerDir { get; init; }
    public bool Gas { get; init; }
    public bool Brake { get; init; }
    public bool Fire { get; init; }
    public bool PadConnected { get; init; }
    public bool PadFire { get; init; }
    public double PadSteer { get; init; }
    public double PadGas { get; init; }
    public double PadBrake { get; init; }
}
