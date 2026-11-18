namespace NightPursuit.Core.Weapons;

/// <summary>How many charges of each weapon the player starts a mission with.</summary>
public sealed record LoadoutDefinition(int Oil = 5, int Smoke = 2, int Caltrops = 2, int Missiles = 1, int Strikes = 0)
{
    public static LoadoutDefinition Default { get; } = new();
}
