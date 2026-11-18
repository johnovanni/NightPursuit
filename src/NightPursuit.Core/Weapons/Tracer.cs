using NightPursuit.Core.Road;

namespace NightPursuit.Core.Weapons;

/// <summary>A bullet. It flies in a straight line, not along the road, so on a bend it leaves the road unless aimed.</summary>
public sealed class Tracer
{
    public RoadPose Pose { get; set; }
    public double PrevDistance { get; set; }
    public double PrevLateral { get; set; }
    public double PrevHeading { get; set; }
    /// <summary>Metres of muzzle travel so far (compared with <see cref="MachineGuns.Range"/>).</summary>
    public double Travel { get; set; }
    public double Speed { get; set; }
    public int OwnerId { get; set; }
    public bool Dead { get; set; }
}
