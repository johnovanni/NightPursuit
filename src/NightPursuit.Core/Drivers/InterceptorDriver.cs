using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// A police interceptor at Manhunt (call 14): the Plan 1 pursuer pattern (come alongside, ram, retreat), driven by the
/// pursuer's own driver with the shared attack helpers (pre-flight F16: delegated, not copied), with every lane put
/// through <see cref="PoliceLanes.MayEnter"/>.
/// <list type="bullet">
/// <item>Coming up from behind it attacks from a side whose attack line the three-lane rule allows and, while a formed
/// rolling roadblock is within 80 m of you, does not cover its open lane; it prefers a side with no car on the attack
/// line between it and you, so it gets round a car in its way. With no side allowed it holds off 80 m behind you.</item>
/// <item>While a rolling roadblock is still forming it never rams and holds off at least 80 m behind you in your lane, so
/// the SUVs have room to pass you and take their lanes (the SUVs come first, call 14).</item>
/// <item>Dropping back it never steers into that open lane either.</item>
/// <item>In your line behind you it closes only to 80 m and then waits at your speed - 3; attacking, it may come up
/// alongside, but while it is still in your line behind you it never closes faster than it can stop 1 m short of your
/// rear, even if you brake as hard as you can, so it never runs into the back of you.</item>
/// <item>An interceptor that finds itself in the gap (spun there, say) ends its attack.</item>
/// </list>
/// Its hits on you are the usual car-to-car contacts, and its ramming never costs you heat (a side contact is your fault
/// only when your own steering moved you into it). No target speed is below 0 (F4).
/// </summary>
public sealed class InterceptorDriver : IDriver
{
    public static readonly InterceptorDriver Instance = new();

    static readonly IDriver Pursuer = AgentCar.DriverFor(AgentKind.Pursuer);
    /// <summary>It holds off at least this far behind you (centre to centre), metres.</summary>
    const double HoldOffBehind = 80;
    /// <summary>The deceleration it plans to arrive at its hold-off spot with, m/s²; and how fast it drops back to it, m/s.</summary>
    const double HoldOffBrake = 5, HoldOffDropBack = 6;
    /// <summary>Room kept beyond the two half widths while it is in your line, metres; and how much slower than you it waits there, m/s.</summary>
    const double KeepOffMargin = 0.3, YieldSlower = 3;
    /// <summary>Attacking from behind in your line, it plans to stop this far short of your rear, metres.</summary>
    const double RearMargin = 1;
    /// <summary>The braking it plans with behind you, and the hardest it allows for you, m/s².</summary>
    const double RearBrake = 8, PlayerBrake = 16;
    /// <summary>A car on the attack line counts as in the way out to this far past you, metres.</summary>
    const double InTheWayPast = 5;

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        var p = view.Player;
        int gap = RollingRoadblock.GapToKeepClear(view.RollingRoadblocks, p.Distance);
        bool holdOff = RollingRoadblockForming(view);
        if (self.DriverState is PursuerDriver.State s)
        {
            // It never rams while the SUVs pass, nor from a line over the gap the roadblock leaves you.
            if (s.Phase == PursuerPhase.Attack && (holdOff || CoversGap(self, AttackLine(p, self.Side), gap) || CoversGap(self, self.Lateral, gap)))
                s.Phase = PursuerPhase.Retreat;
            if (s.Phase == PursuerPhase.Approach && !holdOff && !PickSide(self, view, gap)) holdOff = true;
        }
        var intent = Pursuer.Decide(self, view, dt);
        double speed = intent.TargetSpeed, lateral = intent.TargetLateral;
        // Dropping back, the pursuer heads for the lane farthest from you; that may be the gap, so it keeps out of it.
        if (CoversGap(self, lateral, gap)) lateral = NearestLaneClearOfGap(self, gap);
        if (holdOff)
        {
            lateral = DriverHelpers.NearestLane(p.Lateral);
            double room = p.Distance - self.Distance - HoldOffBehind;
            speed = Math.Min(speed, room > 0 ? p.Speed + Math.Sqrt(2 * HoldOffBrake * room) : p.Speed - HoldOffDropBack);
        }
        if (!PoliceLanes.MayEnter(view, self, lateral)) lateral = PoliceLanes.AllowedLateral(view, self);
        // Behind you in your line: outside an attack it closes only to 80 m and then waits; attacking, it may come up
        // alongside you, but while it is in your line it never closes faster than it can still stop 1 m short of you.
        double keepOff = (self.Stats.Width + p.Stats.Width) / 2 + KeepOffMargin;
        double behind = p.Distance - p.Stats.Length / 2 - (self.Distance + self.Stats.Length / 2);
        bool attacking = self.DriverState is PursuerDriver.State { Phase: PursuerPhase.Attack };
        bool inLine = Math.Abs(self.Lateral - p.Lateral) < keepOff;
        if (behind >= 0 && inLine && attacking)
        {
            // Close no faster than it could stop 1 m short of your rear, at its own pace and if you braked as hard as you
            // can (you brake at about 16 m/s², it plans 8): a car you brake in front of never runs into you.
            double room = Math.Max(0, behind - RearMargin);
            speed = Math.Min(speed, Math.Min(p.Speed + Math.Sqrt(2 * HoldOffBrake * room),
                Math.Sqrt(2 * RearBrake * room + RearBrake / PlayerBrake * p.Speed * p.Speed)));
        }
        else if (behind >= 0 && inLine && Math.Abs(lateral - p.Lateral) < keepOff)
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

