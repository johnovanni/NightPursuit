using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Traffic;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Police;

/// <summary>
/// Puts the police on the road. At Patrol level or above it keeps patrol cars in the traffic and lays speed traps and
/// roadblocks ahead of the player; below Patrol it places nothing (what is already there stays until it is culled).
/// At Pursuit or above (call 13) police cars within 600 m chase you, cruisers come from behind until there are enough
/// chasers, and every step the chasers get their box-in roles; when the level drops below Pursuit they back off.
/// At Manhunt (call 14) interceptors come from behind and a pair of armored SUVs forms a rolling roadblock ahead of you;
/// below Manhunt they back off. The police in the window never exceed 6, filled in priority order: the SUVs, the
/// interceptors, the cruisers, then the patrol cars (F12).
/// Every random draw comes from <see cref="World.TrafficRng"/>, so a mission without police never draws.
/// </summary>
public sealed class PoliceDispatcher(PoliceDefinition def)
{
    const double WindowBehind = 500, WindowAhead = 1500;
    const double SeedFrom = -400, SeedTo = 1450, SeedClear = 60;
    const double AheadMin = 1250, AheadMax = 1450;
    const double SpawnInterval = 0.25;
    /// <summary>No more police than this in the window at once.</summary>
    public const int MaxPolice = 6;
    /// <summary>A surplus patrol car is removed only when the player could not see it: this far ahead or this far behind.</summary>
    const double OutOfSightAhead = 1200, OutOfSightBehind = 300;

    const double TrapAhead = 700, TrapEvery = 1500;
    const double RoadblockAhead = 800, RoadblockEvery = 3000, RoadblockRetry = 500, RoadblockWreckRange = 150;
    const double CarHalfWidth = 1.0;
    static readonly double[] TrapLimits = { 60, 70, 80 };

    /// <summary>At Pursuit, police cars on patrol this close to you (along the road) start chasing, metres.</summary>
    const double ChaseRange = 600;
    /// <summary>At most this many chasers are sent (patrol cars that switch count as chasers too).</summary>
    const int MaxChasers = 3;
    /// <summary>Cruisers appear this far behind you (F10: 150-250 m, not 350-450), at your speed + 15.</summary>
    const double ChaserBehindMin = 150, ChaserBehindMax = 250, ChaserFaster = 15;
    /// <summary>Side must be this far ahead of Front before they swap roles, metres.</summary>
    const double SwapMargin = 10;
    /// <summary>The cruise speed chasers fall back to when they back off, m/s.</summary>
    const double BackOffSpeed = 25;

    /// <summary>At Manhunt, at most this many interceptors (one per presence).</summary>
    const int MaxInterceptors = 2;
    /// <summary>
    /// The second SUV of a pair starts this far behind the first, in the same lane: beyond the 25 m a car keeps clear of
    /// a car ahead at the same speed, so it does not swing out to pass its partner, metres.
    /// </summary>
    const double SuvSpacing = 35;
    /// <summary>A pair forms once both SUVs are at least this far ahead of you, metres.</summary>
    const double FormAhead = 60;
    /// <summary>
    /// No pair is sent while a parked roadblock lies ahead of you or less than this far behind you (the SUVs would meet it
    /// on the way), and no roadblock is parked while a rolling one is on the road: one roadblock at a time, metres.
    /// </summary>
    const double RoadblockClearBehind = 300;

    bool _started;
    bool _atPatrol;
    double _nextSpawn;
    double _nextChaser;
    double _nextInterceptor;
    double _nextPair;

    /// <summary>The player's distance at which the next speed trap is placed (700 m ahead of it).</summary>
    public double NextTrapAt { get; private set; }
    /// <summary>The player's distance at which the next roadblock is placed (800 m ahead of it).</summary>
    public double NextRoadblockAt { get; private set; }

