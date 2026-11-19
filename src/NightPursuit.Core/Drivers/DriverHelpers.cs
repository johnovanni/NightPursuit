using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>What a driver is currently doing about wrecks, kept in its per-car state.</summary>
public sealed class AvoidMemory
{
    /// <summary>True while the car is steering to <see cref="Lateral"/> instead of its own wish.</summary>
    public bool Active { get; set; }
    public double Lateral { get; set; }
    /// <summary>How long the new answer has been waiting to take over.</summary>
    public double Timer { get; set; }
}

/// <summary>What a car is currently doing about the car ahead of it, kept on the car (<see cref="AgentCar.AheadMemory"/>).</summary>
public sealed class CarAheadMemory
{
    /// <summary>True while the car is changing lane or following instead of doing as its driver wishes.</summary>
    public bool Active { get; set; }
    /// <summary>The lane it is changing to (when not <see cref="Follow"/>).</summary>
    public double Lateral { get; set; }
    /// <summary>The speed it is holding behind the car ahead (when <see cref="Follow"/>).</summary>
    public double Speed { get; set; }
    /// <summary>True: no lane is clear, so it follows; false: it changes to <see cref="Lateral"/>.</summary>
    public bool Follow { get; set; }
    /// <summary>How long the new answer has been waiting to take over.</summary>
    public double Timer { get; set; }
}

public static class DriverHelpers
{
    const double Margin = 0.3;
    const double Same = 1e-9;

    /// <summary>
    /// Returns <paramref name="desiredLateral"/> if no wreck within <paramref name="lookAhead"/> metres ahead blocks it;
    /// otherwise the clear lane centre nearest to it (ties go to the one nearest the car), or the desire again if every lane is blocked.
    /// </summary>
    public static double AvoidWrecks(AgentCar self, WorldView view, double desiredLateral, double lookAhead = 120)
    {
        if (!IsBlocked(self, view, desiredLateral, lookAhead)) return desiredLateral;

        double best = desiredLateral;
        bool found = false;
        foreach (double lane in Track.LaneCentres)
        {
            if (IsBlocked(self, view, lane, lookAhead)) continue;
            if (!found || IsBetter(lane, best, desiredLateral, self.Lateral))
            {
                best = lane;
                found = true;
            }
        }
        return best;
    }

    static bool IsBetter(double lane, double best, double desired, double current)
    {
        double a = Math.Abs(lane - desired), b = Math.Abs(best - desired);
        if (Math.Abs(a - b) > Same) return a < b;
        return Math.Abs(lane - current) < Math.Abs(best - current);
    }

    static bool IsBlocked(AgentCar self, WorldView view, double lateral, double lookAhead)
    {
        foreach (var h in view.Hazards)
        {
            if (h is not Wreck wreck) continue;
            double ahead = wreck.Distance - self.Distance;
            if (ahead <= 0 || ahead >= lookAhead) continue;
            if (Math.Abs(wreck.Lateral - lateral) < (wreck.Width + self.Stats.Width) / 2 + Margin) return true;
        }
        return false;
    }

    /// <summary>
    /// Passes a wish through <see cref="AvoidWrecks"/> with a reaction delay: when the avoiding answer changes
    /// (a wreck appears or is passed) the car keeps its previous steering until <paramref name="reactionDelay"/> seconds have gone by.
    /// </summary>
    public static double SteerAroundWrecks(AgentCar self, WorldView view, double desiredLateral,
        AvoidMemory memory, double reactionDelay, double dt)
    {
        double avoided = AvoidWrecks(self, view, desiredLateral);
        bool swerving = Math.Abs(avoided - desiredLateral) > Same;
        bool unchanged = swerving == memory.Active && (!swerving || Math.Abs(avoided - memory.Lateral) < Same);
        if (unchanged)
            memory.Timer = 0;
        else
        {
            memory.Timer += dt;
            if (memory.Timer >= reactionDelay - Same)
            {
                memory.Active = swerving;
                memory.Lateral = avoided;
                memory.Timer = 0;
            }
        }
        return memory.Active ? memory.Lateral : desiredLateral;
    }

