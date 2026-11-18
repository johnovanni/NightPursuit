using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Missions;

/// <summary>A supply crate that appears on the road. <paramref name="Lane"/> is -1, 0 or 1 (left, centre, right).</summary>
public sealed record CrateDefinition(double AtDistance, int Lane, SupplyItem Item, int Amount);
