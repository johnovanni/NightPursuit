using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Weapons;

/// <summary>
/// A boss's slow homing missile (ENM-4). It closes on the player at exactly 25 m/s, from ahead or behind, so it is
/// always announced at least 2 s before it arrives. It follows your lane at 6 m/s until 1 s before impact, then holds
/// its line: a lane change in the last second dodges it, an earlier one does not. Your guns can shoot it down.
/// </summary>
public sealed class EnemyMissile
{
    public const double ClosingSpeed = 25, MinLaunchGap = 60, MaxLaunchGap = 160, LateralRate = 6, LockSeconds = 1.0;
    public const double HitHalfWidth = 1.4, ContactGap = 2.7, Damage = 25;
    /// <summary>Score for shooting one down.</summary>
    public const int ShotDownPoints = 50;

    /// <summary>
    /// What your bullets must cross to hit it: 1.2 m long and 1.6 m wide, plus a 0.15 m margin. The guns sit 0.7 m either
    /// side of your centre line, so anything narrower than about 1.2 m would slip between the two streams.
    /// </summary>
    public const double GunTargetHalfLength = 0.6, GunTargetHalfWidth = 0.8, GunTargetMargin = 0.15;

    public int Id { get; init; }
    public double Distance { get; set; }
    public double Lateral { get; set; }
    /// <summary>Where it was at the start of the latest step, for drawing (D18); a new missile starts with its spawn values.</summary>
    public double PrevDistance { get; set; }
    public double PrevLateral { get; set; }
    /// <summary>The lateral it steers toward: your lateral until it locks, then fixed.</summary>
    public double TargetLateral { get; set; }
    public double LaunchTime { get; init; }
    /// <summary>+1 when launched ahead of the player (it flies back toward you), -1 when launched behind.</summary>
    public int Dir { get; init; }
    /// <summary>
    /// True during the step it was launched in (it starts moving next step); cleared when the next step captures Prev.
    /// </summary>
    public bool JustLaunched { get; set; }
    /// <summary>
    /// The player's Distance when it was launched, until its first move: that move follows the player from here, so a
    /// push later in the launch step (a collision) is followed too. Null once it has moved, or for one placed by hand.
    /// </summary>
    public double? PlayerDistanceAtLaunch { get; set; }
    /// <summary>True from 1 s before impact: it no longer follows your lane.</summary>
    public bool Locked { get; set; }
    public bool Dead { get; set; }

    /// <summary>Seconds until it reaches you, 0 once it has.</summary>
    public double TimeToImpact(PlayerCar p) => Math.Max(0, (Math.Abs(Distance - p.Distance) - ContactGap) / ClosingSpeed);

    /// <summary>True when a car <paramref name="gap"/> metres from the player (either way) may launch: 60 to 160 m.</summary>
    public static bool InLaunchWindow(double gap) => Math.Abs(gap) >= MinLaunchGap && Math.Abs(gap) <= MaxLaunchGap;
}
