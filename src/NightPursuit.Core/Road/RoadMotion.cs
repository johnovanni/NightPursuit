namespace NightPursuit.Core.Road;

public static class RoadMotion
{
    /// <summary>Moves <paramref name="step"/> metres along a straight line (not along the road) and returns the new pose.</summary>
    public static RoadPose MoveStraight(Track track, RoadPose pose, double step)
    {
        double along = step * Math.Cos(pose.Heading);
        double lateral = pose.Lateral + step * Math.Sin(pose.Heading);
        double heading = pose.Heading - track.CurvatureAt(pose.Distance + along / 2) * along;
        return new RoadPose(pose.Distance + along, lateral, heading);
    }
}
