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
    /// <summary>Seconds of lost steering after hitting an enemy's oil (counts down).</summary>
    public double GripLoss { get; set; }
    /// <summary>Seconds of wobble and a 100 mph limit after hitting an enemy's caltrops (counts down).</summary>
    public double PunctureLeft { get; set; }
    /// <summary>How long a puncture lasts, seconds (the HUD scales its bar by it).</summary>
    public const double PunctureSeconds = 5;
    /// <summary>While punctured the car is held to this speed, m/s (100 mph).</summary>
    public const double PunctureSpeedCap = 100 / Common.Units.MphPerMs;
    /// <summary>Total slowdown on the flat while punctured and above the cap, m/s².</summary>
    public const double PunctureBrakeDecel = 4.5;
    public int SpareCars { get; set; } = 3;
}
