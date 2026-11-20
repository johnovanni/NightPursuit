using NightPursuit.Core.Drivers;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Police;

/// <summary>
/// The three-lane guarantee (call 17, HT-7; the rules are pre-flight F2's): moving police cars near you never cover all
/// three lanes, so there is always a lane out. It is applied every step to every Driving police car, blinded ones
/// included: its driver checks the lane it wants with <see cref="MayEnter"/>, and the World passes every police car's
/// final intent through <see cref="Restrain"/>.
/// <list type="bullet">
/// <item>A lane is covered by a car whose body overlaps it (<see cref="Covers"/>), so a car between two lanes covers both.</item>
/// <item>Only Driving police cars within <see cref="CountRange"/> (45 m) of you along the road count.</item>
/// <item>Inside 45 m a car may not steer anywhere that would cover the third lane at any moment on the way. If three
/// lanes are already covered (a push, or you braked hard), it may only move into lanes the others cover.</item>
/// <item>Farther out, the lane it steers to must leave a lane free, and a car whose body would make the third lane is
/// held back beyond the hold distance (<see cref="SpeedCap"/>) until it has moved into a lane the others use.</item>
/// <item>A car's own lane is never refused while it is not changing lane: a move that covers no lane the car does not
/// already cover is always allowed. If that lane makes three, the car is held back by speed instead (it falls back
/// behind you, or pulls away ahead).</item>
/// <item>Below Pursuit, cars on patrol (<see cref="PatrolDriver"/>) are neutral traffic: the rule never moves or holds them
/// (HT-3), but they count as coverage, so chasing cars yield to them. At Pursuit and above every police car near you is
/// held to it: the only ones still on patrol driving there are interceptors and SUVs falling back after a Manhunt.</item>
/// <item>The rule never steers a car into another car, into you, or into a wreck or roadblock (<see cref="AllowedLateral"/>).</item>
/// </list>
/// </summary>
public static class PoliceLanes
{
    /// <summary>Police cars within this distance of you along the road (centre to centre) count, metres.</summary>
    public const double CountRange = 45;
    /// <summary>A held car is kept beyond this distance (plus <see cref="BrakingRoom"/>), metres.</summary>
    public const double HoldDistance = 55;
    /// <summary>Half a lane: a car covers a lane when its body comes within this of the lane centre.</summary>
    const double HalfLane = 2.5;
    /// <summary>How much slower than you a held car behind drops back (or faster, ahead), m/s.</summary>
    const double HoldSlower = 4;
    /// <summary>The deceleration a held car plans to approach the hold distance with (agents can brake at 10), m/s².</summary>
    const double HoldBrake = 5;
    /// <summary>
    /// For a held car behind you: the braking it plans with (agents brake at 10 m/s²), the hardest braking it allows for
    /// you (you brake at about 15 m/s²), and the room it keeps beyond 45 m.
    /// </summary>
    const double HeldBrake = 8, PlayerBrake = 16, StopMargin = 5;
    /// <summary>
    /// How far from you (along the road) the other police count when a car changes lane or is held back, at least:
    /// far enough for a car coming in about 35 m/s faster than you to be slowed in time (50 + 35²/16 m), metres.
    /// </summary>
    const double ApproachReach = 130;
    const int AllLanes = 0b111;

    /// <summary>True when <paramref name="car"/>'s body overlaps the lane centred at <paramref name="laneCentre"/>.</summary>
    public static bool Covers(AgentCar car, double laneCentre) => Covers(car.Lateral, car.Stats.Width, laneCentre);

    /// <summary>True when a car of <paramref name="width"/> at <paramref name="lateral"/> would overlap the lane centred at <paramref name="laneCentre"/>.</summary>
    public static bool Covers(double lateral, double width, double laneCentre) => Math.Abs(lateral - laneCentre) < Reach(width);

    /// <summary>The number of lanes covered by Driving police cars within 45 m of you, <paramref name="except"/> left out.</summary>
    public static int CoveredLanes(WorldView view, AgentCar? except)
    {
        int mask = OthersMask(view, except);
        return (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1);
    }

