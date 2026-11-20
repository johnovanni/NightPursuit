using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// Two armored SUVs that form a rolling roadblock at Manhunt (call 14). The police dispatcher makes it, keeps it in
/// <see cref="World.RollingRoadblocks"/> and forms it (pre-flight F3: the random outer lane is drawn from
/// <see cref="World.TrafficRng"/> there, when the pair is sent, never in a driver). Each SUV's slot is its index in
/// <see cref="Cars"/>. <paramref name="plannedOpen"/> is that random outer lane: the one left open if you are in the
/// middle lane when the pair forms, so the SUVs can pass you in a lane they will keep.
/// </summary>
public sealed class RollingRoadblock(AgentCar first, AgentCar second, int plannedOpen)
{
    readonly int[] _lanes = { -1, -1 };

    /// <summary>The two SUVs: slot 0 and slot 1.</summary>
    public IReadOnlyList<AgentCar> Cars { get; } = [first, second];
    /// <summary>The lane left open (an index into <see cref="Track.LaneCentres"/>); -1 until it has formed, then it never changes.</summary>
    public int OpenLane { get; private set; } = -1;
    /// <summary>The random outer lane drawn when the pair was sent (an index into <see cref="Track.LaneCentres"/>).</summary>
    public int PlannedOpen { get; } = plannedOpen;
    /// <summary>How long (seconds) an SUV of the formed pair has been in its open lane; the dispatcher keeps it.</summary>
    internal double InGapSeconds { get; set; }
    /// <summary>How long (seconds) an SUV of the formed pair near you has been off its lane; the dispatcher keeps it.</summary>
    internal double OffSlotSeconds { get; set; }
    /// <summary>How long (seconds) the pair has been formed; the dispatcher keeps it.</summary>
    internal double FormedSeconds { get; set; }

    /// <summary>Police keep the open lane of a formed pair clear while one of its SUVs is this close to you (along the road), metres.</summary>
    public const double GapReach = 80;

    /// <summary>
    /// The open lane (an index into <see cref="Track.LaneCentres"/>) of a formed pair with an SUV within 80 m of you at
    /// <paramref name="playerDistance"/>, which the other police keep clear; -1 when there is none.
    /// </summary>
    public static int GapToKeepClear(IReadOnlyList<RollingRoadblock> blocks, double playerDistance)
    {
        foreach (var rb in blocks)
        {
            if (!rb.Formed) continue;
            foreach (var car in rb.Cars)
                if (Math.Abs(car.Distance - playerDistance) <= GapReach) return rb.OpenLane;
        }
        return -1;
    }

    /// <summary>
    /// The lane that would be left open if the pair formed with you at <paramref name="playerLateral"/>: the outer lane
    /// farthest from yours, or <see cref="PlannedOpen"/> when you are in the middle lane.
    /// </summary>
    public int PredictedOpen(double playerLateral)
    {
        int yours = Array.IndexOf(Track.LaneCentres, DriverHelpers.NearestLane(playerLateral));
        int last = Track.LaneCentres.Length - 1;
        return yours == 0 ? last : yours == last ? 0 : PlannedOpen;
    }
    /// <summary>Both SUVs were at least 60 m ahead of you, so the open lane is locked and they hold the other two lanes.</summary>
    public bool Formed { get; private set; }

    /// <summary>The lane (an index into <see cref="Track.LaneCentres"/>) the SUV in <paramref name="slot"/> holds once formed; -1 before.</summary>
    public int LaneOf(int slot) => _lanes[slot];

    /// <summary>
    /// Locks the open lane (<see cref="PredictedOpen"/> for you at <paramref name="playerLateral"/>) and gives the SUVs the
    /// other two lanes, the left one to the SUV further left (on a tie, slot 0), so they never cross each other's path.
    /// </summary>
    internal void Form(double playerLateral)
    {
        int openLane = PredictedOpen(playerLateral);
        OpenLane = openLane;
        Formed = true;
        int lo = openLane == 0 ? 1 : 0, hi = openLane == 2 ? 1 : 2;
        bool swap = Cars[0].Lateral > Cars[1].Lateral;
        _lanes[0] = swap ? hi : lo;
        _lanes[1] = swap ? lo : hi;
    }

