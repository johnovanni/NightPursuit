using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Missions;

/// <summary>One mission as described by a data file.</summary>
public sealed record MissionDefinition(
    string Id,
    string Title,
    string Briefing,
    TrackProfile Road,
    double ApproachLength,
    int StartingOil,
    IReadOnlyList<WaveDefinition> Waves,
    LoadoutDefinition? StartingLoadout = null,
    IReadOnlyList<CrateDefinition>? Crates = null,
    BossDefinition? Boss = null)
{
    /// <summary>What the player starts with; oil always follows <see cref="StartingOil"/>.</summary>
    public LoadoutDefinition Loadout => (StartingLoadout ?? LoadoutDefinition.Default) with { Oil = StartingOil };
}

/// <summary>
/// Enemies that appear once the player passes <paramref name="AtDistance"/>. A positive (or zero)
/// <paramref name="SpawnOffset"/> places them ahead of the player, a negative one behind; the cars of a wave are 40 m apart.
/// </summary>
public sealed record WaveDefinition(double AtDistance, AgentKind Kind, int Count, double SpawnOffset);
