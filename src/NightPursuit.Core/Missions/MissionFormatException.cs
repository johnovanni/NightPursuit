namespace NightPursuit.Core.Missions;

/// <summary>A mission file is missing, unreadable or invalid. The message names the file and the field.</summary>
public sealed class MissionFormatException(string message) : Exception(message);
