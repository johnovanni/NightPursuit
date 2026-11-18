using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>One explosion: where it happened on the road (absolute distance), how old it is, and its ray pattern.</summary>
public sealed class Explosion(double distance, double lateral, int seed)
{
    public const double Life = 1.2;
    public double Distance { get; } = distance;
    public double Lateral { get; } = lateral;
    public int Seed { get; } = seed;
    public double Age { get; set; }
}

/// <summary>Presentation-only effects. The World publishes positions and events; the App turns them into explosions.</summary>
public sealed class EffectList
{
    readonly List<Explosion> _explosions = new();
    World? _world;
    double _absorbedTime = double.NaN;
    int _nextSeed = 1000;   // deterministic seeds: the same run always draws the same bursts

    public IReadOnlyList<Explosion> Explosions => _explosions;

    public void Spawn(double distance, double lateral) => _explosions.Add(new Explosion(distance, lateral, _nextSeed++));

    /// <summary>Spawns an explosion for each VehicleDestroyed event of the latest advance (only once per advance).</summary>
    public void Absorb(World w)
    {
        if (!ReferenceEquals(w, _world))
        {
            _world = w;
            _absorbedTime = double.NaN;
            _explosions.Clear();
        }
        if (w.Time == _absorbedTime) return;   // no new steps since the last call: these are the same events
        _absorbedTime = w.Time;

        var destroyed = w.Events.OfType<VehicleDestroyed>().ToList();
        if (destroyed.Count == 0) return;
        // A gunfire kill leaves a Wreck at the car's last position; the newest wrecks belong to the newest kills.
        var wrecks = w.Hazards.OfType<Wreck>().ToList();
        int wreckIndex = wrecks.Count - destroyed.Count(e => e.Cause == KillCause.Gunfire);
        foreach (var e in destroyed)
        {
            if (e.Cause == KillCause.Gunfire)
            {
                if (wreckIndex >= 0 && wreckIndex < wrecks.Count)
                    Spawn(wrecks[wreckIndex].Distance, wrecks[wreckIndex].Lateral);
                wreckIndex++;
            }
            else
            {
                var car = w.Agents.FirstOrDefault(a => a.Id == e.VehicleId);
                if (car is not null) Spawn(car.Distance, car.Lateral);
            }
        }
    }

    public void Update(double seconds)
    {
        foreach (var x in _explosions) x.Age += seconds;
        _explosions.RemoveAll(x => x.Age >= Explosion.Life);
    }
}
