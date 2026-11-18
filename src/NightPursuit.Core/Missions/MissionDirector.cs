using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>Spawns each of a mission's enemy waves once, when the player first passes its distance.</summary>
public sealed class MissionDirector(MissionDefinition def)
{
    const double WaveSpacing = 40;

    const double CrateLeadDistance = 800;

    int _nextWave;
    int _nextCrate;

    public bool Completed { get; private set; }

    public double DistanceRemaining(World w) => Math.Max(0, def.ApproachLength - w.Player.Distance);

    public void Update(World w)
    {
        double at = w.Player.Distance;
        while (_nextWave < def.Waves.Count && at >= def.Waves[_nextWave].AtDistance)
            Spawn(w, def.Waves[_nextWave++]);
        var crates = def.Crates;
        while (crates is not null && _nextCrate < crates.Count && at >= crates[_nextCrate].AtDistance - CrateLeadDistance)
        {
            var c = crates[_nextCrate++];
            w.Hazards.Add(new SupplyCrate(c.Item, c.Amount)
            {
                Distance = c.AtDistance, PrevDistance = c.AtDistance, Lateral = Track.LaneCentres[c.Lane + 1],
            });
        }
        if (at >= def.ApproachLength) Completed = true;
    }

    static void Spawn(World w, WaveDefinition wave)
    {
        int sign = wave.SpawnOffset >= 0 ? 1 : -1;
        for (int i = 0; i < wave.Count; i++)
        {
            double distance = w.Player.Distance + wave.SpawnOffset + i * WaveSpacing * sign;
            double lane = Track.LaneCentres[w.Rng.Next(Track.LaneCentres.Length)];
            double speed = wave.Kind switch
            {
                AgentKind.Runner => 36 + 20 * w.Rng.NextDouble(),
                AgentKind.Blocker => 30 + 8 * w.Rng.NextDouble(),
                _ => w.Player.Speed + 10,
            };
            w.Agents.Add(AgentCar.Create(w.NextId(), wave.Kind, distance, lane, speed));
        }
    }
}
