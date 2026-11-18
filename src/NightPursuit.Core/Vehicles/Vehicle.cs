namespace NightPursuit.Core.Vehicles;

public enum VehicleState { Driving, Spinning, Crashed, Wrecked }

public enum Faction { Player, Hostile }

/// <summary>Common state for every car on the road. Positions are in road coordinates (metres along, metres across).</summary>
public abstract class Vehicle(VehicleStats stats)
{
    public int Id { get; set; }
    public VehicleStats Stats { get; } = stats;
    public abstract Faction Faction { get; }

    public double Distance { get; set; }
    public double Lateral { get; set; }
    /// <summary>Nose angle relative to the road direction, radians (positive points right).</summary>
    public double Heading { get; set; }
    public double Speed { get; set; }
    /// <summary>Sideways shove from collisions, m/s; decays quickly.</summary>
    public double PushVel { get; set; }
    public double LateralVel { get; set; }

    public double Armor { get; set; } = stats.Armor;
    public VehicleState State { get; set; } = VehicleState.Driving;
    public double StateTimer { get; set; }
    public double SpinAngle { get; set; }
    public double SpinRate { get; set; }
    public double HitFlash { get; set; }
    public double ContactCooldown { get; set; }

    // Where this car was at the start of the latest simulation step. The App draws a frame between these and the
    // current values (see World.Alpha); they are presentation-only and never feed the simulation or StateHash.
    public double PrevDistance { get; set; }
    public double PrevLateral { get; set; }
    public double PrevHeading { get; set; }
    public double PrevSpinAngle { get; set; }

    /// <summary>Remembers the current pose as the previous one; call after teleporting or creating a car so it is not drawn sweeping across the screen.</summary>
    public void SnapPrev()
    {
        PrevDistance = Distance;
        PrevLateral = Lateral;
        PrevHeading = Heading;
        PrevSpinAngle = SpinAngle;
    }

    public bool IsOnRoadDriving => State is VehicleState.Driving or VehicleState.Spinning;
}
