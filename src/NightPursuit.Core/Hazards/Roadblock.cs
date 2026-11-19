using NightPursuit.Core.Road;

namespace NightPursuit.Core.Hazards;

/// <summary>
/// A police barrier across two of the three lanes. The third, <see cref="GapLane"/>, is always open (a roadblock whose gap is
/// blocked by a wreck is removed by the World). <see cref="Hazard.Lateral"/> is the gap lane's centre.
/// </summary>
public sealed class Roadblock : Hazard, ILaneBlocker
{
    /// <summary>How deep (along the road) the barrier is, and how far each blocked span reaches either side of its lane centre.</summary>
    public const double Depth = 2.0, BarrierHalfWidth = 2.3;
    public const double ClearBehind = 600;
    /// <summary>The extra room an AI car keeps (the same margin a wreck has).</summary>
    const double AvoidMargin = 0.3;

    /// <summary>The open lane: an index into <see cref="Track.LaneCentres"/> (0-2).</summary>
    public int GapLane { get; init; }
    /// <summary>The player has hit it, so it now blocks like a wreck and costs no more.</summary>
    public bool HitPlayer { get; set; }

    public static Roadblock Place(double distance, int gapLane) =>
        new() { Distance = distance, GapLane = gapLane, Lateral = Track.LaneCentres[gapLane] };

    public bool Blocks(double lateral, double halfWidth)
    {
        for (int i = 0; i < Track.LaneCentres.Length; i++)
            if (i != GapLane && Math.Abs(lateral - Track.LaneCentres[i]) < BarrierHalfWidth + halfWidth + AvoidMargin) return true;
        return false;
    }

    /// <summary>True when a box of this size at (<paramref name="distance"/>, <paramref name="lateral"/>) touches the barrier.</summary>
    public bool Touches(double distance, double lateral, double length, double width)
    {
        if (Math.Abs(distance - Distance) >= (Depth + length) / 2) return false;
        for (int i = 0; i < Track.LaneCentres.Length; i++)
            if (i != GapLane && Math.Abs(lateral - Track.LaneCentres[i]) < BarrierHalfWidth + width / 2) return true;
        return false;
    }

    public override void Update(double dt, double playerDistance)
    {
        if (Distance < playerDistance - ClearBehind) Expired = true;
    }
}
