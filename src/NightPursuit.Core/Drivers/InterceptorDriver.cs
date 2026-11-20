using NightPursuit.Core.Police;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// A police interceptor at Manhunt (call 14): the Plan 1 pursuer pattern (come alongside, ram, retreat), driven by the
/// pursuer's own driver with the shared attack helpers (pre-flight F16: delegated, not copied), with every lane put
/// through <see cref="PoliceLanes.MayEnter"/>. Coming up from behind on a side the three-lane rule refuses, it tries
/// the other side; with neither allowed, in your line behind you it closes only to 80 m and then waits at your speed
/// - 3, so it never runs into the back of you. While a rolling roadblock is still forming it never rams and holds off at
/// least 80 m behind you in your lane, so the SUVs have room to pass you and take their lanes (the SUVs come first, call
/// 14). Its hits on you are the usual car-to-car contacts, and its ramming never costs you heat (a side contact is your
/// fault only when your own steering moved you into it). No target speed is below 0 (F4).
/// </summary>
public sealed class InterceptorDriver : IDriver
{
    public static readonly InterceptorDriver Instance = new();

    static readonly IDriver Pursuer = AgentCar.DriverFor(AgentKind.Pursuer);
    /// <summary>The pursuer's own lane limit (<see cref="PursuerDriver"/>), so the side test matches where it will steer.</summary>
    const double LaneLimit = 6.5;
    /// <summary>While a rolling roadblock forms, it keeps at least this far behind you (centre to centre), metres.</summary>
    const double HoldOffBehind = 80;
    /// <summary>The deceleration it plans to arrive at its hold-off spot with, m/s²; and how fast it drops back to it, m/s.</summary>
    const double HoldOffBrake = 5, HoldOffDropBack = 6;
    /// <summary>Room kept beyond the two half widths while it is in your line, metres; and how much slower than you it waits there, m/s.</summary>
    const double KeepOffMargin = 0.3, YieldSlower = 3;

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        var p = view.Player;
        bool holdOff = RollingRoadblockForming(view);
        if (self.DriverState is PursuerDriver.State s)
        {
            if (holdOff && s.Phase == PursuerPhase.Attack) s.Phase = PursuerPhase.Retreat;   // it never rams while the SUVs pass
            if (s.Phase == PursuerPhase.Approach && !holdOff) PickAllowedSide(self, view);
        }
        var intent = Pursuer.Decide(self, view, dt);
        double speed = intent.TargetSpeed, lateral = intent.TargetLateral;
        if (holdOff)
        {
            lateral = DriverHelpers.NearestLane(p.Lateral);
            double room = p.Distance - self.Distance - HoldOffBehind;
            speed = Math.Min(speed, room > 0 ? p.Speed + Math.Sqrt(2 * HoldOffBrake * room) : p.Speed - HoldOffDropBack);
        }
        if (!PoliceLanes.MayEnter(view, self, lateral)) lateral = PoliceLanes.AllowedLateral(view, self);
        // Behind you in your line (it could not get out of it): it closes only to 80 m, then waits.
        double keepOff = (self.Stats.Width + p.Stats.Width) / 2 + KeepOffMargin;
        double behind = p.Distance - p.Stats.Length / 2 - (self.Distance + self.Stats.Length / 2);
        if (Math.Abs(self.Lateral - p.Lateral) < keepOff && Math.Abs(lateral - p.Lateral) < keepOff && behind >= 0)
            speed = Math.Min(speed, behind < HoldOffBehind
                ? p.Speed - YieldSlower
                : p.Speed + Math.Sqrt(2 * HoldOffBrake * (behind - HoldOffBehind)));
        return intent with { TargetSpeed = Math.Max(0, speed), TargetLateral = lateral };
    }

    /// <summary>A rolling roadblock is on the road and has not formed yet.</summary>
    static bool RollingRoadblockForming(WorldView view)
    {
        foreach (var rb in view.RollingRoadblocks)
            if (!rb.Formed) return true;
        return false;
    }

    /// <summary>Approaching on a side whose attack line the three-lane rule refuses, it switches to the other side if that one is allowed.</summary>
    static void PickAllowedSide(AgentCar self, WorldView view)
    {
        double p = view.Player.Lateral;
        if (PoliceLanes.MayEnter(view, self, DriverHelpers.AttackLateral(p, self.Side, ramming: false, LaneLimit))) return;
        if (PoliceLanes.MayEnter(view, self, DriverHelpers.AttackLateral(p, -self.Side, ramming: false, LaneLimit))) self.Side = -self.Side;
    }
}
