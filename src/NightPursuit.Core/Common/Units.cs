namespace NightPursuit.Core.Common;

/// <summary>Unit conversions. Core works in m/s; only the HUD shows mph.</summary>
public static class Units
{
    /// <summary>Miles per hour in one metre per second.</summary>
    public const double MphPerMs = 2.23694;

    public static double MphToMs(double mph) => mph / MphPerMs;

    public static double MsToMph(double ms) => ms * MphPerMs;
}