    /// <summary>
    /// True when the pair can no longer work: an SUV is wrecked, blinded or gone from the road (a respawn clear or a cull,
    /// F12), or, once formed, a wreck or roadblock blocks an SUV's lane, or the open lane, within 120 m ahead of an SUV
    /// (with the open lane blocked you would have no way past).
    /// </summary>
    internal bool IsBroken(IReadOnlyList<AgentCar> agents, IReadOnlyList<Hazard> hazards)
    {
        for (int slot = 0; slot < Cars.Count; slot++)
        {
            var car = Cars[slot];
            if (car.State == VehicleState.Wrecked || car.Blinded > 0 || !Contains(agents, car)) return true;
            if (Formed && (LaneBlockedAhead(car, Track.LaneCentres[_lanes[slot]], hazards)
                           || LaneBlockedAhead(car, Track.LaneCentres[OpenLane], hazards))) return true;
        }
        return false;
    }

    static bool Contains(IReadOnlyList<AgentCar> agents, AgentCar car)
    {
        foreach (var a in agents) if (ReferenceEquals(a, car)) return true;
        return false;
    }

    /// <summary>How far ahead of an SUV a wreck or roadblock in its lane, or in the open lane, breaks the formation, metres (the wreck look-ahead).</summary>
    const double BlockedWithin = 120;

    static bool LaneBlockedAhead(AgentCar car, double lane, IReadOnlyList<Hazard> hazards)
    {
        foreach (var h in hazards)
        {
            if (h is not ILaneBlocker blocker) continue;
            double ahead = blocker.Distance - car.Distance;
            if (ahead > 0 && ahead < BlockedWithin && blocker.Blocks(lane, car.Stats.Width / 2)) return true;
        }
        return false;
    }
}

/// <summary>
/// An armored SUV in a rolling roadblock (call 14). Before the pair forms it passes you at your speed + 15
/// (<see cref="DriverHelpers.PassingLane"/>) in a lane that is neither yours nor the one the pair will leave open, so it
/// is already in a lane it keeps (or, with no such lane allowed, on the side with room). Formed, it holds its lane side by side with the other
/// SUV, about 40 m ahead of you (<see cref="DriverHelpers.HoldGap"/>), and once you are within 40 m of both (they have
/// come back to their hold) the pair rides at your speed - 6, so you close gently and must take the open lane or brake.
/// Every lane goes through <see cref="PoliceLanes.MayEnter"/> (and the World holds it to <see cref="PoliceLanes.SpeedCap"/>). It never moves
/// over into a car beside it, never steers into your line beside you, and never closes on you from behind in your
/// line. No target speed is below 0 (F4). When the pair breaks, the World makes it a Trail chaser.
/// </summary>
public sealed class RollingRoadblockDriver : IDriver
{
    public static readonly RollingRoadblockDriver Instance = new();

    public sealed class State(RollingRoadblock block, int slot)
    {
        public RollingRoadblock Block { get; } = block;
        /// <summary>0 or 1: this SUV's index in <see cref="RollingRoadblock.Cars"/>.</summary>
        public int Slot { get; } = slot;
        public AvoidMemory Avoid { get; } = new();
    }