    /// <summary>
    /// Extra hold distance for a car behind you, so it can still stop short of 45 m when you brake hard (you brake at about
    /// 15 m/s², an agent at 10: the difference in stopping distance is under v²/50), metres.
    /// </summary>
    public static double BrakingRoom(double playerSpeed) => playerSpeed * playerSpeed / 50;

    /// <summary>How far from you a car that would make the third lane is held, metres: 55 m plus <see cref="BrakingRoom"/>.</summary>
    public static double HoldBeyond(double playerSpeed) => HoldDistance + BrakingRoom(playerSpeed);

    /// <summary>How far from you the other police count for a lane change and for holding a car back: <see cref="HoldBeyond"/>, and at least 130 m.</summary>
    public static double LaneChangeReach(double playerSpeed) => Math.Max(HoldBeyond(playerSpeed), ApproachReach);

    /// <summary>
    /// <paramref name="wanted"/>, unless <paramref name="self"/>'s body would make the third lane covered with the police
    /// within 45 m: then it is held beyond <see cref="HoldBeyond"/>. Behind you it drives no faster than it can still stop
    /// there (at 5 m/s²), and within it at your speed - 4 (never below 0); ahead of you, at least as fast, mirrored, and
    /// your speed + 4 within it. Behind you it is also never faster than it could stop short of 45 m from if you braked
    /// as hard as you can. This holds an approaching car outside 45 m, and inside 45 m it moves a car that keeps its
    /// lane out of the way (its lane is never taken from it). Front is held like any chaser. A patrol car is never held
    /// below Pursuit. The car's body counts where it is and, given <paramref name="toward"/>, everywhere on its way there,
    /// so a car that starts moving into the third lane while still far away is already slowed on the braking curve. A
    /// car farther out also counts the police nearer to you than itself out to <see cref="LaneChangeReach"/>, so of two
    /// cars coming in at once the farther one yields in good time, not only when the nearer one crosses 45 m (the nearer
    /// one is never held by it).
    /// </summary>
    public static double SpeedCap(WorldView view, AgentCar self, double wanted, double? toward = null)
    {
        if (IsNeutral(view, self)) return wanted;
        var p = view.Player;
        double dz = self.Distance - p.Distance;
        double width = self.Stats.Width;
        int body = toward is double to ? PathMask(self.Lateral, to, Reach(width)) : MaskAt(self.Lateral, width);
        if ((HeldAgainst(view, self) | body) != AllLanes) return wanted;
        double room = Math.Abs(dz) - HoldBeyond(p.Speed);
        double margin = room <= 0 ? -HoldSlower : Math.Sqrt(2 * HoldBrake * room);
        if (dz >= 0) return Math.Max(wanted, p.Speed - margin);
        // Behind you it is also never faster than it can still stop short of 45 m if you brake as hard as you can from now.
        double stopRoom = Math.Max(0, -dz - CountRange - StopMargin);
        double safe = Math.Sqrt(2 * HeldBrake * stopRoom + HeldBrake / PlayerBrake * p.Speed * p.Speed);
        return Math.Max(0, Math.Min(wanted, Math.Min(p.Speed + margin, safe)));
    }

