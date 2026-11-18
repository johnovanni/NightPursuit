namespace NightPursuit.Core.Vehicles;

public sealed class PlayerCar() : Vehicle(VehicleStats.Player)
{
    public override Faction Faction => Faction.Player;

    /// <summary>Pedal and wheel positions as last applied (for the HUD).</summary>
    public double Throttle { get; set; }
    public double Brake { get; set; }
    public double Steer { get; set; }
    /// <summary>Camera shake strength, decays by 1 per second.</summary>
    public double Shake { get; set; }
    public bool OffRoad { get; set; }
    public int SpareCars { get; set; } = 3;
}
