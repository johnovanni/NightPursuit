using System.Windows;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>One explosion: where it happened on the road (absolute distance), how old it is, and its ray pattern.</summary>
public sealed class Explosion(double distance, double lateral, int seed)
{
    public const double Life = 1.2;
    public double Distance { get; } = distance;
    public double Lateral { get; } = lateral;
    public int Seed { get; } = seed;
    public double Age { get; set; }

    /// <summary>The burst's 18 rays (direction and relative length), worked out once from the seed: the same burst every frame.</summary>
    public (Vector Dir, double Len)[] Rays { get; } = MakeRays(seed);

    static (Vector, double)[] MakeRays(int seed)
    {
        var rng = new Random(seed);
        var rays = new (Vector, double)[18];
        for (int i = 0; i < rays.Length; i++)
        {
            double a = rng.NextDouble() * Math.Tau, len = 0.5 + rng.NextDouble() * 0.5;
            rays[i] = (new Vector(Math.Cos(a), Math.Sin(a) * 0.8), len);
        }
        return rays;
    }
}

/// <summary>A satellite strike's column of light on a kill: where it landed (absolute distance) and how old it is.</summary>
public sealed class Beam(double distance, double lateral)
{
    public const double Life = 0.8;
    public double Distance { get; } = distance;
    public double Lateral { get; } = lateral;
    public double Age { get; set; }
}

/// <summary>Presentation-only effects. The World publishes positions and events; the App turns them into explosions.</summary>
public sealed class EffectList
{
    readonly List<Explosion> _explosions = new();
    readonly List<Beam> _beams = new();
    World? _world;
    double _absorbedTime = double.NaN;
    HashSet<Tracer> _seenTracers = new(), _spareTracers = new();   // swapped each call so no set is allocated per frame
    int _nextSeed = 1000;   // deterministic seeds: the same run always draws the same bursts

    public IReadOnlyList<Explosion> Explosions => _explosions;
    public IReadOnlyList<Beam> Beams => _beams;

    /// <summary>World time of the newest player shot seen (negative infinity before any), for the muzzle flash.</summary>
    public double LastShotTime { get; private set; } = double.NegativeInfinity;
    public bool LastShotLeft { get; private set; }

    /// <summary>The muzzle flash is shown for this many seconds of world time after a shot.</summary>
    public const double MuzzleFlashSeconds = 0.05;

    public void Spawn(double distance, double lateral) => _explosions.Add(new Explosion(distance, lateral, _nextSeed++));

    public void SpawnBeam(double distance, double lateral) => _beams.Add(new Beam(distance, lateral));

    /// <summary>Spawns an explosion for each VehicleDestroyed event of the latest advance (only once per advance).</summary>
    public void Absorb(World w)
    {
        if (!ReferenceEquals(w, _world))
        {
            _world = w;
            _absorbedTime = double.NaN;
            Clear();
        }
        if (w.Time == _absorbedTime) return;   // no new steps since the last call: these are the same events
        _absorbedTime = w.Time;

        // A player tracer we have not seen before is a new shot, however far it flew during a long frame.
        var seen = _spareTracers;
        seen.Clear();
        bool shot = false;
        foreach (var t in w.Tracers)
        {
            if (t.OwnerId != w.Player.Id) continue;
            seen.Add(t);
            if (!_seenTracers.Contains(t)) shot = true;
        }
        _spareTracers = _seenTracers;
        _seenTracers = seen;
        if (shot) MarkShot(w.Time, w.Loadout.Guns.LeftGun);

        if (w.Events.Count == 0) return;   // nothing happened this advance
        var destroyed = w.Events.OfType<VehicleDestroyed>().ToList();
        if (destroyed.Count == 0) return;
        // A kill that leaves a wreck (gunfire, missile, strike) puts it at the car's last position; the newest wrecks belong to the newest kills.
        var wrecks = w.Hazards.OfType<Wreck>().ToList();
        int wreckIndex = wrecks.Count - destroyed.Count(e => e.Cause.LeavesWreck());
        foreach (var e in destroyed)
        {
            double distance, lateral;
            if (e.Cause.LeavesWreck())
            {
                if (wreckIndex >= 0 && wreckIndex < wrecks.Count) (distance, lateral) = (wrecks[wreckIndex].Distance, wrecks[wreckIndex].Lateral);
                else { wreckIndex++; continue; }
                wreckIndex++;
            }
            else
            {
                var car = w.Agents.FirstOrDefault(a => a.Id == e.VehicleId);
                if (car is null) continue;
                (distance, lateral) = (car.Distance, car.Lateral);
            }
            Spawn(distance, lateral);
            if (e.Cause == KillCause.Satellite) SpawnBeam(distance, lateral);
        }
    }

    /// <summary>Records that a player gun fired at world time <paramref name="time"/>.</summary>
    public void MarkShot(double time, bool leftGun)
    {
        LastShotTime = time;
        LastShotLeft = leftGun;
    }

    /// <summary>Forgets every effect (a new run, or a diagnostic scene that places its own).</summary>
    public void Clear()
    {
        _explosions.Clear();
        _beams.Clear();
        _seenTracers.Clear();
        LastShotTime = double.NegativeInfinity;
    }

    public void Update(double seconds)
    {
        foreach (var x in _explosions) x.Age += seconds;
        _explosions.RemoveAll(x => x.Age >= Explosion.Life);
        foreach (var b in _beams) b.Age += seconds;
        _beams.RemoveAll(b => b.Age >= Beam.Life);
    }
}
