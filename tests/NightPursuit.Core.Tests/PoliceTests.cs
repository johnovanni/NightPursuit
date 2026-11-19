using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class PoliceTests
{
    const double Step = World.Step;

    static MissionRun Run(double presence, double startingHeat, double approach = 200000, BossDefinition? boss = null, int seed = 3) =>
        new(new MissionDefinition("t", "T", "B", new TrackProfile(7), approach, 5, Array.Empty<WaveDefinition>(),
            Boss: boss, Police: new PoliceDefinition(presence, startingHeat)), new FeelSettings(), seed);

    /// <summary>One step with the player held at <paramref name="speed"/>; steered by the autopilot when asked.</summary>
    static void Tick(MissionRun run, double speed, bool autopilot = false)
    {
        var w = run.World;
        w.Player.Speed = speed;
        w.Player.Armor = 100;
        var frame = autopilot
            ? new ControlFrame { Gas = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(w) }
            : ControlFrame.Empty;
        run.Update(frame, Step);
    }

    static List<AgentCar> Police(World w) => w.Agents.Where(a => KindRules.IsPolice(a.Kind)).ToList();

    static int InWindow(World w, AgentKind kind) =>
        w.Agents.Count(a => a.Kind == kind && a.Distance - w.Player.Distance is var d && d >= -500 && d <= 1500);

    [Fact]
    public void No_police_below_patrol_level()
    {
        var run = Run(1, 40);
        for (int i = 0; i < 60 * 120; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(40);
            Tick(run, 50);
        }
        var w = run.World;
        Assert.Equal(HeatLevel.Watched, w.Heat.Level);
        Assert.True(w.Player.Distance > 2500, "the player really drove");
        Assert.Empty(Police(w));
        Assert.DoesNotContain(w.Hazards, h => h is SpeedTrap or Roadblock);
    }

    [Fact]
    public void Patrol_level_keeps_patrol_cars_in_traffic()
    {
        var run = Run(1, 0);
        Tick(run, 50);                       // first update at Calm: nothing is seeded
        Assert.Empty(Police(run.World));
        var seen = new HashSet<int>();
        int spawned = 0;
        for (int i = 0; i < 30 * 120; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(55);
            Tick(run, 50);
            var w = run.World;
            foreach (var c in Police(w))
                if (seen.Add(c.Id))
                {
                    spawned++;
                    double dz = c.Distance - w.Player.Distance;
                    Assert.True(dz >= 1250, $"new patrol car at {dz:0} m");
                    Assert.Equal(AgentKind.PolicePatrol, c.Kind);
                }
        }
        Assert.True(spawned >= 2, $"only {spawned} patrol cars ever appeared");
        Assert.Equal(2, InWindow(run.World, AgentKind.PolicePatrol));
    }

    [Fact]
    public void Patrol_level_places_traps_every_1500_m()
    {
        var run = Run(1, 0);
        Tick(run, 50);
        for (int i = 0; i < 6 * 120; i++) Tick(run, 50);   // drive a while at Calm: no trap
        Assert.DoesNotContain(run.World.Hazards, h => h is SpeedTrap);
        run.World.Heat.Set(55);
        Tick(run, 50);
        double reach = run.World.Player.Distance;
        var traps = run.World.Hazards.OfType<SpeedTrap>().ToList();
        Assert.Single(traps);
        Assert.InRange(traps[0].Distance - reach, 699, 701);   // 700 m ahead on that very step
        var all = new List<SpeedTrap>(traps);
        for (int i = 0; i < 100 * 120; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(55);
            Tick(run, 50);
            foreach (var t in run.World.Hazards.OfType<SpeedTrap>()) if (!all.Contains(t)) all.Add(t);
        }
        all = all.OrderBy(t => t.Distance).ToList();
        Assert.True(all.Count >= 3, $"only {all.Count} traps");
        for (int i = 1; i < all.Count; i++) Assert.Equal(1500, all[i].Distance - all[i - 1].Distance, 6);
        Assert.All(all, t => Assert.Contains(t.LimitMph, new[] { 60.0, 70.0, 80.0 }));
    }

    [Fact]
    public void Entering_patrol_again_restarts_the_first_placements()
    {
        var run = Run(1, 0);
        Tick(run, 50);
        run.World.Heat.Set(55);
        Tick(run, 50);
        Assert.Single(run.World.Hazards.OfType<SpeedTrap>());
        run.World.Heat.Set(0);                       // the level drops to Calm
        for (int i = 0; i < 20 * 120; i++) Tick(run, 50);
        Assert.Equal(HeatLevel.Calm, run.World.Heat.Level);
        run.World.Hazards.RemoveAll(h => h is SpeedTrap or Roadblock);
        run.World.Heat.Set(55);
        Tick(run, 50);
        double reach = run.World.Player.Distance;
        var trap = Assert.Single(run.World.Hazards.OfType<SpeedTrap>());
        var block = Assert.Single(run.World.Hazards.OfType<Roadblock>());
        Assert.InRange(trap.Distance - reach, 699, 701);
        Assert.InRange(block.Distance - reach, 799, 801);
    }

    [Fact]
    public void Patrol_level_places_one_roadblock_at_a_time()
    {
        var run = Run(1, 0, approach: 9000);
        for (int i = 0; i < 120; i++) Tick(run, 50, autopilot: true);
        run.World.Heat.Set(55);
        Tick(run, 50, autopilot: true);
        double reach = run.World.Player.Distance;
        var seen = new List<Roadblock>();
        bool hit = false;
        for (int i = 0; run.World.Player.Distance < reach + 7000 && i < 400 * 120; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(55);
            Tick(run, 50, autopilot: true);
            foreach (var h in run.World.Hazards.OfType<Roadblock>()) if (!seen.Contains(h)) seen.Add(h);
            hit |= run.World.Events.OfType<RoadblockHit>().Any();
            Assert.True(run.World.Hazards.OfType<Roadblock>().Count() <= 1);
        }
        seen = seen.OrderBy(r => r.Distance).ToList();
        Assert.True(seen.Count >= 3, $"only {seen.Count} roadblocks");
        Assert.InRange(seen[0].Distance - reach, 795, 805);      // 800 m ahead of where Patrol was first reached
        for (int i = 1; i < seen.Count; i++) Assert.Equal(3000, seen[i].Distance - seen[i - 1].Distance, 6);
        Assert.False(hit, "the autopilot should take every gap");
    }

    [Fact]
    public void Boss_stage_clears_roadblocks()
    {
        var boss = new BossDefinition("T", 100, [new BossPhaseDefinition(AgentKind.Pursuer, 0)]);
        var run = Run(1, 55, approach: 600, boss: boss);
        var w = run.World;
        Tick(run, 50);
        Assert.Contains(w.Hazards, h => h is Roadblock);          // placed ~800 m ahead, beyond the end of the approach
        int safety = 0;
        while (run.Director.Stage == MissionStage.Approach && safety++ < 30 * 120)
        {
            Assert.Contains(w.Hazards, h => h is Roadblock);      // alive on every step of the approach
            Tick(run, 50);
        }
        Assert.Equal(MissionStage.Boss, run.Director.Stage);
        Assert.DoesNotContain(w.Hazards, h => h is Roadblock);   // gone on the very step the boss appeared
        for (int i = 0; i < 20 * 120; i++)
        {
            if (i % 600 == 0) w.Heat.Set(55);
            Tick(run, 20);
            Assert.DoesNotContain(w.Hazards, h => h is Roadblock);
        }
    }

    [Fact]
    public void Starting_at_patrol_seeds_patrol_cars()
    {
        var run = Run(1, 55);
        Tick(run, 0);
        var cars = Police(run.World);
        Assert.Equal(2, cars.Count);
        Assert.All(cars, c => Assert.Equal(AgentKind.PolicePatrol, c.Kind));
        var ds = cars.Select(c => c.Distance - run.World.Player.Distance).OrderBy(d => d).ToList();
        Assert.True(ds[1] - ds[0] > 800, $"seeded {ds[0]:0} and {ds[1]:0} m");
        Assert.All(ds, d => Assert.InRange(d, -440, 1490));
    }

    [Fact]
    public void Presence_zero_means_no_police_ever()
    {
        var run = Run(0, 100);
        for (int i = 0; i < 60 * 120; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(100);
            Tick(run, 50);
        }
        Assert.Empty(Police(run.World));
        Assert.DoesNotContain(run.World.Hazards, h => h is SpeedTrap or Roadblock);
        Assert.True(run.World.Heat.Level == HeatLevel.Manhunt, "heat really was high");
    }

    [Fact]
    public void Patrol_cars_ignore_you()
    {
        var run = Run(1, 55);
        Tick(run, 30);
        var w = run.World;
        var car = AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, w.Player.Distance, 5, 30);
        w.Agents.Add(car);
        for (int i = 0; i < 10 * 120; i++)
        {
            if (i % 600 == 0) w.Heat.Set(55);
            w.Player.Lateral = 0; w.Player.Heading = 0;
            Tick(run, 30);
            Assert.InRange(car.Lateral, 4.5, 5.5);                       // never moves toward the player's lane
            Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);   // no contact
        }
        Assert.True(Math.Abs(car.Distance - w.Player.Distance) < 15, "it stayed alongside");
        Assert.Equal(VehicleState.Driving, car.State);
    }

    [Fact]
    public void Over_the_cap_only_unseen_patrol_cars_are_removed()
    {
        var run = Run(1, 55);
        Tick(run, 0);
        var w = run.World;
        w.Agents.RemoveAll(a => KindRules.IsPolice(a.Kind));
        // Seven patrol cars in the window: five in view, two far ahead out of sight.
        for (int i = 0; i < 5; i++) w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, w.Player.Distance + 100 + i * 100, Track.LaneCentres[i % 3], 30));
        for (int i = 0; i < 2; i++) w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, w.Player.Distance + 1300 + i * 100, Track.LaneCentres[i % 3], 30));
        Tick(run, 0);
        var left = Police(w);
        Assert.Equal(6, left.Count);
        Assert.Equal(5, left.Count(c => c.Distance - w.Player.Distance < 1200));   // the five in view all stayed
        // Seven all in view: nothing may be removed even above the cap.
        w.Agents.RemoveAll(a => KindRules.IsPolice(a.Kind));
        for (int i = 0; i < 7; i++) w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, w.Player.Distance + 100 + i * 100, Track.LaneCentres[i % 3], 30));
        Tick(run, 0);
        Assert.Equal(7, Police(w).Count);
    }
}