    /// <summary>
    /// Whether <paramref name="self"/> may steer to <paramref name="lateral"/> (a lane centre or any line). Inside 45 m
    /// (or when it could come within 45 m before it has got there): no moment on the way may cover all three lanes with
    /// the other police out to <see cref="HoldBeyond"/>, or, if three are covered already within 45 m, it must end in
    /// lanes the other police there cover. Farther out, to <see cref="LaneChangeReach"/>: where it ends up must leave a
    /// lane free with the other police out to there. Farther away: always. So a car never moves into the third lane just
    /// as another one comes within 45 m, nor makes the third lane for a car already coming in too fast to be held back.
    /// A move that covers no lane the car does not already cover (keeping its lane, or finishing a lane change) is always
    /// allowed (<see cref="SpeedCap"/> deals with the lanes it already covers), and a patrol car is never refused below
    /// Pursuit.
    /// </summary>
    public static bool MayEnter(WorldView view, AgentCar self, double lateral)
    {
        if (IsNeutral(view, self)) return true;
        var p = view.Player;
        double dz = Math.Abs(self.Distance - p.Distance), reach = LaneChangeReach(p.Speed);
        double width = self.Stats.Width;
        if ((PathMask(self.Lateral, lateral, Reach(width)) & ~MaskAt(self.Lateral, width)) == 0) return true;
        if (dz > reach) return true;
        int there = MaskAt(lateral, width);
        if (dz > CountRange && !ReachesCountRangeDuring(self, p, lateral, dz))
            return (OthersMask(view, self, reach) | there) != AllLanes;
        int counted = OthersMask(view, self);
        if ((counted | MaskAt(self.Lateral, width)) == AllLanes) return (there & ~counted) == 0;
        return !PathCoversAll(OthersMask(view, self, HoldBeyond(p.Speed)), self.Lateral, lateral, Reach(width));
    }

    /// <summary>
    /// True when <paramref name="self"/>, closing on you, could come within 45 m (plus 5 m) before it has moved across
    /// to <paramref name="lateral"/> at its side speed: then every moment of the move counts, not just where it ends up.
    /// </summary>
    static bool ReachesCountRangeDuring(AgentCar self, PlayerCar p, double lateral, double dz)
    {
        double closing = self.Distance < p.Distance ? self.Speed - p.Speed : p.Speed - self.Speed;
        if (closing <= 0) return false;
        double seconds = Math.Abs(lateral - self.Lateral) / self.Stats.SideSpeed;
        return dz - closing * seconds <= CountRange + StopMargin;
    }

    /// <summary>True when a police car of <paramref name="width"/> at <paramref name="lateral"/> near you would leave a lane free (for spawning).</summary>
    public static bool LeavesALaneFree(WorldView view, double lateral, double width) =>
        (OthersMask(view, null, HoldBeyond(view.Player.Speed)) | MaskAt(lateral, width)) != AllLanes;

    /// <summary>
    /// Where a car goes when the line it wants is refused: its current lane if it is allowed and no wreck or roadblock
    /// blocks it; otherwise the nearest other lane that is allowed, clear of wrecks and roadblocks, and clear of other cars
    /// and of you (as a keep-clear lane change needs); otherwise it keeps its current lane, and <see cref="SpeedCap"/>
    /// holds it back, or, when a wreck or roadblock blocks that lane, it keeps its own line (its driver stops it short).
    /// So the rule never steers a car into another car, into you, or toward a blocker.
    /// </summary>
    public static double AllowedLateral(WorldView view, AgentCar self)
    {
        double current = DriverHelpers.NearestLane(self.Lateral);
        bool currentClear = IsClear(view, self, current);
        if (currentClear && MayEnter(view, self, current)) return current;
        double? best = null;
        foreach (double lane in Track.LaneCentres)
            if (lane != current && MayEnter(view, self, lane) && IsClear(view, self, lane) && DriverHelpers.LaneClearOfCars(self, view, lane)
                && (best is null || Math.Abs(lane - self.Lateral) < Math.Abs(best.Value - self.Lateral)))
                best = lane;
        return best ?? (currentClear ? current : self.Lateral);
    }

    /// <summary>A car on patrol driving below Pursuit: neutral traffic the rule never moves or holds.</summary>
    static bool IsNeutral(WorldView view, AgentCar car) =>
        ReferenceEquals(car.Driver, PatrolDriver.Instance) && view.HeatLevel < HeatLevel.Pursuit;

    /// <summary>No wreck or roadblock ahead that the car would have to steer round at <paramref name="lateral"/>.</summary>
    static bool IsClear(WorldView view, AgentCar self, double lateral) =>
        Math.Abs(DriverHelpers.AvoidWrecks(self, view, lateral) - lateral) < 1e-9;

