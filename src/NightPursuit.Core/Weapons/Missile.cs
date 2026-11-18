using NightPursuit.Core.Road;

namespace NightPursuit.Core.Weapons;

/// <summary>The player's homing missile. It flies a straight line from its launch pose, steering toward its target.</summary>
public sealed class Missile
{
    /// <summary>Speed added to the player's own at launch, m/s.</summary>
    public const double LaunchBoost = 110;
    /// <summary>Metres flown before the missile gives up.</summary>
    public const double Range = 700;
    /// <summary>Homing turn rate, rad/s.</summary>
    public const double TurnRate = 1.5;
    /// <summary>Lateral slack added to a car's half width when testing a hit, metres.</summary>
    public const double HitMargin = 0.6;
    public const double LockRange = 500;
    public const double LockMin = 15;
    public const double LockHalfAngle = 25 * Math.PI / 180;
    public const double MaxHeading = 60 * Math.PI / 180;

    public RoadPose Pose { get; set; }
    public double PrevDistance { get; set; }
    public double PrevLateral { get; set; }
    public double PrevHeading { get; set; }
    public double Speed { get; set; }
    /// <summary>Metres flown so far.</summary>
    public double Travel { get; set; }
    public int? TargetId { get; set; }
    public bool Dead { get; set; }
}
