namespace NightPursuit.Core.Hazards;

/// <summary>A police radar at the road's edge. Driving past it faster than <see cref="LimitMph"/> costs heat. It never collides with anything.</summary>
public sealed class SpeedTrap : Hazard
{
    public const double ClearBehind = 600;
    /// <summary>How far from the road centre it stands, metres (the sign is on a random side).</summary>
    public const double EdgeLateral = 9.0;

    /// <summary>The posted limit: 60, 70 or 80.</summary>
    public double LimitMph { get; set; } = 70;
    /// <summary>The player has passed it (it works once).</summary>
    public bool Triggered { get; set; }
    /// <summary>The player passed it over the limit (the App draws its sign red).</summary>
    public bool Caught { get; set; }

    public override void Update(double dt, double playerDistance)
    {
        if (Distance < playerDistance - ClearBehind) Expired = true;
    }
}
