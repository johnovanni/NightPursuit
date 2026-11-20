using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>
/// One stage of a boss fight: the boss drives as <paramref name="Behaviour"/> until its health falls to
/// <paramref name="EndsBelow"/> of the full amount (0 for the last phase).
/// </summary>
public sealed record BossPhaseDefinition(AgentKind Behaviour, double EndsBelow, bool FiresMissiles = false);

/// <summary>How a boss is drawn.</summary>
public enum BossLook { Fin, Twin, Dome }

/// <summary>A mission's boss: its name, full health, the phases it goes through in order, and how it looks.</summary>
public sealed record BossDefinition(string Name, double Health, IReadOnlyList<BossPhaseDefinition> Phases, BossLook Look = BossLook.Fin);