    /// <summary>Seconds a driver of this kind takes to react to a wreck or a car ahead.</summary>
    public static double ReactionDelay(AgentKind kind) => kind switch
    {
        AgentKind.Runner => 0.5,
        AgentKind.Blocker => 0.8,
        AgentKind.Pursuer => 0.3,
        AgentKind.Gunner => 0.4,
        AgentKind.Dropper => 0.5,
        AgentKind.Armored => 0.6,
        AgentKind.Boss => 0.3,
        AgentKind.Civilian => 0.7,
        AgentKind.PolicePatrol => 0.5,
        AgentKind.PoliceCruiser => 0.4,
        AgentKind.Interceptor => 0.3,
        AgentKind.ArmoredSuv => 0.5,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no reaction delay for this kind"),
    };

    /// <summary>
    /// The speed that holds a car <paramref name="wanted"/> metres ahead of the player: the player's speed plus a push
    /// proportional to how far the gap is from wanted, kept within [<paramref name="lo"/>, <paramref name="hi"/>].
    /// </summary>
    public static double HoldGap(double playerSpeed, double gap, double wanted, double gain, double lo, double hi) =>
        playerSpeed + Math.Clamp((wanted - gap) * gain, lo, hi);

    /// <summary>Counts a timer down by <paramref name="dt"/>, landing on exactly 0 rather than a rounding error above it.</summary>
    public static double Countdown(double timer, double dt)
    {
        timer -= dt;
        return timer < SnapBelow ? 0 : timer;
    }

    const double SnapBelow = 1e-9;

    /// <summary>A pursuer's attack lasts this long once it is alongside; it rams for the last <see cref="RamSeconds"/>.</summary>
    public const double AttackSeconds = 3.5, RamSeconds = 2.4;
    /// <summary>How far to the side of the player a pursuer holds before it rams, metres.</summary>
    public const double AttackOffset = 2.7;
    const double AttackGain = 1.5, AttackSpeedRange = 8, RamOffset = 0.8;

    /// <summary>The speed that brings a pursuer level with the player: 1.5 m/s per metre of gap, at most 8 m/s either way.</summary>
    public static double AttackSpeed(double playerSpeed, double gap) =>
        HoldGap(playerSpeed, gap, 0, AttackGain, -AttackSpeedRange, AttackSpeedRange);

    /// <summary>
    /// Where a pursuer steers alongside the player on its <paramref name="side"/>: 2.7 m off (kept within
    /// <paramref name="limit"/> of the road centre), or 0.8 m off while it rams.
    /// </summary>
    public static double AttackLateral(double playerLateral, int side, bool ramming, double limit) =>
        ramming ? playerLateral + side * RamOffset : Math.Clamp(playerLateral + side * AttackOffset, -limit, limit);

    /// <summary>How far from the road centre a car's centre can be with all of the car on the road.</summary>
    public static double OnRoadLimit(Vehicle v) => Track.RoadHalfWidth - v.Stats.Width / 2;

    /// <summary>The least sideways distance from the player's centre to a lane a passing car may use.</summary>
    public const double PassClearance = 2.5;

    /// <summary>
    /// The bumper-to-bumper gap a passing car needs before it may cross the player's path: its rear this far past the
    /// player's front to cross in front, or its front this far short of the player's rear to cross behind, metres.
    /// </summary>
    const double CrossGap = 2;
    /// <summary>Room kept beyond the two cars' half widths while holding a line alongside, metres.</summary>
    const double HoldLineMargin = 0.3;
    /// <summary>How much slower than the player a car with no way past waits, m/s.</summary>
    const double YieldSlower = 3;

    /// <summary>Where a passing car steers, and the most it may drive at (infinite when there is no limit).</summary>
    public readonly record struct PassingLine(double Lateral, double MaxSpeed);

