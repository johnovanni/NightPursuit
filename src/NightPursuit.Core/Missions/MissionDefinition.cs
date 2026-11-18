using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>One mission as described by a data file.</summary>
public sealed record MissionDefinition(
    string Id,
    string Title,
    string Briefing,
    TrackProfile Road,
    double ApproachLength,
    int StartingOil,
    IReadOnlyList<WaveDefinition> Waves);

/// <summary>
/// Enemies that appear once the player passes <paramref name="AtDistance"/>. A positive (or zero)
/// <paramref name="SpawnOffset"/> places them ahead of the player, a negative one behind; the cars of a wave are 40 m apart.
/// </summary>
public sealed record WaveDefinition(double AtDistance, AgentKind Kind, int Count, double SpawnOffset);
