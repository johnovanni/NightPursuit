using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>What a chasing police car does in a box-in. The dispatcher hands the roles out every step.</summary>
public enum ChaseRole { Trail, Front, Side }

/// <summary>
/// A police car chasing you at Pursuit level (call 13). Its <see cref="State.Role"/> says what it does:
/// <list type="bullet">
/// <item><b>Trail</b>: in your lane about 20 m behind (more at speed, so it can stop when you brake hard); when you are
/// below 5 m/s it stops 6 m behind you, bumper to bumper. From beside or ahead of you it first gets out of your way and drops back;
/// more than 30 m ahead it never goes slower than 8 m/s, so it moves on rather than stop dead ahead of a slow you.</item>
/// <item><b>Front</b>: passes you on the box side (<see cref="DriverHelpers.PassingLane"/>), takes your lane about 12 m
/// ahead, and while within 25 m ahead slows to your speed - 4, never below 8 m/s.</item>
/// <item><b>Side</b>: holds level with you (|ΔD| &lt; 3) in the neighbouring lane on the box side, at your speed but never
/// below 3 m/s, so it rolls on past you if you stop. It never steers within 2.7 m of you.</item>
/// </list>
/// Within 150 m (or 5 s) before a roadblock, Front and Side act as Trail, so no police car is stopped in the gap. A Side
/// that would have to move into the third lane near you to take its place stands down to Trail (pre-flight F2); one
/// already in its lane is held back by speed instead (<see cref="PoliceLanes.SpeedCap"/>). While a rolling roadblock is
/// forming, or a formed one is within 80 m of you, Side acts as Trail (call 14), so the SUVs and the box never fill three
/// lanes and the SUVs have room to pass you; and while a formed pair is within 80 m, Front acts as Trail too (the pair is
/// the front of the box, and Front would otherwise pass you through its open lane or sit between you and the SUVs). Every lane goes through
/// <see cref="PoliceLanes.MayEnter"/>. It never rams: alongside it never steers into your line, and from behind in your
/// line it waits at your speed - 3. Blinded, the World drives it with <see cref="BlindDriver"/>, so smoke breaks a box-in.
/// No target speed is below 0 (F4).
/// </summary>
public sealed class ChaseDriver : IDriver
{
    public static readonly ChaseDriver Instance = new();

    public sealed class State
    {
        public ChaseRole Role { get; set; }
        /// <summary>-1 or +1: the side of you the box is on (Side's lane, and the side Front passes you on). The dispatcher keeps it the same for all chasers.</summary>
        public int Side { get; set; } = 1;
        public AvoidMemory Avoid { get; } = new();
        /// <summary>The rolling roadblock it made room for: while that one is on the road it does not take the Side role back.</summary>
        public RollingRoadblock? MadeRoomFor { get; internal set; }
        /// <summary>The role it last drove as: <see cref="Role"/>, or Trail when it dropped back near a roadblock or stood down.</summary>
        public ChaseRole ActingAs { get; private set; }

        internal void Acted(ChaseRole role) => ActingAs = role;
    }