    /// <summary>
    /// Where to pass the player, so the car never swerves across the player's path while alongside and never drives
    /// into them. <paramref name="current"/> is kept while it is a lane centre at least <see cref="PassClearance"/> from
    /// the player. Otherwise, in order:
    /// <list type="number">
    /// <item>the lane farthest from the player on the car's own side of them (see <see cref="SideOf"/>) that is at least
    /// <see cref="PassClearance"/> away;</item>
    /// <item>once its rear is 2 m past the player's front, the lane farthest from the player (it crosses in front);</item>
    /// <item>its own line, pushed out to the two half widths + 0.3 m from the player and kept on the road, if that
    /// still clears the player by that much;</item>
    /// <item>while its front is at least 2 m short of the player's rear, the lane farthest from the player (it crosses
    /// behind);</item>
    /// <item>otherwise it keeps that line without pushing into the player. While its centre is behind the player's it
    /// waits at the player's speed - 3 m/s until it has dropped 2 m behind, then crosses behind; once its centre is
    /// level or ahead it drives on uncapped until it is 2 m clear, then crosses in front.</item>
    /// </list>
    /// Whatever the line, a car behind the player (its front short of their rear) and still less than the two half
    /// widths + 0.3 m off them sideways drives no faster than the player's speed - 3 m/s, so it never closes on them
    /// while it is in their line.
    /// </summary>
    public static PassingLine PassingLane(AgentCar self, PlayerCar p, double? current = null)
    {
        double selfHalf = self.Stats.Length / 2, playerHalf = p.Stats.Length / 2;
        double keepOff = (self.Stats.Width + p.Stats.Width) / 2 + HoldLineMargin;
        double yield = Math.Max(0, p.Speed - YieldSlower);
        bool behindInLine = self.Distance + selfHalf <= p.Distance - playerHalf
            && Math.Abs(self.Lateral - p.Lateral) < keepOff - Same;
        double cap = behindInLine ? yield : double.PositiveInfinity;

        if (current is double lane && Array.IndexOf(Track.LaneCentres, lane) >= 0 && Math.Abs(lane - p.Lateral) >= PassClearance)
            return new PassingLine(lane, cap);
        int side = SideOf(self, p.Lateral);
        if (ClearLaneOnSide(p.Lateral, side) is double own) return new PassingLine(own, cap);

        if (self.Distance - selfHalf >= p.Distance + playerHalf + CrossGap)
            return new PassingLine(FarthestLane(p.Lateral), cap);

        double line = self.Lateral;
        if ((line - p.Lateral) * side < keepOff) line = p.Lateral + side * keepOff;
        double limit = OnRoadLimit(self);
        line = Math.Clamp(line, -limit, limit);
        if (Math.Abs(line - p.Lateral) >= keepOff - Same) return new PassingLine(line, cap);
        if (self.Distance + selfHalf <= p.Distance - playerHalf - CrossGap)
            return new PassingLine(FarthestLane(p.Lateral), cap);
        // Wait or go by centres, not bumpers: you move before agents decide, so a bumper test is off by your speed · dt.
        bool aheadOfYou = self.Distance >= p.Distance;
        return new PassingLine(line, aheadOfYou ? double.PositiveInfinity : yield);
    }

    /// <summary>The lane centre farthest from the player (5 m or more away).</summary>
    static double FarthestLane(double playerLateral)
    {
        double farthest = Track.LaneCentres[0];
        foreach (double l in Track.LaneCentres)
            if (Math.Abs(l - playerLateral) > Math.Abs(farthest - playerLateral) + Same) farthest = l;
        return farthest;
    }

    /// <summary>Which side of the player the car is on: -1 left, +1 right, its own <see cref="AgentCar.Side"/> when level.</summary>
    public static int SideOf(AgentCar self, double playerLateral)
    {
        double offset = self.Lateral - playerLateral;
        return offset < 0 ? -1 : offset > 0 ? 1 : self.Side;
    }

    /// <summary>The lane centre on <paramref name="side"/> of the player farthest from them and at least <see cref="PassClearance"/> away; null if none.</summary>
    public static double? ClearLaneOnSide(double playerLateral, int side)
    {
        double? best = null;
        foreach (double l in Track.LaneCentres)
        {
            double fromPlayer = l - playerLateral;
            if (Math.Sign(fromPlayer) != side || Math.Abs(fromPlayer) < PassClearance) continue;
            if (best is null || Math.Abs(fromPlayer) > Math.Abs(best.Value - playerLateral)) best = l;
        }
        return best;
    }

    const double BandMargin = 0.3;
    const double ClosingSlack = 0.5;
    const double MinLookAhead = 25;
    const double ClearBehind = 10;
    const double ClearBeyond = 25;
    const double LaneSpacing = 5;

    /// <summary>
    /// Keeps a car from running into the back of a slower car in its lane: it changes to a clear neighbouring lane
    /// (the one nearest its wish), or with none clear, slows to the other car's speed and follows. Like
    /// <see cref="SteerAroundWrecks"/>, a changed answer takes over only after <paramref name="reactionDelay"/> seconds.
    /// The player is never the car ahead: the Blocker and the Armored aim at you on purpose.
    /// </summary>
    public static DriveIntent KeepClearOfCarAhead(AgentCar self, WorldView view, DriveIntent wish,
        CarAheadMemory memory, double reactionDelay, double dt)
    {
        var ahead = FindCarAhead(self, view, wish.TargetLateral, reactionDelay, out double gap);
        return Resolve(self, view, wish, memory, reactionDelay, dt, ahead, gap, 0);
    }

