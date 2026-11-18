using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// Drives a <see cref="BossCar"/>. It eases in at your speed - 10 m/s until you are within 140 m or hit it (call 21);
/// then its current phase's behaviour drives it, slowed by caltrops, and a leash keeps it from being lost: far behind it
/// closes at +25 m/s, far ahead it waits for you.
/// </summary>
public sealed class BossDriver : IDriver
{
    const double EngageWithin = 140;
    const double RunnerGapWanted = 70, RunnerGain = 0.6, RunnerRange = 10;
    const double TopSpeed = 88;
    const double PassFromBehind = 10, PassUntilAhead = 60, PassExtra = 15;
    const double LeashBehind = 120, LeashCatchUp = 25, LeashAhead = 600, LeashWait = 15;

    public static readonly BossDriver Instance = new();

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        var b = (BossCar)self;
        var p = view.Player;
        double dD = b.Distance - p.Distance;
        double delay = DriverHelpers.ReactionDelay(b.Kind);

        // Hits engage it where they land (World.DamageBoss / SpinBoss); a caltrops slow is seen here.
        if (!b.Engaged && (dD <= EngageWithin || b.SlowLeft > 0)) b.Engaged = true;
        if (!b.Engaged)
        {
            double holdLine = DriverHelpers.SteerAroundWrecks(b, view, b.Lateral, b.ArrivalAvoid, delay, dt);
            return new DriveIntent(Math.Max(0, p.Speed - BossCar.ArrivalSlower), holdLine);
        }

        var behaviour = b.Phase.Behaviour;
        var intent = behaviour switch
        {
            AgentKind.Pursuer => BossPursuitDriver.Instance.Decide(b, view, dt),
            AgentKind.Runner => RunnerDriver.Weaving.Decide(b, view, dt) with
            {
                TargetSpeed = DriverHelpers.HoldGap(p.Speed, dD, RunnerGapWanted, RunnerGain, -RunnerRange, RunnerRange),
            },
            _ => AgentCar.DriverFor(behaviour).Decide(b, view, dt),
        };

        intent = PassWhenLeftBehind(b, view, intent, dD, delay, dt);
        if (b.SlowLeft > 0) intent = intent with { TargetSpeed = intent.TargetSpeed * BossDamage.CaltropSlowFactor };
        if (dD < -LeashBehind) intent = intent with { TargetSpeed = Math.Min(TopSpeed, p.Speed + LeashCatchUp) };
        else if (dD > LeashAhead) intent = intent with { TargetSpeed = p.Speed - LeashWait };
        return intent with { TargetSpeed = Math.Max(0, intent.TargetSpeed) };   // never reverses toward a stopped player
    }

    /// <summary>
    /// A Blocker or Armored phase never overtakes on its own, so once more than 10 m behind, the boss passes you at
    /// up to your speed + 15 m/s in a lane at least 2.5 m from you, until it is 60 m ahead (preflight F5).
    /// </summary>
    static DriveIntent PassWhenLeftBehind(BossCar b, WorldView view, DriveIntent intent, double dD, double delay, double dt)
    {
        bool neverOvertakes = b.Phase.Behaviour is AgentKind.Blocker or AgentKind.Armored;
        if (!neverOvertakes || dD >= PassUntilAhead || (b.PassLane is null && dD >= -PassFromBehind))
        {
            b.PassLane = null;
            return intent;
        }

        var p = view.Player;
        var line = DriverHelpers.PassingLane(b, p, b.PassLane);
        b.PassLane = line.Lateral;
        double lateral = DriverHelpers.SteerAroundWrecks(b, view, line.Lateral, b.PassAvoid, delay, dt);
        return intent with
        {
            TargetSpeed = Math.Min(Math.Max(intent.TargetSpeed, Math.Min(TopSpeed, p.Speed + PassExtra)), line.MaxSpeed),
            TargetLateral = lateral,
        };
    }
}