    const double TrailGap = 20, TrailGain = 1.2, TrailDropBack = -6, CloseUp = 20;
    /// <summary>Below this speed (m/s) a Trail stops <see cref="StopGap"/> m behind you, bumper to bumper.</summary>
    const double StoppedBelow = 5, StopGap = 6;
    const double FrontAhead = 12, FrontGain = 1.0, FrontSlowWithin = 25, FrontSlower = 4, FrontMinSpeed = 8;
    const double SideGain = 1.5, SideDropBack = -8, SideMinSpeed = 3, SideClearance = 2.7, SideCloseIn = 15;
    const double RoadblockReach = 150, RoadblockSeconds = 5;
    /// <summary>
    /// While a chaser drops back past you near a roadblock it looks only this far ahead for it (it will be behind you,
    /// and in the gap, before it gets there); otherwise it would be steered back in front of you.
    /// </summary>
    const double FallBackLookAhead = 40, FallBackRoadblockSeconds = 1.5, WreckLookAhead = 120;
    /// <summary>Only a car this close ahead of you (or beside you) drops back past you that way; one farther ahead just takes the gap, metres.</summary>
    const double FallBackWithin = 30;
    /// <summary>
    /// A Trail car more than this far ahead of you never goes slower than <see cref="FrontMinSpeed"/>: dropping back at
    /// your speed - 6 would stop it dead in its lane ahead of you when you are slow, metres.
    /// </summary>
    const double KeepsMovingAhead = 30;
    /// <summary>How much slower than you a car beside or ahead of you drops back near a roadblock, m/s (otherwise <see cref="TrailDropBack"/>).</summary>
    const double RoadblockDropBack = -10;
    /// <summary>Bumper-to-bumper room needed to cross your line ahead of you (plus the closing speed × <see cref="CrossSeconds"/>) or behind you, metres.</summary>
    const double CrossGap = 2, CrossSeconds = 1.5;
    /// <summary>The deceleration a chaser plans to arrive at its spot with (agents can brake at 10), m/s².</summary>
    const double PlanBrake = 5;
    const double LaneSpacing = 5, LaneHalfWidth = 2.5, KeepOffMargin = 0.3, YieldSlower = 3, DropBackBeyond = 10;
    /// <summary>A chaser kept out of the clear lane stops this far short of the blocker, braking at <see cref="StopBrake"/> m/s².</summary>
    const double StopShort = 3, StopBrake = 8;
    /// <summary>How far beyond touching (along the road) a car still counts as beside this one when it moves over, metres.</summary>
    const double BesideReach = 4;
    const double Same = 1e-9;
    /// <summary>The bumper-to-bumper gap a Trail keeps behind the police car ahead of it in your lane, metres.</summary>
    const double QueueGap = 8;

    /// <summary>The role of a car driven by a ChaseDriver, or null.</summary>
    public static ChaseRole? RoleOf(AgentCar car) => car.Driver is ChaseDriver && car.DriverState is State s ? s.Role : null;

    /// <summary>
    /// True while <paramref name="car"/> is boxing you in as the Front car: it has the Front role, it drove as Front on its
    /// last step (not dropped back near a roadblock), and it is not blinded.
    /// </summary>
    public static bool IsBoxingFront(AgentCar car) =>
        car.Driver is ChaseDriver && car.DriverState is State { Role: ChaseRole.Front, ActingAs: ChaseRole.Front } && car.Blinded <= 0;

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
        {
            s = new State();
            self.DriverState = s;
        }
        var p = view.Player;
        double home = DriverHelpers.NearestLane(p.Lateral);
        double boxLane = BoxLane(home, s);
        var role = s.Role;
        bool roadblock = RoadblockNear(self, view);
        if (role != ChaseRole.Trail && roadblock) role = ChaseRole.Trail;
        if (role == ChaseRole.Side && (MakesRoomForRollingRoadblock(view, s) || !PoliceLanes.MayEnter(view, self, boxLane)))
            role = ChaseRole.Trail;   // makes room for a rolling roadblock (call 14), or stands down (F2)
        // A formed rolling roadblock within 80 m is the front of the box now: Front drops back as Trail, so it never passes
        // you through the gap the pair leaves and never sits between you and the SUVs.
        if (role == ChaseRole.Front && RollingRoadblock.GapToKeepClear(view.RollingRoadblocks, p.Distance) >= 0) role = ChaseRole.Trail;
        s.Acted(role);