    /// <summary>
    /// Plain lane driving for civilians and police: hold <paramref name="lane"/> at the car's cruise speed, steer round
    /// wrecks, then keep clear of the player when the player is ahead.
    /// </summary>
    internal static DriveIntent DriveInLane(AgentCar self, WorldView view, double lane, AvoidMemory avoid,
        CarAheadMemory playerAhead, double dt)
    {
        double delay = ReactionDelay(self.Kind);
        double lateral = SteerAroundWrecks(self, view, lane, avoid, delay, dt);
        return KeepClearOfPlayerAhead(self, view, new DriveIntent(self.CruiseSpeed, lateral), playerAhead, delay, dt);
    }

    /// <summary>How far under the player's speed a car follows a player it cannot pass, m/s.</summary>
    const double PlayerFollowSlower = 2;

    /// <summary>
    /// <see cref="KeepClearOfCarAhead"/> with the player as the only car ahead, for civilians and police on plain lane
    /// driving. It changes to a clear neighbouring lane, or with none clear follows at the player's speed - 2 m/s (never
    /// below 0, so a stopped player is never reversed into). Keep a separate <paramref name="memory"/> for it: the World's
    /// own <see cref="KeepClearOfCarAhead"/> pass uses <see cref="AgentCar.AheadMemory"/>.
    /// </summary>
    public static DriveIntent KeepClearOfPlayerAhead(AgentCar self, WorldView view, DriveIntent wish,
        CarAheadMemory memory, double reactionDelay, double dt)
    {
        var player = view.Player;
        Vehicle? ahead = null;
        double gap = 0;
        if (player.IsOnRoadDriving && IsAhead(self, player, wish.TargetLateral, reactionDelay, out double g))
        {
            ahead = player;
            gap = g;
        }
        return Resolve(self, view, wish, memory, reactionDelay, dt, ahead, gap, PlayerFollowSlower);
    }

    /// <summary>The shared half of the two rules: reacts to <paramref name="ahead"/> (null: nothing in the way) after the reaction delay.</summary>
    static DriveIntent Resolve(AgentCar self, WorldView view, DriveIntent wish, CarAheadMemory memory,
        double reactionDelay, double dt, Vehicle? ahead, double gap, double followSlower)
    {
        bool active = ahead is not null && Math.Abs(wish.TargetLateral - ahead.Lateral) < Band(self, ahead);
        bool follow = false;
        double lateral = 0;
        if (active)
        {
            double? lane = ClearNeighbourLane(self, view, ahead!, wish.TargetLateral, gap);
            if (lane is double l) lateral = l;
            else follow = true;
        }

        bool unchanged = active == memory.Active
            && (!active || (follow == memory.Follow && (follow || Math.Abs(lateral - memory.Lateral) < Same)));
        if (unchanged)
            memory.Timer = 0;
        else
        {
            memory.Timer += dt;
            if (memory.Timer >= reactionDelay - Same)
            {
                memory.Active = active;
                memory.Follow = follow;
                memory.Lateral = lateral;
                memory.Speed = ahead is null ? 0 : Math.Max(0, ahead.Speed - followSlower);
                memory.Timer = 0;
            }
        }
        // While following, track the leader's current speed, even while a change of answer is pending.
        if (memory.Follow && ahead is not null) memory.Speed = Math.Max(0, ahead.Speed - followSlower);

        if (!memory.Active) return wish;
        return memory.Follow
            ? wish with { TargetSpeed = Math.Min(wish.TargetSpeed, memory.Speed) }
            : wish with { TargetLateral = memory.Lateral };
    }

    /// <summary>How far apart two cars' centres must be sideways to pass without touching, plus a little.</summary>
    static double Band(Vehicle a, Vehicle b) => (a.Stats.Width + b.Stats.Width) / 2 + BandMargin;

    /// <summary>
    /// Enough room to see the car, react and stop at the agents' 10 m/s^2: max(25, 5 + closing * delay + closing^2 / 20) metres.
    /// </summary>
    static double LookAhead(double closing, double reactionDelay) =>
        Math.Max(MinLookAhead, 5 + closing * reactionDelay + closing * closing / 20);

