using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Traffic;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>The approach (waves, up to <see cref="MissionDefinition.ApproachLength"/>), then the boss fight if the mission has a boss.</summary>
public enum MissionStage { Approach, Boss }

/// <summary>
/// Spawns each of a mission's enemy waves once, when the player first passes its distance. At the end of the approach
/// the mission is complete, or, if it has a boss, the boss appears and the mission is complete once it is destroyed.
/// </summary>
public sealed class MissionDirector(MissionDefinition def)
{
    readonly TrafficDirector? _traffic = def.Traffic is { } t ? new TrafficDirector(t) : null;

    readonly PoliceDispatcher? _police = def.Police is { Presence: > 0 } p ? new PoliceDispatcher(p) : null;

    const double WaveSpacing = 40;

    const double CrateLeadDistance = 800;

    /// <summary>How far ahead of the player the boss appears, metres (also where a respawn puts it back).</summary>
    public const double BossAheadDistance = 300;

    int _nextWave;
    int _nextCrate;

    public bool Completed { get; private set; }
    public MissionStage Stage { get; private set; }
    /// <summary>The boss, once it has appeared; it stays here after it is destroyed.</summary>
    public BossCar? Boss { get; private set; }

    /// <summary>Metres left of the approach; 0 once the boss stage has begun.</summary>
    public double DistanceRemaining(World w) =>
        Stage == MissionStage.Boss ? 0 : Math.Max(0, def.ApproachLength - w.Player.Distance);

    public void Update(World w)
    {
        if (Completed) return;
        double at = w.Player.Distance;
        if (Stage == MissionStage.Approach)
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

        if (Stage == MissionStage.Boss)
        {
            if (Boss!.State == VehicleState.Wrecked) Completed = true;
        }
        else if (at >= def.ApproachLength)
        {
            if (def.Boss is null) Completed = true;
            else SpawnBoss(w, def.Boss);
        }

        _traffic?.Update(w);   // last, so traffic and police see this step's boss stage
        // Until you have moved off (World.HeatHeldUntilMoved) the police only place the starting patrol cars, on the first step.
        if (w.HeatHeldUntilMoved) _police?.SeedWhileHeld(w);
        else _police?.Update(w, Stage);
    }

    /// <summary>DIAGNOSTICS ONLY: puts the director in the boss stage with this boss, so the HUD draws the boss bar. The game never calls this.</summary>
    public void AdoptBossForDiagnostics(BossCar boss)
    {
        Boss = boss;
        Stage = MissionStage.Boss;
    }

    /// <summary>
    /// DIAGNOSTICS ONLY: undoes <see cref="AdoptBossForDiagnostics"/>, back to the approach with no boss, so the HUD draws its
    /// approach layout again (no boss bar). The game never calls this.
    /// </summary>
    public void ReleaseBossForDiagnostics()
    {
        Boss = null;
        Stage = MissionStage.Approach;
    }

    /// <summary>The boss appears once, 300 m ahead in the centre lane at your speed - 10 m/s, not yet engaged.</summary>
    void SpawnBoss(World w, BossDefinition bossDef)
    {
        var p = w.Player;
        Boss = BossCar.Create(w.NextId(), bossDef, p.Distance + BossAheadDistance, 0, Math.Max(0, p.Speed - BossCar.ArrivalSlower));
        w.Agents.Add(Boss);
        Stage = MissionStage.Boss;
        w.Publish(new BossAppeared(w.Time, bossDef.Name));
        w.Publish(new Message(w.Time, $"BOSS AHEAD: {bossDef.Name.ToUpperInvariant()}", Tone.Bad));
        w.Announce($"BOSS: {bossDef.Name.ToUpperInvariant()}", "DESTROY IT TO FINISH THE MISSION", 3, urgent: true);
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
                AgentKind.Gunner or AgentKind.Dropper => Math.Max(30, w.Player.Speed),
                AgentKind.Armored => wave.SpawnOffset < 0 ? w.Player.Speed + 6 : 30 + 8 * w.Rng.NextDouble(),
                _ => w.Player.Speed + 10,
            };
            w.Agents.Add(AgentCar.Create(w.NextId(), wave.Kind, distance, lane, speed));
        }
    }
}