    public void Update(World w, MissionStage stage)
    {
        bool boss = stage == MissionStage.Boss;
        if (boss) w.Hazards.RemoveAll(h => h is Roadblock);   // the same step the boss appears, and for as long as it lasts

        bool pursuit = w.Heat.Level >= HeatLevel.Pursuit;
        bool manhunt = w.Heat.Level >= HeatLevel.Manhunt;
        bool backingOff = false;
        if (!manhunt) backingOff |= BackOffManhunt(w);
        if (!pursuit) backingOff |= BackOff(w);
        if (backingOff) w.Publish(new Message(w.Time, "POLICE BACKING OFF", Tone.Good));
        if (boss) BreakUpRollingRoadblocks(w);   // no roadblock of either kind in a boss fight

        bool patrol = w.Heat.Level >= HeatLevel.Patrol;
        bool entered = patrol && !_atPatrol;
        _atPatrol = patrol;
        bool first = !_started;
        _started = true;
        if (!patrol) return;

        if (entered)
        {
            // A trap not yet due keeps its place, so traps stay 1500 m apart; an overdue one is placed now.
            NextTrapAt = Math.Max(NextTrapAt, w.Player.Distance);
            NextRoadblockAt = w.Player.Distance;
        }

        int target = Math.Min(MaxPolice, (int)Math.Round(def.Presence * 2.0, MidpointRounding.AwayFromZero));
        // In priority order (F12): the Manhunt cars before the patrol cars and cruisers, so they get the room first.
        if (manhunt) SendManhunt(w, all: first, boss);
        if (first) Seed(w, target);
        else TopUp(w, target);
        if (pursuit)
        {
            StartChasing(w);
            SendChasers(w, all: first);   // starting at Pursuit or above, all the chasers are seeded on the first step (F10)
            AssignRoles(w);
        }
        if (manhunt) FormRollingRoadblocks(w);
        RemoveSurplus(w);

        PlaceTrap(w);
        if (!boss) PlaceRoadblock(w);
    }

    void Seed(World w, int target)
    {
        for (int i = 0; i < target && Count(w) < MaxPolice; i++)
        {
            double offset = SpawnSpots.SeedOffset(w, i, target, SeedFrom, SeedTo, SeedClear);
            Place(w, w.Player.Distance + offset);
        }
    }

    void TopUp(World w, int target)
    {
        if (w.Time < _nextSpawn || Count(w) >= target) return;
        _nextSpawn = w.Time + SpawnInterval;
        Place(w, w.Player.Distance + AheadMin + (AheadMax - AheadMin) * w.TrafficRng.NextDouble());
    }