        var (speed, lateral) = role switch
        {
            ChaseRole.Front => Front(self, p, home, boxLane),
            ChaseRole.Side => Side(self, p, boxLane, s.Side),
            _ => Trail(self, view, home, boxLane, roadblock),
        };
        bool fallingBack = role == ChaseRole.Trail && roadblock && !IsBehind(self, p) && self.Distance - p.Distance < FallBackWithin;
        lateral = DriverHelpers.SteerAroundWrecks(self, view, lateral, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt,
            fallingBack ? FallBackLookAhead : WreckLookAhead, fallingBack ? FallBackRoadblockSeconds : DriverHelpers.RoadblockLookAheadSeconds);
        if (!PoliceLanes.MayEnter(view, self, lateral)) lateral = PoliceLanes.AllowedLateral(view, self);
        if (CarBeside(self, view, lateral)) lateral = self.Lateral;   // never moves over into a car beside it
        lateral = KeepOffPlayer(self, p, lateral);
        // Kept out of the clear lane (by the three-lane rule, or by you beside it): it stops short of the blocker instead.
        speed = Math.Min(speed, StopShortOf(self, view, lateral));
        return new DriveIntent(Math.Max(0, speed), lateral);
    }

    /// <summary>In your lane behind you; from beside or ahead of you, out of your way first.</summary>
    static (double Speed, double Lateral) Trail(AgentCar self, WorldView view, double home, double boxLane, bool roadblock)
    {
        var p = view.Player;
        double dz = self.Distance - p.Distance, half = (self.Stats.Length + p.Stats.Length) / 2;
        double wanted = p.Speed < StoppedBelow
            ? -(StopGap + half)
            : -Math.Max(TrailGap, half + CrossGap + PoliceLanes.BrakingRoom(p.Speed));
        // Trails queue: behind the nearest police car between it and you in your lane, never level with it.
        if (CarAheadInLane(self, view, home) is { } leader)
            wanted = Math.Min(wanted, leader.Distance - p.Distance - (self.Stats.Length + leader.Stats.Length) / 2 - QueueGap);
        // Refused your lane near you (it would cover the third lane on the way): drop back past the hold distance,
        // change lane there and come back.
        if (!PoliceLanes.Covers(self, home) && !PoliceLanes.MayEnter(view, self, home))
            wanted = Math.Min(wanted, -(PoliceLanes.HoldBeyond(p.Speed) + DropBackBeyond));
        bool behind = IsBehind(self, p);
        // Near a roadblock a car beside or ahead of you drops back faster, so it is behind you before the barrier.
        double dropBack = roadblock && !behind ? RoadblockDropBack : TrailDropBack;
        double speed = Approach(p.Speed, dz, wanted, TrailGain, dropBack, CloseUp);
        if (dz > KeepsMovingAhead) speed = Math.Max(FrontMinSpeed, speed);   // well ahead of a slow you, it moves on
        // From beside or ahead of you it gets out of your way in the box lane, never in a rolling roadblock's open lane.
        int gap = RollingRoadblock.GapToKeepClear(view.RollingRoadblocks, p.Distance);
        double? dropLane = gap >= 0 && boxLane == Track.LaneCentres[gap] ? OtherSideLane(home, boxLane) : boxLane;
        double lateral = behind ? home : DriverHelpers.PassingLane(self, p, dropLane).Lateral;
        return (speed, lateral);
    }

    /// <summary>Past you on the box side, then into your lane 12 m ahead, slowing you.</summary>
    static (double Speed, double Lateral) Front(AgentCar self, PlayerCar p, double home, double boxLane)
    {
        double dz = self.Distance - p.Distance;
        bool holding = InLine(self, p) && self.Distance > p.Distance;    // already in your lane ahead of you
        bool inFront = holding || CanCrossInFront(self, p);
        double lateral, cap = double.PositiveInfinity;
        if (inFront) lateral = home;
        else
        {
            var line = DriverHelpers.PassingLane(self, p, boxLane);
            lateral = line.Lateral;
            cap = line.MaxSpeed;
        }
        double speed = holding && dz <= FrontSlowWithin
            ? p.Speed - FrontSlower
            : Math.Min(cap, Approach(p.Speed, dz, FrontAhead, FrontGain, TrailDropBack, CloseUp));
        if (inFront) speed = Math.Max(FrontMinSpeed, speed);
        return (speed, lateral);
    }

    /// <summary>Level with you in the box lane, never closer than 2.7 m to your line, never slower than 3 m/s.</summary>
    static (double Speed, double Lateral) Side(AgentCar self, PlayerCar p, double boxLane, int side)
    {
        double dz = self.Distance - p.Distance;
        double lateral = Math.Abs(boxLane - p.Lateral) < SideClearance ? p.Lateral + side * SideClearance : boxLane;
        double speed = dz < -SideCloseIn
            ? Approach(p.Speed, dz, 0, SideGain, SideDropBack, CloseUp)
            : DriverHelpers.AttackSpeed(p.Speed, dz);
        speed = Math.Max(SideMinSpeed, speed);
        // Still behind you in your line (it has not moved out yet): it waits, so it never runs into you.
        if (InLine(self, p) && self.Distance + self.Stats.Length / 2 <= p.Distance - p.Stats.Length / 2)
            speed = Math.Min(speed, Math.Max(0, p.Speed - YieldSlower));
        return (speed, lateral);
    }

    /// <summary>
    /// The fastest the car may go and still stop (at 8 m/s²) 3 m short of the nearest wreck or roadblock ahead that blocks
    /// <paramref name="lateral"/> within the roadblock look-ahead; infinite when none does.
    /// </summary>
    static double StopShortOf(AgentCar self, WorldView view, double lateral)
    {
        double reach = Math.Max(WreckLookAhead, self.Speed * DriverHelpers.RoadblockLookAheadSeconds), nearest = double.PositiveInfinity;
        foreach (var h in view.Hazards)
        {
            if (h is not ILaneBlocker blocker || !blocker.Blocks(lateral, self.Stats.Width / 2)) continue;
            double ahead = blocker.Distance - self.Distance;
            if (ahead > 0 && ahead < reach) nearest = Math.Min(nearest, ahead);
        }
        if (double.IsPositiveInfinity(nearest)) return nearest;
        double room = nearest - self.Stats.Length / 2 - Roadblock.Depth / 2 - StopShort;
        return Math.Sqrt(2 * StopBrake * Math.Max(0, room));
    }

    /// <summary>
    /// True when moving sideways toward <paramref name="lateral"/> would run into another car beside it: one within the two
    /// half widths + 0.3 m of that line, between the car's line and it, and level with it (within the half lengths + 4 m).
    /// </summary>
    internal static bool CarBeside(AgentCar self, WorldView view, double lateral)
    {
        if (Math.Abs(lateral - self.Lateral) < Same) return false;
        double lo = Math.Min(self.Lateral, lateral), hi = Math.Max(self.Lateral, lateral);
        foreach (var other in view.Agents)
        {
            if (ReferenceEquals(other, self) || !other.IsOnRoadDriving) continue;
            double band = (self.Stats.Width + other.Stats.Width) / 2 + KeepOffMargin;
            bool inPath = other.Lateral > lo - band && other.Lateral < hi + band && Math.Abs(other.Lateral - self.Lateral) >= Same
                && Math.Sign(other.Lateral - self.Lateral) == Math.Sign(lateral - self.Lateral);
            if (!inPath) continue;
            if (Math.Abs(other.Distance - self.Distance) < (self.Stats.Length + other.Stats.Length) / 2 + BesideReach) return true;
        }
        return false;
    }

    /// <summary>The nearest police car ahead of <paramref name="self"/> and behind you, in the lane centred at <paramref name="lane"/>; null if none.</summary>
    static AgentCar? CarAheadInLane(AgentCar self, WorldView view, double lane)
    {
        AgentCar? best = null;
        foreach (var other in view.Agents)
        {
            if (ReferenceEquals(other, self) || !other.IsOnRoadDriving || !KindRules.IsPolice(other.Kind)) continue;
            if (other.Distance <= self.Distance || other.Distance >= view.Player.Distance) continue;
            if (Math.Abs(other.Lateral - lane) >= LaneHalfWidth) continue;
            if (best is null || other.Distance < best.Distance) best = other;
        }
        return best;
    }

    /// <summary>The lane beside yours on the other side from <paramref name="boxLane"/>, or null when there is none.</summary>
    static double? OtherSideLane(double home, double boxLane)
    {
        double lane = 2 * home - boxLane;
        return Array.IndexOf(Track.LaneCentres, lane) >= 0 ? lane : null;
    }

    /// <summary>The lane beside yours on the box side; with no lane there (you are in an outer lane), the box changes side.</summary>
    static double BoxLane(double home, State s)
    {
        double lane = home + s.Side * LaneSpacing;
        if (Array.IndexOf(Track.LaneCentres, lane) >= 0) return lane;
        s.Side = -s.Side;
        return home + s.Side * LaneSpacing;
    }

    /// <summary>While an SUV of a rolling roadblock is this close to you (along the road), Side acts as Trail, metres.</summary>
    const double RollingRoadblockReach = 80;

    /// <summary>
    /// True while the car makes room for a rolling roadblock: one is still forming (its SUVs need a lane to pass you in),
    /// or a formed one has an SUV within 80 m of you, or the car already made room for one that is still on the road.
    /// Once it has yielded it does not take the Side role back until that pair is gone, so it never swings back and
    /// forth across a lane while the SUVs come and go.
    /// </summary>
    static bool MakesRoomForRollingRoadblock(WorldView view, State s)
    {
        double at = view.Player.Distance;
        bool kept = false;
        foreach (var rb in view.RollingRoadblocks)
        {
            if (ReferenceEquals(rb, s.MadeRoomFor)) kept = true;
            bool near = !rb.Formed;
            foreach (var car in rb.Cars)
                near |= Math.Abs(car.Distance - at) <= RollingRoadblockReach;
            if (near) { s.MadeRoomFor = rb; return true; }
        }
        if (!kept) s.MadeRoomFor = null;
        return kept;
    }

    /// <summary>A roadblock ahead of you or of the car within 150 m, or within 5 s of you at your speed.</summary>
    static bool RoadblockNear(AgentCar self, WorldView view)
    {
        var p = view.Player;
        double reach = Math.Max(RoadblockReach, p.Speed * RoadblockSeconds);
        foreach (var h in view.Hazards)
        {
            if (h is not Roadblock) continue;
            double toYou = h.Distance - p.Distance, toCar = h.Distance - self.Distance;
            if ((toYou > 0 && toYou <= reach) || (toCar > 0 && toCar <= reach)) return true;
        }
        return false;
    }

    /// <summary>
    /// <see cref="DriverHelpers.HoldGap"/>, but never closing on the spot it wants faster than it can still stop there at
    /// 5 m/s², so it never overshoots into you.
    /// </summary>
    static double Approach(double playerSpeed, double gap, double wanted, double gain, double lo, double hi)
    {
        double v = DriverHelpers.HoldGap(playerSpeed, gap, wanted, gain, lo, hi);
        double room = wanted - gap;
        return room > 0 ? Math.Min(v, playerSpeed + Math.Sqrt(2 * PlanBrake * room)) : v;
    }

    static double KeepOff(AgentCar self, PlayerCar p) => (self.Stats.Width + p.Stats.Width) / 2 + KeepOffMargin;

    static bool InLine(AgentCar self, PlayerCar p) => Math.Abs(self.Lateral - p.Lateral) < KeepOff(self, p);

    /// <summary>Its front is at least 2 m short of your rear.</summary>
    static bool IsBehind(AgentCar self, PlayerCar p) =>
        self.Distance + self.Stats.Length / 2 <= p.Distance - p.Stats.Length / 2 - CrossGap;

    /// <summary>Its rear is far enough past your front to cross your line: 2 m, plus 1.5 s of any speed you are closing on it at.</summary>
    static bool CanCrossInFront(AgentCar self, PlayerCar p)
    {
        double gap = self.Distance - self.Stats.Length / 2 - (p.Distance + p.Stats.Length / 2);
        return gap >= CrossGap + Math.Max(0, p.Speed - self.Speed) * CrossSeconds;
    }

    /// <summary>
    /// Beside you (neither clear behind nor clear to cross in front), a car out of your line never steers into it: its
    /// line is kept the two half widths + 0.3 m from yours on the side it is on.
    /// </summary>
    internal static double KeepOffPlayer(AgentCar self, PlayerCar p, double lateral)
    {
        if (IsBehind(self, p) || CanCrossInFront(self, p) || InLine(self, p)) return lateral;
        int side = DriverHelpers.SideOf(self, p.Lateral);
        double keepOff = KeepOff(self, p);
        return (lateral - p.Lateral) * side < keepOff ? p.Lateral + side * keepOff : lateral;
    }
}
