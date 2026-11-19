namespace NightPursuit.Core.Vehicles;

/// <summary>Fixed per-kind numbers for a vehicle. Sizes in metres, speeds in m/s, Accel in m/s².</summary>
public sealed record VehicleStats(double Width, double Length, double Height, double TopSpeed, double Accel,
    double SideSpeed, double Armor, int Points)
{
    public static VehicleStats Player { get; } = new(1.9, 4.4, 1.3, 0, 0, 0, 100, 0);
    public static VehicleStats Runner { get; } = new(1.9, 4.4, 1.4, 58, 6, 3.5, 6, 100);
    public static VehicleStats Blocker { get; } = new(2.4, 6.0, 2.1, 40, 4, 2.8, 14, 300);
    public static VehicleStats Pursuer { get; } = new(1.9, 4.4, 1.15, 85, 8, 6, 10, 500);
    public static VehicleStats Gunner { get; } = new(1.9, 4.6, 1.3, 80, 7, 4.5, 9, 600);
    public static VehicleStats Dropper { get; } = new(2.0, 4.8, 1.8, 75, 6.5, 4.0, 9, 600);
    public static VehicleStats Armored { get; } = new(2.3, 5.2, 1.6, 64, 5.0, 3.0, 12, 900);
    public static VehicleStats Civilian { get; } = new(1.85, 4.5, 1.45, 38, 4, 2.5, 4, 0);
    public static VehicleStats PolicePatrol { get; } = new(1.9, 4.8, 1.45, 72, 6.5, 4.0, 8, 0);
    public static VehicleStats PoliceCruiser { get; } = new(1.9, 4.8, 1.45, 76, 7, 4.5, 10, 0);
    public static VehicleStats Interceptor { get; } = new(1.9, 4.7, 1.25, 80, 9, 6, 10, 0);
    /// <summary>Top speed 82, not 70: it must be able to catch a player near 67 m/s (pre-flight F1).</summary>
    public static VehicleStats ArmoredSuv { get; } = new(2.3, 5.2, 1.9, 82, 6, 3.5, 30, 0);
    /// <summary>A mission boss; its Armor is replaced by the boss definition's health.</summary>
    public static VehicleStats Boss { get; } = new(2.4, 5.6, 1.5, 88, 9, 5.5, 180, 5000);
}
