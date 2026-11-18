using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// Stays well ahead of the player and drops oil and caltrops behind it, one every 3.5 s or so. Each drop is announced by
/// 0.5 s of arming, and only happens where the player's front bumper is at least 1.3 s (and 60 m) from the hazard's near edge.
/// </summary>
public sealed class DropperDriver : IDriver
{
    const double DropEvery = 3.5, ArmSeconds = 0.5, RetrySeconds = 0.5;
    const double HoldMin = 80, HoldPerSpeed = 1.5, HoldExtra = 18.5, HoldGain = 0.8, HoldLo = -10, HoldHi = 12;
    const double ArmLateral = 2.5, ArmRange = 200;
    const double MinGap = 60, GapSeconds = 1.3;
    const double HazardClearance = 1.5;   // the hazard's near edge lies this far behind the dropper's rear bumper

    public sealed class State
    {
        public double Timer { get; set; } = DropEvery;
        /// <summary>Seconds of warning left; 0 when not arming.</summary>
        public double Arming { get; set; }
        public double Target { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    /// <summary>True while the car is warning that it is about to drop something; the HUD uses it to warn the player.</summary>
    public static bool IsArming(AgentCar c) => c.DriverState is State { Arming: > 0 };

    /// <summary>
    /// Metres from the player's front bumper to the near edge of the hazard <paramref name="dropper"/> would drop now
    /// (<paramref name="next"/> says which kind; only its type matters).
    /// </summary>
    public static double ReactionGap(AgentCar dropper, PlayerCar p, Hazard next) =>
        ReactionGap(dropper, p, next is OilSlick);

    /// <summary>The hazard's centre is where the reaction gap measures from: its near edge <see cref="HazardClearance"/> behind the dropper's rear bumper.</summary>
    public static double CentreBehind(AgentCar dropper, bool oil) =>
        dropper.Distance - dropper.Stats.Length / 2 - HazardClearance - HalfSize(oil);

    static double HalfSize(bool oil) => oil ? OilSlick.Radius : Caltrops.HalfLength;

    public static double ReactionGap(AgentCar dropper, PlayerCar p, bool oil) =>
        (CentreBehind(dropper, oil) - HalfSize(oil)) - (p.Distance + p.Stats.Length / 2);

    /// <summary>The gap the player must have to the hazard's near edge for it to be dropped.</summary>
    public static double RequiredGap(PlayerCar p) => Math.Max(MinGap, GapSeconds * p.Speed);

    /// <summary>Which hazard the car would drop now: true oil, false caltrops, null when neither dropper can be used.</summary>
    public static bool? NextDrop(AgentArms arms)
    {
        bool oilReady = arms.Oil is { Ready: true }, caltropsReady = arms.Caltrops is { Ready: true };
        if (arms.NextDropIsOil) return oilReady ? true : caltropsReady ? false : null;
        return caltropsReady ? false : oilReady ? true : null;
    }

    static bool GapHolds(AgentCar self, PlayerCar p, out bool hasDrop)
    {
        bool? oil = NextDrop(self.Arms);
        hasDrop = oil is not null;
        return oil is bool o && ReactionGap(self, p, o) >= RequiredGap(p);
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
            self.DriverState = s = new State { Target = DriverHelpers.NearestLane(self.Lateral) };

        var p = view.Player;
        double dD = self.Distance - p.Distance;
        double want = Math.Max(HoldMin, HoldPerSpeed * p.Speed) + HoldExtra;
        double speed = DriverHelpers.HoldGap(p.Speed, dD, want, HoldGain, HoldLo, HoldHi);
        bool drop = false;

        if (self.Arms.LastDropSkipped)           // the world refused the last drop: try again soon
        {
            self.Arms.LastDropSkipped = false;
            s.Timer = RetrySeconds;
        }

        if (s.Arming > 0)
        {
            s.Arming = DriverHelpers.Countdown(s.Arming, dt);
            if (s.Arming <= 0)
            {
                if (GapHolds(self, p, out _)) { drop = true; s.Timer = DropEvery; }
                else s.Timer = RetrySeconds;
            }
        }
        else
        {
            s.Target = DriverHelpers.NearestLane(p.Lateral);
            s.Timer = DriverHelpers.Countdown(s.Timer, dt);
            if (s.Timer <= 0 && view.CanSeePlayer(self) && Math.Abs(self.Lateral - p.Lateral) < ArmLateral
                && dD <= ArmRange && GapHolds(self, p, out _))
            {
                s.Arming = ArmSeconds;
            }
        }

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        return new DriveIntent(speed, lateral, DropRear: drop);
    }
}
