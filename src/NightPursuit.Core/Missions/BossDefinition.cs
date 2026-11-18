using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>
/// One stage of a boss fight: the boss drives as <paramref name="Behaviour"/> until its health falls to
/// <paramref name="EndsBelow"/> of the full amount (0 for the last phase).
/// </summary>
public sealed record BossPhaseDefinition(AgentKind Behaviour, double EndsBelow, bool FiresMissiles = false);

/// <summary>A mission's boss: its name, full health and the phases it goes through in order.</summary>
public sealed record BossDefinition(string Name, double Health, IReadOnlyList<BossPhaseDefinition> Phases);
