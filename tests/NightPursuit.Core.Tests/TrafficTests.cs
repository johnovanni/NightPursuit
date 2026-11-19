using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class TrafficTests
{
    const double Step = World.Step;

    static MissionRun Run(double carsPerKm, int seed = 3) =>
        new(new MissionDefinition("t", "T", "B", new TrackProfile(7), 200000, 5, Array.Empty<WaveDefinition>(),
            Traffic: new TrafficDefinition(carsPerKm)), new FeelSettings(), seed);

    /// <summary>One simulation step with the player held at <paramref name="speed"/>.</summary>
    static void Tick(MissionRun run, double speed)
    {
        run.World.Player.Speed = speed;
        run.World.Player.Armor = 100;
        run.Update(ControlFrame.Empty, Step);
    }

    static List<AgentCar> Civilians(World w) => w.Agents.Where(a => a.Kind == AgentKind.Civilian).ToList();

    static int InWindow(World w) =>
        Civilians(w).Count(c => c.Distance - w.Player.Distance is var d && d >= -500 && d <= 1500);

    static void AssertSpaced(World w)
    {
        foreach (var lane in Track.LaneCentres)
        {
            var ds = w.Agents.Where(a => Math.Abs(a.Lateral - lane) < 2.5).Select(a => a.Distance).OrderBy(d => d).ToList();
            for (int i = 1; i < ds.Count; i++)
                Assert.True(ds[i] - ds[i - 1] >= 40 - 1e-9, $"two cars {ds[i] - ds[i - 1]:0.0} m apart in lane {lane}");
        }
    }

    [Fact]
    public void Traffic_starts_spread_out()
    {
        var run = Run(6);
        Tick(run, 0);
        var w = run.World;
        var cars = Civilians(w);
        Assert.Equal(12, cars.Count);
        AssertSpaced(w);
        // The four 500 m quarters of the -500..+1500 window.
        int quarters = Enumerable.Range(0, 4).Count(q =>
            cars.Any(c => c.Distance - w.Player.Distance is var d && d >= -500 + q * 500 && d < q * 500));
        Assert.True(quarters >= 3, $"only {quarters} of 4 quarters hold a car");
        Assert.All(cars, c => Assert.True(Math.Abs(c.Distance - w.Player.Distance) >= 60 - 1e-9));
    }

    [Fact]
    public void Traffic_spawns_out_of_sight_and_spaced()
    {
        var run = Run(6);
        Tick(run, 50);   // the first Update seeds the window (-400..+1450); only later spawns are checked
        var seen = Civilians(run.World).Select(c => c.Id).ToHashSet();
        int spawned = 0;
        for (int i = 0; i < 60 * 120; i++)
        {
            Tick(run, 50);
            var w = run.World;
            foreach (var c in Civilians(w))
                if (seen.Add(c.Id))
                {
                    spawned++;
                    Assert.DoesNotContain(w.Agents, o => o != c && Math.Abs(o.Lateral - c.Lateral) < 2.5 && Math.Abs(o.Distance - c.Distance) < 40 - 1e-9);   // at spawn; cars at different speeds may close up later
                    double dz = c.Distance - w.Player.Distance;
                    Assert.True(dz >= 1250 || dz <= -400, $"new civilian at {dz:0} m");
                }
        }
        Assert.True(spawned >= 5, $"only {spawned} spawned");
    }

    [Fact]
    public void Traffic_density_stays_near_the_target()
    {
        var run = Run(4);   // target 8
        for (int i = 0; i <= 60 * 120; i++)
        {
            Tick(run, 50);
            if (i > 0 && i % (5 * 120) == 0) Assert.InRange(InWindow(run.World), 6, 8);
        }
    }

    [Fact]
    public void Slow_player_gets_traffic_from_behind()
    {
        var run = Run(4);
        Tick(run, 10);
        var seen = Civilians(run.World).Select(c => c.Id).ToHashSet();
        bool behind = false;
        for (int i = 0; i < 40 * 120; i++)
        {
            Tick(run, 10);
            var w = run.World;
            foreach (var c in Civilians(w))
                if (seen.Add(c.Id))
                {
                    Assert.InRange(c.Distance - w.Player.Distance, -450.5, -399);
                    behind = true;
                }
        }
        Assert.True(behind);
    }

    [Fact]
    public void Traffic_never_spawns_on_a_wreck()
    {
        var run = Run(8);
        Tick(run, 50);
        var w = run.World;
        var wreck = new Wreck { Distance = w.Player.Distance + 1300, Lateral = Track.LaneCentres[0], Width = 1.8, Length = 4.5 };
        w.Hazards.Add(wreck);
        var seen = Civilians(w).Select(c => c.Id).ToHashSet();
        int spawned = 0;
        for (int i = 0; i < 60 * 120; i++)
        {
            Tick(run, 50);
            wreck.Distance = w.Player.Distance + 1300;   // keep it where the spawns happen
            foreach (var c in Civilians(w))
                if (seen.Add(c.Id))
                {
                    spawned++;
                    Assert.True(Math.Abs(c.Distance - wreck.Distance) >= 60, $"civilian {c.Distance - wreck.Distance:0.0} m from the wreck");
                }
        }
        Assert.True(spawned > 10, $"only {spawned} spawns");
    }

    [Fact]
    public void Zero_traffic_draws_nothing()
    {
        var run = Run(0);
        var spy = new CountingRng();
        run.World.TrafficRng = spy;
        for (int i = 0; i < 10 * 120; i++) Tick(run, 50);
        Assert.Equal(0, spy.Draws);
        Assert.Empty(Civilians(run.World));
    }

    [Fact]
    public void A_mission_without_a_traffic_section_never_touches_the_traffic_rng()
    {
        var def = new MissionDefinition("t", "T", "B", new TrackProfile(7), 200000, 5, Array.Empty<WaveDefinition>());
        var run = new MissionRun(def, new FeelSettings(), 3);
        var spy = new CountingRng();
        run.World.TrafficRng = spy;
        for (int i = 0; i < 5 * 120; i++) Tick(run, 50);
        Assert.Equal(0, spy.Draws);
    }

    [Fact]
    public void The_traffic_rng_is_seeded_from_the_mission_seed()
    {
        var a = Run(6, seed: 5);
        var b = Run(6, seed: 5);
        var c = Run(6, seed: 6);
        Tick(a, 50); Tick(b, 50); Tick(c, 50);
        Assert.Equal(Civilians(a.World).Select(x => x.Distance), Civilians(b.World).Select(x => x.Distance));
        Assert.NotEqual(Civilians(a.World).Select(x => x.Distance), Civilians(c.World).Select(x => x.Distance));
    }

    sealed class CountingRng : IRng
    {
        public int Draws { get; private set; }
        public double NextDouble() { Draws++; return 0.5; }
        public int Next(int maxExclusive) { Draws++; return 0; }
    }
}