    /// <summary>How much faster than you an SUV passes you, m/s.</summary>
    public const double OvertakeFaster = 15;
    /// <summary>How far ahead of you the formed pair holds, and within it how much slower than you it rides.</summary>
    public const double HoldAhead = 40, RideSlower = 6;
    const double HoldGain = 1.0, HoldDropBack = -8, HoldCloseUp = 10;
    /// <summary>
    /// The hold closes on 40 m from above and never quite reaches it, so an SUV this close to it counts as there (you are
    /// within 40 m of it), metres.
    /// </summary>
    const double ArrivedSlack = 1;

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        var s = (State)self.DriverState!;
        var p = view.Player;
        double speed, lateral;
        if (!s.Block.Formed)
        {
            var line = DriverHelpers.PassingLane(self, p, PassLane(self, view, s.Block) ?? RoomySideLane(self, view));
            lateral = line.Lateral;
            speed = Math.Min(line.MaxSpeed, p.Speed + OvertakeFaster);
        }
        else
        {
            double lane = Track.LaneCentres[s.Block.LaneOf(s.Slot)];
            double gap = self.Distance - p.Distance;
            speed = YouAreWithinHold(s.Block, p)
                ? p.Speed - RideSlower
                : DriverHelpers.HoldGap(p.Speed, gap, HoldAhead, HoldGain, HoldDropBack, HoldCloseUp);
            lateral = lane;
            if (self.Distance <= p.Distance)
            {
                // Level with or behind you (you went through the gap): it passes you again, never closing on you in
                // your line nor swerving across you. Ahead of you it keeps its lane even as you close on it.
                var line = DriverHelpers.PassingLane(self, p, lane);
                lateral = line.Lateral;
                speed = Math.Min(speed, line.MaxSpeed);
            }
        }
        lateral = DriverHelpers.SteerAroundWrecks(self, view, lateral, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        if (!PoliceLanes.MayEnter(view, self, lateral)) lateral = PoliceLanes.AllowedLateral(view, self);
        if (ChaseDriver.CarBeside(self, view, lateral)) lateral = self.Lateral;
        lateral = ChaseDriver.KeepOffPlayer(self, p, lateral);
        return new DriveIntent(Math.Max(0, speed), lateral);
    }

    /// <summary>
    /// You are within 40 m (plus the arrival slack) behind both SUVs, each with a real gap ahead of you (at least the two
    /// half lengths + 3 m), so the pair rides at your speed - 6 together and stays side by side; until then each one
    /// holds 40 m ahead of you. (An SUV beside you never rides slower than you, so it never hovers there.)
    /// </summary>
    static bool YouAreWithinHold(RollingRoadblock block, PlayerCar p)
    {
        foreach (var car in block.Cars)
        {
            double gap = car.Distance - p.Distance;
            if (gap < (car.Stats.Length + p.Stats.Length) / 2 + RealGap || gap > HoldAhead + ArrivedSlack) return false;
        }
        return true;
    }

    /// <summary>The bumper-to-bumper room an SUV must have ahead of you to count as ahead of you in its hold, metres.</summary>
    const double RealGap = 3;

    /// <summary>
    /// Where an SUV passes you before the pair forms: its own lane if that is neither yours (at least 2.5 m from you) nor
    /// the lane the pair will leave open (<see cref="RollingRoadblock.PredictedOpen"/>), and the three-lane rule lets it;
    /// otherwise the nearest such lane; null when there is none.
    /// </summary>
    static double? PassLane(AgentCar self, WorldView view, RollingRoadblock block)
    {
        var p = view.Player;
        int open = block.PredictedOpen(p.Lateral);
        double mine = DriverHelpers.NearestLane(self.Lateral);
        double? best = null;
        for (int i = 0; i < Track.LaneCentres.Length; i++)
        {
            double lane = Track.LaneCentres[i];
            if (i == open || Math.Abs(lane - p.Lateral) < DriverHelpers.PassClearance || !PoliceLanes.MayEnter(view, self, lane)) continue;
            if (lane == mine) return lane;
            if (best is null || Math.Abs(lane - self.Lateral) < Math.Abs(best.Value - self.Lateral)) best = lane;
        }
        return best;
    }

    /// <summary>
    /// The side with room: the lane farthest from you on the SUV's own side of you, or else on the other side, that the
    /// three-lane rule lets it use; null when neither may be used (then <see cref="DriverHelpers.PassingLane"/> decides).
    /// </summary>
    static double? RoomySideLane(AgentCar self, WorldView view)
    {
        var p = view.Player;
        int own = DriverHelpers.SideOf(self, p.Lateral);
        for (int side = own, k = 0; k < 2; k++, side = -side)
            if (DriverHelpers.ClearLaneOnSide(p.Lateral, side) is double lane && PoliceLanes.MayEnter(view, self, lane))
                return lane;
        return null;
    }
}
