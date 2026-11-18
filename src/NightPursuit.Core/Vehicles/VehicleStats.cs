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
}
