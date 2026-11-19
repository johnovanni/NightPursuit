namespace NightPursuit.Core.Missions;

/// <summary>How busy the road is: civilian cars per kilometre (the director keeps about twice that many in its 2 km window).</summary>
public sealed record TrafficDefinition(double CarsPerKm);
