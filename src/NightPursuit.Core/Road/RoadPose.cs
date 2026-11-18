namespace NightPursuit.Core.Road;

/// <summary>A position in road coordinates: distance along the road, lateral offset (positive right), and heading relative to the road (radians, positive = turned right).</summary>
public readonly record struct RoadPose(double Distance, double Lateral, double Heading);
