namespace NightPursuit.Core.Simulation;

/// <summary>A banner for something big (a boss, a phase, a strike): a title and a line of detail, shown from <paramref name="Start"/> until <paramref name="Until"/> (world time). An <paramref name="Urgent"/> one (a boss or a boss phase) is not replaced by a non-urgent one while it is showing.</summary>
public sealed record Announcement(string Title, string Detail, double Start, double Until, bool Urgent = false);
