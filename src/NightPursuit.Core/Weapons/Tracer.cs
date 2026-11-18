using NightPursuit.Core.Road;

namespace NightPursuit.Core.Weapons;

/// <summary>A bullet. It flies in a straight line, not along the road, so on a bend it leaves the road unless aimed.</summary>
public sealed class Tracer
{
    public RoadPose Pose { get; set; }
    public double PrevDistance { get; set; }
    public double PrevLateral { get; set; }
    public double PrevHeading { get; set; }
    /// <summary>Metres of muzzle travel so far (compared with <see cref="Range"/>).</summary>
    public double Travel { get; set; }
    public double Speed { get; set; }
    /// <summary>How fast the muzzle travel grows per second (compared with <see cref="Range"/>).</summary>
    public double MuzzleSpeed { get; init; } = MachineGuns.MuzzleSpeed;
    public double Range { get; init; } = MachineGuns.Range;
    /// <summary>True for an enemy bullet: it hits only the player.</summary>
    public bool Hostile { get; init; }
    /// <summary>Fired by the boss, so a hit says "SHOT BY THE BOSS" (even if the boss is gone by the time it lands).</summary>
    public bool FromBoss { get; init; }
    public int OwnerId { get; set; }
    public bool Dead { get; set; }
}