    static double AttackLine(PlayerCar p, int side) =>
        DriverHelpers.AttackLateral(p.Lateral, side, ramming: false, PursuerDriver.LaneLimit);

    static bool CoversGap(AgentCar self, double line, int gap) =>
        gap >= 0 && PoliceLanes.Covers(line, self.Stats.Width, Track.LaneCentres[gap]);

    /// <summary>
    /// Chooses the side to come up on: one whose attack line is clear of your line (not squeezed by the road edge), that
    /// the three-lane rule allows and that does not cover <paramref name="gap"/>; of those, its own side unless a car is in the way on that line and not on the other.
    /// Returns false (keeping its side) when neither side may be used.
    /// </summary>
    static bool PickSide(AgentCar self, WorldView view, int gap)
    {
        var p = view.Player;
        bool Usable(int side)
        {
            // Not a side where the road edge squeezes the attack line into your line (it would come up into the back of you).
            double line = AttackLine(p, side);
            return Math.Abs(line - p.Lateral) >= (self.Stats.Width + p.Stats.Width) / 2 + KeepOffMargin
                   && PoliceLanes.MayEnter(view, self, line) && !CoversGap(self, line, gap);
        }
        bool own = Usable(self.Side), other = Usable(-self.Side);
        if (!own && !other) return false;
        if (!own || (other && InTheWay(self, view, AttackLine(p, self.Side)) && !InTheWay(self, view, AttackLine(p, -self.Side))))
            self.Side = -self.Side;
        return true;
    }

    /// <summary>The lane centre nearest the car that does not overlap the gap lane.</summary>
    static double NearestLaneClearOfGap(AgentCar self, int gap)
    {
        double best = self.Lateral, bestGap = double.MaxValue;
        foreach (double lane in Track.LaneCentres)
        {
            if (CoversGap(self, lane, gap) || Math.Abs(lane - self.Lateral) >= bestGap) continue;
            best = lane;
            bestGap = Math.Abs(lane - self.Lateral);
        }
        return best;
    }

    /// <summary>Another car on the road is on <paramref name="line"/> between this car and you (out to 5 m past you).</summary>
    static bool InTheWay(AgentCar self, WorldView view, double line)
    {
        double limit = view.Player.Distance + InTheWayPast;
        foreach (var other in view.Agents)
        {
            if (ReferenceEquals(other, self) || !other.IsOnRoadDriving) continue;
            if (other.Distance <= self.Distance || other.Distance > limit) continue;
            if (Math.Abs(other.Lateral - line) < (self.Stats.Width + other.Stats.Width) / 2 + KeepOffMargin) return true;
        }
        return false;
    }
}
