using NightPursuit.Core.Missions;

namespace NightPursuit.Core.Session;

/// <summary>What finishing a mission pays: its base bonus, 10 per armor point left, and 1000 for losing no car.</summary>
public sealed record MissionBonus(int Base, int ArmorLeft, bool NoCarLost)
{
    public int ArmorPoints => ArmorLeft * 10;
    public int CleanPoints => NoCarLost ? 1000 : 0;
    public int Total => Base + ArmorPoints + CleanPoints;

    public static MissionBonus For(MissionDefinition def, double armor, bool carLost) =>
        new(def.Bonus, (int)Math.Floor(Math.Clamp(armor, 0, 100)), !carLost);
}
