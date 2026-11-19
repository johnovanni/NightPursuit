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

    bool _started;
    bool _atPatrol;
    double _nextSpawn;

    /// <summary>The player's distance at which the next speed trap is placed (700 m ahead of it).</summary>
    public double NextTrapAt { get; private set; }
    /// <summary>The player's distance at which the next roadblock is placed (800 m ahead of it).</summary>
    public double NextRoadblockAt { get; private set; }

    public void Update(World w, MissionStage stage)
    {
        bool boss = stage == MissionStage.Boss;
        if (boss) w.Hazards.RemoveAll(h => h is Roadblock);   // the same step the boss appears, and for as long as it lasts

        bool patrol = w.Heat.Level >= HeatLevel.Patrol;
        bool entered = patrol && !_atPatrol;
        _atPatrol = patrol;
        bool first = !_started;
        _started = true;
        if (!patrol) return;

        if (entered) { NextTrapAt = w.Player.Distance; NextRoadblockAt = w.Player.Distance; }

        int target = Math.Min(MaxPolice, (int)Math.Round(def.Presence * 2.0, MidpointRounding.AwayFromZero));
        if (first) Seed(w, target);
        else TopUp(w, target);
        RemoveSurplus(w);

        PlaceTrap(w);
        if (!boss) PlaceRoadblock(w);
    }

    void Seed(World w, int target)
    {
        for (int i = 0; i < target; i++)
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
    static void RemoveSurplus(World w)
    {
        int count = Count(w);
        for (int i = w.Agents.Count - 1; i >= 0 && count > MaxPolice; i--)
        {
            var a = w.Agents[i];
            if (a.Kind != AgentKind.PolicePatrol || !InWindow(w, a)) continue;
            double d = a.Distance - w.Player.Distance;
            if (d <= OutOfSightAhead && d >= -OutOfSightBehind) continue;
            w.Agents.RemoveAt(i);
            count--;
        }
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
        if (w.Player.Distance < NextRoadblockAt || w.Hazards.Any(h => h is Roadblock)) return;
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
