using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Traffic;

/// <summary>Keeps civilian cars on the road around the player: seeds the whole window at the start, then tops it up out of sight.</summary>
public sealed class TrafficDirector(TrafficDefinition def)
{
    const double WindowBehind = 500, WindowAhead = 1500;
    const double SeedFrom = -400, SeedTo = 1450, SeedClear = 60;
    const double SpawnInterval = 0.25;
    const double AheadMin = 1250, AheadMax = 1450;
    const double BehindMin = 400, BehindMax = 450;
    const double SlowPlayerSpeed = 25;

    bool _seeded;
    double _nextSpawn;

    public void Update(World w)
    {
        if (def.CarsPerKm <= 0) return;
        int target = (int)Math.Round(def.CarsPerKm * 2.0, MidpointRounding.AwayFromZero);

        if (!_seeded)
        {
            _seeded = true;
            for (int i = 0; i < target; i++)
            {
                double offset = SpawnSpots.SeedOffset(w, i, target, SeedFrom, SeedTo, SeedClear);
                Place(w, w.Player.Distance + offset, 25 + 10 * w.TrafficRng.NextDouble());
            }
            return;
        }

        if (w.Time < _nextSpawn || Count(w) >= target) return;
        _nextSpawn = w.Time + SpawnInterval;
        bool behind = w.Player.Speed < SlowPlayerSpeed;
        double dist = behind
            ? w.Player.Distance - (BehindMin + (BehindMax - BehindMin) * w.TrafficRng.NextDouble())
            : w.Player.Distance + AheadMin + (AheadMax - AheadMin) * w.TrafficRng.NextDouble();
        double speed = behind ? 30 + 5 * w.TrafficRng.NextDouble() : 25 + 10 * w.TrafficRng.NextDouble();
        Place(w, dist, speed);
    }

    static int Count(World w)
    {
        int n = 0;
        foreach (var a in w.Agents)
        {
            double d = a.Distance - w.Player.Distance;
            if (a.Kind == AgentKind.Civilian && a.State != VehicleState.Wrecked && d >= -WindowBehind && d <= WindowAhead) n++;
        }
        return n;
    }

    static void Place(World w, double distance, double speed)
    {
        if (SpawnSpots.PickFreeLane(w, distance) is not double lane) return;
        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Civilian, distance, lane, speed));
    }
}
