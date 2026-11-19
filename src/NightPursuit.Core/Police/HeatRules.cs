namespace NightPursuit.Core.Police;

/// <summary>Every heat number in one place: thresholds, what each offence adds, and how heat dies down.</summary>
public static class HeatRules
{
    public const double Watched = 25, Patrol = 50, Pursuit = 70, Manhunt = 90, Max = 100;

    public const double CivilianHit = 10, CivilianDestroyed = 25, PoliceHit = 15, SpeedTrap = 8, RoadblockHit = 15,
        PoliceDestroyed = 40, PoliceShot = 5;

    /// <summary>Seconds after an offence before heat starts to fall.</summary>
    public const double DecayDelay = 5;
    /// <summary>Heat lost per second with police near, and with none near.</summary>
    public const double Decay = 2, DecayNoPolice = 4;
    /// <summary>A police car this close (metres along the road) keeps the slow decay.</summary>
    public const double NoPoliceRadius = 300;
    /// <summary>A hit on a car counts as the player's doing for this long, and a car can add heat for a hit once per <see cref="HitCooldown"/>.</summary>
    public const double BlameSeconds = 5, HitCooldown = 1;

    /// <summary>A level is left only when heat is this far below its threshold, and Patrol and above are kept at least <see cref="MinPoliceLevelSeconds"/>.</summary>
    public const double LevelHysteresis = 5, MinPoliceLevelSeconds = 20;

    public static HeatLevel LevelOf(double heat) =>
        heat >= Manhunt ? HeatLevel.Manhunt :
        heat >= Pursuit ? HeatLevel.Pursuit :
        heat >= Patrol ? HeatLevel.Patrol :
        heat >= Watched ? HeatLevel.Watched : HeatLevel.Calm;

    /// <summary>The heat at which <paramref name="level"/> begins (Calm: 0).</summary>
    public static double ThresholdOf(HeatLevel level) => level switch
    {
        HeatLevel.Watched => Watched,
        HeatLevel.Patrol => Patrol,
        HeatLevel.Pursuit => Pursuit,
        HeatLevel.Manhunt => Manhunt,
        _ => 0,
    };

    public static string LevelName(HeatLevel level) => level switch
    {
        HeatLevel.Watched => "WATCHED",
        HeatLevel.Patrol => "PATROL",
        HeatLevel.Pursuit => "PURSUIT",
        HeatLevel.Manhunt => "MANHUNT",
        _ => "CALM",
    };
}