    static void Place(World w, double distance)
    {
        double speed = 28 + 4 * w.TrafficRng.NextDouble();
        if (SpawnSpots.PickFreeLane(w, distance) is not double lane) return;
        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, distance, lane, speed));
    }

    static bool InWindow(World w, AgentCar a)
    {
        double d = a.Distance - w.Player.Distance;
        return KindRules.IsPolice(a.Kind) && a.State != VehicleState.Wrecked && d >= -WindowBehind && d <= WindowAhead;
    }

    static int Count(World w)
    {
        int n = 0;
        foreach (var a in w.Agents) if (InWindow(w, a)) n++;
        return n;
    }

    /// <summary>Over the cap, patrol cars that cannot be seen are removed (never one in view).</summary>
    static void RemoveSurplus(World w) => RemoveUnseenPatrols(w, MaxPolice);

    /// <summary>Removes patrol cars that cannot be seen until at most <paramref name="keep"/> police are in the window; returns how many are left.</summary>
    static int RemoveUnseenPatrols(World w, int keep)
    {
        int count = Count(w);
        for (int i = w.Agents.Count - 1; i >= 0 && count > keep; i--)
        {
            var a = w.Agents[i];
            if (a.Kind != AgentKind.PolicePatrol || !InWindow(w, a)) continue;
            double d = a.Distance - w.Player.Distance;
            if (d <= OutOfSightAhead && d >= -OutOfSightBehind) continue;
            w.Agents.RemoveAt(i);
            count--;
        }
        return count;
    }

    static bool IsChaser(AgentCar a) => a.Driver is ChaseDriver && a.State != VehicleState.Wrecked;

    /// <summary>
    /// Every police car on patrol within 600 m starts chasing you (as Trail); an interceptor or SUV that has backed off
    /// does not (they only take part at Manhunt).
    /// </summary>
    static void StartChasing(World w)
    {
        foreach (var a in w.Agents)
        {
            if (!KindRules.IsPolice(a.Kind) || !ReferenceEquals(a.Driver, PatrolDriver.Instance) || a.State == VehicleState.Wrecked) continue;
            if (IsManhuntKind(a.Kind)) continue;
            if (Math.Abs(a.Distance - w.Player.Distance) > ChaseRange) continue;
            a.Driver = ChaseDriver.Instance;
            a.DriverState = new ChaseDriver.State { Role = ChaseRole.Trail };
        }
    }

    /// <summary>
    /// Sends cruisers from behind until min(3, round(2 x presence)) chasers exist: one every 0.25 s, or all at once when
    /// <paramref name="all"/>. Never above the cap of 6: a cruiser waits until a patrol car out of sight can make room (F12).
    /// </summary>
    void SendChasers(World w, bool all)
    {
        int want = Math.Min(MaxChasers, (int)Math.Round(def.Presence * 2.0, MidpointRounding.AwayFromZero));
        int have = w.Agents.Count(IsChaser);
        if (have >= want || (!all && w.Time < _nextChaser)) return;
        _nextChaser = w.Time + SpawnInterval;
        for (; have < want; have++)
        {
            if (Count(w) >= MaxPolice && RemoveUnseenPatrols(w, MaxPolice - 1) >= MaxPolice) return;
            if (!SpawnChaser(w) || !all) return;
        }
    }

    /// <summary>One cruiser 150-250 m behind you at your speed + 15, in a free lane that leaves a lane free near you (your own lane first).</summary>
    static bool SpawnChaser(World w)
    {
        if (SpawnBehind(w, AgentKind.PoliceCruiser) is not { } car) return false;
        car.Driver = ChaseDriver.Instance;
        car.DriverState = new ChaseDriver.State { Role = ChaseRole.Trail };
        return true;
    }

    /// <summary>
    /// A police car of <paramref name="kind"/> 150-250 m behind you at your speed + 15 (F10), in a free lane that leaves a
    /// lane free near you (your own lane first), added to the world; null when no lane is free.
    /// </summary>
    static AgentCar? SpawnBehind(World w, AgentKind kind)
    {
        var p = w.Player;
        double distance = p.Distance - (ChaserBehindMin + (ChaserBehindMax - ChaserBehindMin) * w.TrafficRng.NextDouble());
        double home = DriverHelpers.NearestLane(p.Lateral);
        var view = new WorldView(w);
        foreach (double lane in Track.LaneCentres.OrderBy(l => Math.Abs(l - home)))
        {
            if (!SpawnSpots.IsFree(w, distance, lane) || !PoliceLanes.LeavesALaneFree(view, lane, VehicleStats.PoliceCruiser.Width)) continue;
            var car = AgentCar.Create(w.NextId(), kind, distance, lane, p.Speed + ChaserFaster);
            w.Agents.Add(car);
            return car;
        }
        return null;
    }

    // ---- Manhunt (call 14) ----

    static bool IsManhuntKind(AgentKind kind) => kind is AgentKind.Interceptor or AgentKind.ArmoredSuv;

    static bool IsInterceptor(AgentCar a) => a.Driver is InterceptorDriver && a.State != VehicleState.Wrecked;

    /// <summary>
    /// The Manhunt cars, highest priority first: the rolling roadblock pair (presence 1 or more; not in a boss fight), then
    /// min(2, round(presence)) interceptors. One spawn every 0.25 s each, or all at once when <paramref name="all"/>.
    /// </summary>
    void SendManhunt(World w, bool all, bool boss)
    {
        if (def.Presence >= 1 && !boss) SendPair(w, all);
        SendInterceptors(w, all);
    }

    /// <summary>
    /// One pair of armored SUVs on <see cref="RollingRoadblockDriver"/> when there is none, both 150-250 m behind you (F10)
    /// at your speed + 15, one 35 m behind the other in the lane farthest from yours. Not while a parked roadblock is near.
    /// </summary>
    void SendPair(World w, bool all)
    {
        if (w.RollingRoadblocks.Count > 0 || (!all && w.Time < _nextPair) || ParkedRoadblockNear(w)) return;
        _nextPair = w.Time + SpawnInterval;
        if (!MakeRoom(w, 2, RankOfSuv)) return;
        var p = w.Player;
        // The lead SUV 150-215 m behind, so the second one, 35 m behind it, is still within 250 m (F10).
        double distance = p.Distance - (ChaserBehindMin + (ChaserBehindMax - ChaserBehindMin - SuvSpacing) * w.TrafficRng.NextDouble());
        double home = DriverHelpers.NearestLane(p.Lateral);
        foreach (double lane in Track.LaneCentres.OrderByDescending(l => Math.Abs(l - home)))
        {
            if (!SpawnSpots.IsFree(w, distance, lane) || !SpawnSpots.IsFree(w, distance - SuvSpacing, lane)) continue;
            double speed = p.Speed + RollingRoadblockDriver.OvertakeFaster;
            var lead = AgentCar.Create(w.NextId(), AgentKind.ArmoredSuv, distance, lane, speed);
            var second = AgentCar.Create(w.NextId(), AgentKind.ArmoredSuv, distance - SuvSpacing, lane, speed);
            var block = new RollingRoadblock(lead, second);
            for (int slot = 0; slot < block.Cars.Count; slot++)
            {
                var car = block.Cars[slot];
                car.Driver = RollingRoadblockDriver.Instance;
                car.DriverState = new RollingRoadblockDriver.State(block, slot);
                w.Agents.Add(car);
            }
            w.RollingRoadblockList.Add(block);
            return;
        }
    }

    /// <summary>Interceptors from behind, like cruisers, until min(2, round(presence)) are on the road; each starts on a random side.</summary>
    void SendInterceptors(World w, bool all)
    {
        int want = Math.Min(MaxInterceptors, (int)Math.Round(def.Presence, MidpointRounding.AwayFromZero));
        int have = w.Agents.Count(IsInterceptor);
        if (have >= want || (!all && w.Time < _nextInterceptor)) return;
        _nextInterceptor = w.Time + SpawnInterval;
        for (; have < want; have++)
        {
            if (!MakeRoom(w, 1, RankOfInterceptor)) return;
            if (SpawnBehind(w, AgentKind.Interceptor) is not { } car) return;
            car.Driver = InterceptorDriver.Instance;
            car.Side = w.TrafficRng.Next(2) == 0 ? -1 : 1;
            if (!all) return;
        }
    }

    const int RankOfSuv = 0, RankOfInterceptor = 1, RankOfChaser = 2, RankOfPatrol = 3;

    /// <summary>Call 14's priority: a rolling-roadblock SUV 0, an interceptor 1, a chaser 2, anything else (on patrol) 3.</summary>
    static int Rank(AgentCar a) => a.Driver switch
    {
        RollingRoadblockDriver => RankOfSuv,
        InterceptorDriver => RankOfInterceptor,
        ChaseDriver => RankOfChaser,
        _ => RankOfPatrol,
    };

    /// <summary>
    /// Room for <paramref name="needed"/> more police within the cap of 6: while there is not, the lowest-priority police
    /// car below <paramref name="rank"/> that you could not see is removed. False when there is still no room (the spawn
    /// waits; F12: never above 6).
    /// </summary>
    static bool MakeRoom(World w, int needed, int rank)
    {
        int count = Count(w);
        while (count + needed > MaxPolice)
        {
            int index = -1;
            for (int i = w.Agents.Count - 1; i >= 0; i--)
            {
                var a = w.Agents[i];
                if (!InWindow(w, a) || Rank(a) <= rank) continue;
                double d = a.Distance - w.Player.Distance;
                if (d <= OutOfSightAhead && d >= -OutOfSightBehind) continue;
                if (index < 0 || Rank(a) > Rank(w.Agents[index])) index = i;
            }
            if (index < 0) return false;
            w.Agents.RemoveAt(index);
            count--;
        }
        return true;
    }

    /// <summary>A parked roadblock ahead of you, or less than 300 m behind you.</summary>
    static bool ParkedRoadblockNear(World w)
    {
        foreach (var h in w.Hazards)
            if (h is Roadblock && h.Distance > w.Player.Distance - RoadblockClearBehind) return true;
        return false;
    }

    /// <summary>
    /// Forms each pair once both SUVs are at least 60 m ahead of you: the open lane is locked to the lane farthest from
    /// yours, or a random outer lane (from <see cref="World.TrafficRng"/>) when you are in the middle (F3).
    /// </summary>
    static void FormRollingRoadblocks(World w)
    {
        var p = w.Player;
        foreach (var rb in w.RollingRoadblocks)
        {
            if (rb.Formed || rb.Cars.Any(c => c.Distance - p.Distance < FormAhead)) continue;
            int yours = Array.IndexOf(Track.LaneCentres, DriverHelpers.NearestLane(p.Lateral));
            int last = Track.LaneCentres.Length - 1;
            int open = yours == 0 ? last : yours == last ? 0 : w.TrafficRng.Next(2) == 0 ? 0 : last;
            rb.Form(open);
        }
    }

    /// <summary>Breaks up every rolling roadblock: its SUVs chase you as Trail (a boss fight has no roadblocks).</summary>
    static void BreakUpRollingRoadblocks(World w)
    {
        foreach (var rb in w.RollingRoadblocks)
            foreach (var car in rb.Cars)
            {
                if (car.State == VehicleState.Wrecked) continue;
                car.Driver = ChaseDriver.Instance;
                car.DriverState = new ChaseDriver.State { Role = ChaseRole.Trail };
            }
        w.RollingRoadblockList.Clear();
    }

    /// <summary>
    /// Below Manhunt the interceptors and SUVs back off as chasers do below Pursuit: patrol driving at 25 m/s, and the
    /// rolling roadblocks are gone. True when at least one of them is not wrecked.
    /// </summary>
    static bool BackOffManhunt(World w)
    {
        w.RollingRoadblockList.Clear();
        bool any = false;
        foreach (var a in w.Agents)
        {
            if (!IsManhuntKind(a.Kind) || ReferenceEquals(a.Driver, PatrolDriver.Instance)) continue;
            any |= BackOffCar(a);
        }
        return any;
    }

    /// <summary>Puts a car back on patrol driving at 25 m/s; true when it is not wrecked.</summary>
    static bool BackOffCar(AgentCar a)
    {
        a.Driver = PatrolDriver.Instance;
        a.DriverState = null;
        a.CruiseSpeed = BackOffSpeed;
        return a.State != VehicleState.Wrecked;
    }

    /// <summary>
    /// The box-in roles, every step. A role stays with its car while it is still chasing. An empty Front role goes to the
    /// chaser nearest you (it may be the Side car), then an empty Side role to the nearest of the rest; a blinded car is
    /// not given a new role. When Side is more than 10 m ahead of Front they swap, so Front never comes up behind Side in
    /// its lane (never when they are level: they would cross each other's path). Everyone else is Trail. All chasers share
    /// one box side: the side of you the Side car was on when it got the role.
    /// </summary>
    static void AssignRoles(World w)
    {
        var p = w.Player;
        AgentCar? front = null, side = null;
        foreach (var a in w.Agents)
        {
            if (!IsChaser(a) || a.DriverState is not ChaseDriver.State s) continue;
            if (s.Role == ChaseRole.Front && front is null) front = a;
            else if (s.Role == ChaseRole.Side && side is null) side = a;
            else s.Role = ChaseRole.Trail;
        }
        if (front is null && Nearest(w, null) is { } f)
        {
            if (ReferenceEquals(f, side)) side = null;
            front = f;
            var fs = StateOf(f);
            fs.Role = ChaseRole.Front;
            fs.Side = DriverHelpers.SideOf(f, p.Lateral);
        }
        if (side is null && Nearest(w, front) is { } n)
        {
            side = n;
            var ss = StateOf(n);
            ss.Role = ChaseRole.Side;
            ss.Side = DriverHelpers.SideOf(n, p.Lateral);
        }
        if (front is not null && side is not null && side.Distance - front.Distance > SwapMargin)
        {
            StateOf(front).Role = ChaseRole.Side;
            StateOf(side).Role = ChaseRole.Front;
            (front, side) = (side, front);
        }
        var box = side ?? front;
        if (box is null) return;
        int boxSide = StateOf(box).Side;
        foreach (var a in w.Agents)
            if (IsChaser(a) && a.DriverState is ChaseDriver.State s) s.Side = boxSide;
    }

    static ChaseDriver.State StateOf(AgentCar a) => (ChaseDriver.State)a.DriverState!;

    /// <summary>The chaser nearest you along the road that is driving and can see, other than <paramref name="except"/>; null if none.</summary>
    static AgentCar? Nearest(World w, AgentCar? except)
    {
        AgentCar? best = null;
        double bestGap = double.MaxValue;
        foreach (var a in w.Agents)
        {
            if (ReferenceEquals(a, except) || !IsChaser(a) || a.State != VehicleState.Driving || a.Blinded > 0) continue;
            if (a.DriverState is not ChaseDriver.State) continue;
            double gap = Math.Abs(a.Distance - w.Player.Distance);
            if (gap < bestGap) { best = a; bestGap = gap; }
        }
        return best;
    }

    /// <summary>
    /// Below Pursuit every chaser goes back to patrol driving at 25 m/s. True when at least one of them is not wrecked
    /// (then one "POLICE BACKING OFF" message goes out).
    /// </summary>
    static bool BackOff(World w)
    {
        bool any = false;
        foreach (var a in w.Agents)
            if (a.Driver is ChaseDriver) any |= BackOffCar(a);
        return any;
    }

    void PlaceTrap(World w)
    {
        if (w.Player.Distance < NextTrapAt) return;
        double distance = NextTrapAt + TrapAhead;
        NextTrapAt += TrapEvery;
        double side = w.TrafficRng.Next(2) == 0 ? -1 : 1;
        double limit = TrapLimits[w.TrafficRng.Next(TrapLimits.Length)];
        w.Hazards.Add(new SpeedTrap { Distance = distance, Lateral = side * SpeedTrap.EdgeLateral, LimitMph = limit });
    }

    void PlaceRoadblock(World w)
    {
        if (w.Player.Distance < NextRoadblockAt) return;
        if (w.Hazards.Any(h => h is Roadblock) || w.RollingRoadblocks.Count > 0)
        {
            // Due, but the last one (parked or rolling) is still there: the schedule moves along with you, so when this
            // one is placed it is still a full 800 m ahead, never nearer.
            NextRoadblockAt = w.Player.Distance;
            return;
        }
        double distance = NextRoadblockAt + RoadblockAhead;
        int first = w.TrafficRng.Next(Track.LaneCentres.Length);
        for (int i = 0; i < Track.LaneCentres.Length; i++)
        {
            int gap = (first + i) % Track.LaneCentres.Length;
            if (WreckNear(w, distance, Track.LaneCentres[gap])) continue;
            w.Hazards.Add(Roadblock.Place(distance, gap));
            NextRoadblockAt += RoadblockEvery;
            return;
        }
        NextRoadblockAt += RoadblockRetry;   // every lane has a wreck near: skip this one
    }

    static bool WreckNear(World w, double distance, double lane)
    {
        foreach (var h in w.Hazards)
            if (h is Wreck wreck && Math.Abs(wreck.Distance - distance) <= RoadblockWreckRange && wreck.Blocks(lane, CarHalfWidth)) return true;
        return false;
    }
}
