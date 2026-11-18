namespace NightPursuit.Core.Road;

/// <summary>
/// A point on or beside the road in the player's frame: X right, F forward (depth), Y up, metres.
/// Heading is the road's direction here relative to the road direction at the player (radians, positive = turned right).
/// </summary>
public readonly record struct RoadPoint(double X, double F, double Y, double Heading);
