namespace NightPursuit.Core.Hazards;

/// <summary>Something fixed in the road that cars must steer round: a wreck or a roadblock.</summary>
public interface ILaneBlocker
{
    /// <summary>Where it is along the road, metres.</summary>
    double Distance { get; }

    /// <summary>
    /// True when a car whose centre is at <paramref name="lateral"/>, with half width <paramref name="halfWidth"/>, would have
    /// to steer round it. This is the avoidance test (it includes a 0.3 m margin), not the contact test.
    /// </summary>
    bool Blocks(double lateral, double halfWidth);
}