    /// <summary>
    /// The World's pass over a police car's final intent, every step: a refused line becomes <see cref="AllowedLateral"/>,
    /// the line is kept out of a formed rolling roadblock's open lane (<see cref="KeepGapClear"/>), the speed goes through
    /// <see cref="SpeedCap"/> (counting the lanes on its way to that line), and no target speed is below 0 (F4). Below
    /// Pursuit a patrol car's intent is left exactly as its driver made it.
    /// </summary>
    public static DriveIntent Restrain(WorldView view, AgentCar self, DriveIntent intent)
    {
        if (IsNeutral(view, self)) return intent;
        double lateral = MayEnter(view, self, intent.TargetLateral) ? intent.TargetLateral : AllowedLateral(view, self);
        lateral = KeepGapClear(view, self, lateral, out bool stuck);
        double speed = Math.Max(0, SpeedCap(view, self, intent.TargetSpeed, lateral));
        if (stuck) speed = GapFallBack(view.Player, self, speed);
        return intent with { TargetSpeed = speed, TargetLateral = lateral };
    }

    /// <summary>Police within this distance of you (along the road) keep a formed rolling roadblock's open lane clear, metres.</summary>
    public const double GapKeptClearWithin = 60;
    /// <summary>Below this speed (m/s) a car steering out of the gap may take a lane whose car ahead is at least <see cref="SlowRoom"/> m away.</summary>
    const double SlowBelow = 5, SlowRoom = 10;

    /// <summary>
    /// The one gap rule, applied last to every police car that is not one of the pair (so it also covers the World's
    /// keep-clear lane changes): while a formed pair's open lane is kept clear (<see cref="RollingRoadblock.GapToKeepClear"/>)
    /// and the car is within 60 m of you, it never steers to a line that covers that lane. Asked to, or already in it,
    /// it steers to the nearest lane that does not cover it and that the three-lane rule allows, with no wreck or roadblock
    /// ahead and room among the cars there; with no such lane it keeps its own line, and if it is in the gap it is
    /// <paramref name="stuck"/>: it falls back out of the 60 m (<see cref="GapFallBack"/>).
    /// </summary>
    static double KeepGapClear(WorldView view, AgentCar self, double lateral, out bool stuck)
    {
        stuck = false;
        if (self.Driver is RollingRoadblockDriver) return lateral;
        var p = view.Player;
        if (Math.Abs(self.Distance - p.Distance) > GapKeptClearWithin) return lateral;
        int gap = RollingRoadblock.GapToKeepClear(view.RollingRoadblocks, p.Distance);
        if (gap < 0) return lateral;
        double centre = Track.LaneCentres[gap], width = self.Stats.Width;
        if (!Covers(lateral, width, centre) && !Covers(self.Lateral, width, centre)) return lateral;
        double? best = null;
        foreach (double lane in Track.LaneCentres)
            if (!Covers(lane, width, centre) && MayEnter(view, self, lane) && IsClear(view, self, lane) && RoomToSteerOut(view, self, lane)
                && (best is null || Math.Abs(lane - self.Lateral) < Math.Abs(best.Value - self.Lateral)))
                best = lane;
        if (best is double b) return b;
        stuck = Covers(self.Lateral, width, centre);
        return Covers(lateral, width, centre) ? self.Lateral : lateral;
    }

    /// <summary>
    /// A car stuck in the gap within 60 m of you leaves by speed: behind you at most your speed - 4 (never below 0), ahead
    /// of you at least your speed + 4, as a car held back by the three-lane rule does.
    /// </summary>
    static double GapFallBack(PlayerCar p, AgentCar self, double speed) =>
        self.Distance < p.Distance ? Math.Min(speed, Math.Max(0, p.Speed - HoldSlower)) : Math.Max(speed, p.Speed + HoldSlower);

