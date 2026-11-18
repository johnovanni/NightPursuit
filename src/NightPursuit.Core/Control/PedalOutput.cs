namespace NightPursuit.Core.Control;

/// <summary>Throttle and brake pedal positions, each 0 to 1.</summary>
public readonly record struct PedalOutput(double Throttle, double Brake);
