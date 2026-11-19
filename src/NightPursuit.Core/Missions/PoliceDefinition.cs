namespace NightPursuit.Core.Missions;

/// <summary>The police in a mission: how many there are (0 none, 1 normal, 2 twice as many) and the heat the player starts with.</summary>
public sealed record PoliceDefinition(double Presence, double StartingHeat);