    /// <summary>
    /// Room to steer into <paramref name="lane"/>: the keep-clear span is free (<see cref="DriverHelpers.LaneClearOfCars"/>),
    /// or, both crawling (below 5 m/s), every car in that lane near it is crawling too and at least 10 m ahead of it,
    /// bumper to bumper (a crawling car cannot run into one 10 m ahead).
    /// </summary>
    static bool RoomToSteerOut(WorldView view, AgentCar self, double lane)
    {
        if (DriverHelpers.LaneClearOfCars(self, view, lane)) return true;
        if (self.Speed >= SlowBelow) return false;
        bool Fine(Vehicle other)
        {
            if (Math.Abs(other.Lateral - lane) >= (self.Stats.Width + other.Stats.Width) / 2 + 0.3) return true;
            double d = other.Distance - self.Distance, half = (self.Stats.Length + other.Stats.Length) / 2;
            if (d <= -(10 + half) || d >= 25 + half) return true;
            return other.Speed < SlowBelow && d - half >= SlowRoom;
        }
        if (!Fine(view.Player)) return false;
        foreach (var other in view.Agents)
            if (!ReferenceEquals(other, self) && other.IsOnRoadDriving && !Fine(other)) return false;
        return true;
    }

    static double Reach(double width) => HalfLane + width / 2;

    /// <summary>Bit i set: lane i (of <see cref="Track.LaneCentres"/>) is covered by a car of this width at this lateral.</summary>
    static int MaskAt(double lateral, double width)
    {
        double reach = Reach(width);
        int mask = 0;
        for (int i = 0; i < Track.LaneCentres.Length; i++)
            if (Math.Abs(lateral - Track.LaneCentres[i]) < reach) mask |= 1 << i;
        return mask;
    }

    /// <summary>The lanes covered by Driving police cars within <paramref name="range"/> (default 45 m) of you, <paramref name="except"/> left out.</summary>
    static int OthersMask(WorldView view, AgentCar? except, double range = CountRange)
    {
        double at = view.Player.Distance;
        int mask = 0;
        foreach (var a in view.Agents)
        {
            if (ReferenceEquals(a, except) || a.State != VehicleState.Driving || !KindRules.IsPolice(a.Kind)) continue;
            if (Math.Abs(a.Distance - at) > range) continue;
            mask |= MaskAt(a.Lateral, a.Stats.Width);
        }
        return mask;
    }

    /// <summary>
    /// The lanes <see cref="SpeedCap"/> holds <paramref name="self"/> against: those of the Driving police within 45 m of
    /// you, and of those out to <see cref="LaneChangeReach"/> that are nearer to you than <paramref name="self"/>.
    /// </summary>
    static int HeldAgainst(WorldView view, AgentCar self)
    {
        var p = view.Player;
        double own = Math.Abs(self.Distance - p.Distance), reach = LaneChangeReach(p.Speed);
        int mask = 0;
        foreach (var a in view.Agents)
        {
            if (ReferenceEquals(a, self) || a.State != VehicleState.Driving || !KindRules.IsPolice(a.Kind)) continue;
            double d = Math.Abs(a.Distance - p.Distance);
            if (d <= CountRange || (d < own && d <= reach)) mask |= MaskAt(a.Lateral, a.Stats.Width);
        }
        return mask;
    }

    /// <summary>Every lane a car with this <paramref name="reach"/> covers at some moment while moving straight from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static int PathMask(double from, double to, double reach)
    {
        double a = Math.Min(from, to), b = Math.Max(from, to);
        int mask = 0;
        for (int i = 0; i < Track.LaneCentres.Length; i++)
        {
            double c = Track.LaneCentres[i];
            double gap = c < a ? a - c : c > b ? c - b : 0;
            if (gap < reach) mask |= 1 << i;
        }
        return mask;
    }

    /// <summary>
    /// True when a car moving straight from <paramref name="from"/> to <paramref name="to"/> would at some moment cover
    /// every lane the others leave free: some point of the path lies within <paramref name="reach"/> of all of them at once.
    /// </summary>
    static bool PathCoversAll(int others, double from, double to, double reach)
    {
        int missing = AllLanes & ~others;
        if (missing == 0) return true;
        double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
        for (int i = 0; i < Track.LaneCentres.Length; i++)
        {
            if ((missing & (1 << i)) == 0) continue;
            lo = Math.Max(lo, Track.LaneCentres[i] - reach);
            hi = Math.Min(hi, Track.LaneCentres[i] + reach);
        }
        double a = Math.Min(from, to), b = Math.Max(from, to);
        return lo < hi && a < hi && b > lo;
    }
}