    /// <summary>
    /// The nearest other enemy car whose rear is within the look-ahead of this car's front, that this car is closing on,
    /// and that is in this car's band or in the band of where it wants to be (so a car that has just moved out to pass
    /// still sees the car it is passing, even alongside, until its rear clears that car's front); <paramref name="gap"/> is
    /// the bumper-to-bumper distance to it (negative while alongside).
    /// </summary>
    static AgentCar? FindCarAhead(AgentCar self, WorldView view, double wishLateral, double reactionDelay, out double gap)
    {
        AgentCar? best = null;
        gap = 0;
        foreach (var other in view.Agents)
        {
            if (ReferenceEquals(other, self) || !other.IsOnRoadDriving) continue;
            if (!IsAhead(self, other, wishLateral, reactionDelay, out double g)) continue;
            if (best is null || g < gap)
            {
                best = other;
                gap = g;
            }
        }
        return best;
    }

    /// <summary>
    /// Whether <paramref name="other"/> is a car ahead of <paramref name="self"/> in the sense of <see cref="FindCarAhead"/>
    /// (in its band or its wish's band, closing, and within the look-ahead); <paramref name="gap"/> is the bumper-to-bumper distance.
    /// </summary>
    static bool IsAhead(AgentCar self, Vehicle other, double wishLateral, double reactionDelay, out double gap)
    {
        gap = 0;
        double band = Band(self, other);
        bool inOwnBand = Math.Abs(other.Lateral - self.Lateral) < band;
        if (!inOwnBand && Math.Abs(other.Lateral - wishLateral) >= band) return false;
        double closing = self.Speed - other.Speed;
        if (closing <= -ClosingSlack) return false;
        double lengths = self.Stats.Length + other.Stats.Length;
        double g = other.Distance - self.Distance - lengths / 2;
        // A car this one is passing (out of its own band, in its wish's band) still counts while alongside,
        // until this car's rear has cleared its front, so the passer doesn't steer back into its side.
        double nearest = inOwnBand ? 0 : -lengths;
        if (g < nearest || g > LookAhead(closing, reactionDelay)) return false;
        gap = g;
        return true;
    }

    /// <summary>
    /// The clear lane 5 m either side of the car's nearest lane that is nearest its wish (ties: nearest the car); null if none.
    /// The car's nearest lane itself also counts once it is out of the car ahead's band (the car is already moving out to pass).
    /// </summary>
    static double? ClearNeighbourLane(AgentCar self, WorldView view, Vehicle ahead, double wishLateral, double gapAhead)
    {
        double home = NearestLane(self.Lateral);
        double? best = null;
        foreach (double lane in Track.LaneCentres)
        {
            if (Math.Abs(lane - home) > LaneSpacing + Same) continue;
            if (Math.Abs(lane - ahead.Lateral) < Band(self, ahead)) continue;
            if (!IsLaneClear(self, view, lane, gapAhead)) continue;
            if (best is null || IsBetter(lane, best.Value, wishLateral, self.Lateral)) best = lane;
        }
        return best;
    }

    /// <summary>
    /// No other enemy car in the lane's band from 10 m behind this car to 25 m past the car ahead
    /// (bumper to bumper), and no wreck that <see cref="AvoidWrecks"/> would steer it away from.
    /// </summary>
    static bool IsLaneClear(AgentCar self, WorldView view, double lane, double gapAhead)
    {
        foreach (var other in view.Agents)
        {
            if (ReferenceEquals(other, self) || !other.IsOnRoadDriving) continue;
            if (Math.Abs(other.Lateral - lane) >= Band(self, other)) continue;
            double d = other.Distance - self.Distance, half = (self.Stats.Length + other.Stats.Length) / 2;
            if (d > -(ClearBehind + half) && d < gapAhead + ClearBeyond + half) return false;
        }
        return Math.Abs(AvoidWrecks(self, view, lane) - lane) < Same;
    }

    /// <summary>The lane centre nearest <paramref name="lateral"/>.</summary>
    public static double NearestLane(double lateral)
    {
        double best = Track.LaneCentres[0];
        foreach (double lane in Track.LaneCentres)
            if (Math.Abs(lane - lateral) < Math.Abs(best - lateral)) best = lane;
        return best;
    }
}
