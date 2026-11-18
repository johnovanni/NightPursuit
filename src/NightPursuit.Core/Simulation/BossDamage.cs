namespace NightPursuit.Core.Simulation;

/// <summary>How each weapon and contact hurts or hinders a boss (the damage table; sources are wired to it in the world).</summary>
public static class BossDamage
{
    /// <summary>Fractions of the boss's full health taken by one missile, one strike and the first touch of a wreck.</summary>
    public const double MissileFraction = 0.20, StrikeFraction = 0.35, WreckFraction = 0.10;
    /// <summary>Oil spins it for 1.5 s with its armor open (gunfire x2); in an Armored phase gunfire does a quarter.</summary>
    public const double OilSpinSeconds = 1.5, OpenArmorGunFactor = 2, ArmoredPhaseGunFactor = 0.25;
    /// <summary>Caltrops slow it to 0.6 of its wanted speed for 4 s; a new phase starts with 1.5 s of shield.</summary>
    public const double CaltropSlowSeconds = 4, CaltropSlowFactor = 0.6, PhaseShieldSeconds = 1.5;
    /// <summary>The hardest sideways shove a ram, car contact or wreck gives it, m/s; a wreck also keeps 0.7 of its speed.</summary>
    public const double MaxPush = 4, WreckSpeedFactor = 0.7;
}
