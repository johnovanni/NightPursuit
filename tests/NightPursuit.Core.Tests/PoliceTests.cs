using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

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
            // Steered through the roadblock gaps: a roadblock hit (+15) would briefly reach Pursuit, which sends chasers (Task 8).
            Tick(run, 50, autopilot: true);
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
        // 2000 m: the next trap (due 1500 m after the first) is overdue, so re-entry places one at once (Task 8 carry-over).
        for (int i = 0; i < 40 * 120; i++) Tick(run, 50);
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

    // ---- Task 8: Pursuit ----

    /// <summary>A straight, flat world at the given heat (default Pursuit), the player at distance 0 in the centre lane.</summary>
    static World PursuitWorld(double heat = 75)
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Heat.Set(heat);
        return w;
    }

    /// <summary>A police car on <see cref="ChaseDriver"/> in <paramref name="role"/>, <paramref name="dz"/> m from the player.</summary>
    static AgentCar Chaser(World w, ChaseRole role, double dz, double lateral, double speed, int side = 1,
        AgentKind kind = AgentKind.PoliceCruiser)
    {
        var car = AgentCar.Create(w.NextId(), kind, w.Player.Distance + dz, lateral, speed);
        car.Driver = ChaseDriver.Instance;
        car.DriverState = new ChaseDriver.State { Role = role, Side = side };
        w.Agents.Add(car);
        return car;
    }

    /// <summary>One step with the player held at <paramref name="speed"/> in its lane at <paramref name="lateral"/>; then the dispatcher, if any.</summary>
    static void Hold(World w, double speed, double lateral, PoliceDispatcher? dispatcher = null)
    {
        var p = w.Player;
        p.Speed = speed; p.Lateral = lateral; p.Heading = 0; p.PushVel = 0; p.Armor = 100;
        w.StepOnce(ControlFrame.Empty);
        dispatcher?.Update(w, MissionStage.Approach);
    }

    /// <summary>
    /// <see cref="Hold"/>, but the player never drives into a car ahead in its line: within 10 m (bumper to bumper) it
    /// slows to that car's speed, as a player boxed in by a Front car would.
    /// </summary>
    static void HoldCareful(World w, double speed, double lateral, PoliceDispatcher? dispatcher = null)
    {
        var p = w.Player;
        foreach (var a in w.Agents)
        {
            double gap = a.Distance - p.Distance - (a.Stats.Length + p.Stats.Length) / 2;
            if (a.IsOnRoadDriving && Math.Abs(a.Lateral - lateral) < (a.Stats.Width + p.Stats.Width) / 2 + 0.3 && gap > -2 && gap < 10)
                speed = Math.Min(speed, a.Speed);
        }
        Hold(w, speed, lateral, dispatcher);
    }

    static bool IsChasing(AgentCar a) => a.Driver is ChaseDriver;

    [Fact]
    public void Pursuit_sends_cruisers_from_behind()
    {
        var run = Run(1, 75);
        var w = run.World;
        var cruisers = new HashSet<int>();
        bool chased = false;
        for (int i = 0; i < 35 * 120 && !chased; i++)
        {
            if (i % 240 == 0) w.Heat.Set(75);
            w.Player.Lateral = 0; w.Player.Heading = 0;
            Tick(run, 45);
            foreach (var c in w.Agents.Where(a => a.Kind == AgentKind.PoliceCruiser))
                if (cruisers.Add(c.Id))
                {
                    // F10: 150-250 m behind you, at your speed + 15, on the chase.
                    Assert.InRange(c.Distance - w.Player.Distance, -250, -150);
                    Assert.Equal(w.Player.Speed + 15, c.Speed, 9);
                    Assert.True(IsChasing(c));
                }
            chased = Police(w).Count(c => IsChasing(c) && Math.Abs(c.Distance - w.Player.Distance) <= 60) >= 2;
        }
        Assert.Equal(HeatLevel.Pursuit, w.Heat.Level);
        Assert.NotEmpty(cruisers);
        Assert.True(chased, "two chasing police cars never got within 60 m");
        Assert.All(Police(w).Where(c => Math.Abs(c.Distance - w.Player.Distance) <= 600), c => Assert.True(IsChasing(c)));
    }

    [Fact]
    public void Starting_at_pursuit_seeds_the_chasers_at_once()
    {
        var run = Run(1.5, 85);
        Tick(run, 0);
        var w = run.World;
        var chasers = Police(w).Where(IsChasing).ToList();
        Assert.Equal(3, chasers.Count);                      // min(3, round(2 x 1.5)) on the very first step
        Assert.All(chasers.Where(c => c.Kind == AgentKind.PoliceCruiser),
            c => Assert.InRange(c.Distance - w.Player.Distance, -250, -150));
    }

    [Fact]
    public void Front_car_slows_you_but_never_below_8()
    {
        var w = PursuitWorld();
        w.Player.Speed = 20;
        var front = Chaser(w, ChaseRole.Front, 12, 0, 16);   // F22: it starts at your speed - 4
        var gas = new ControlFrame { Gas = true };
        bool contact = false;
        // 5 s: held gas rams it again and again, and about 6 s in it is rammed to death (Task 3's blame ruling).
        for (int i = 0; i < 5 * 120; i++)
        {
            int before = w.Events.Count;
            w.StepOnce(gas);
            Assert.True(front.Speed >= 8 - 1e-6, $"front at {front.Speed:0.00} m/s");
            contact |= w.Events.Skip(before).Any(e => e is PlayerDamaged);
            if (!contact) Assert.True(front.Speed <= w.Player.Speed - 3.5, $"front {front.Speed:0.00}, you {w.Player.Speed:0.00} m/s");
        }
        Assert.True(contact, "you never caught the front car");
        Assert.Equal(VehicleState.Driving, front.State);
        // F9: rear-ending the Front car is never your fault.
        Assert.DoesNotContain(w.Events, e => e is HeatChanged { Amount: > 0 });
    }

    [Fact]
    public void Rear_ending_a_front_car_is_not_your_fault_but_a_trail_car_is()
    {
        RearEnd(ChaseRole.Front, 0);
        RearEnd(ChaseRole.Trail, HeatRules.PoliceHit);
    }

    [Fact]
    public void Rear_ending_a_front_car_that_is_not_boxing_you_is_your_fault()
    {
        // Fix round 1, Minor: only a Front car that is boxing you in right now. Dropped back to Trail near a roadblock, or blinded, it is not.
        RearEnd(ChaseRole.Front, HeatRules.PoliceHit,
            w => w.Hazards.Add(Roadblock.Place(w.Player.Distance + 100, gapLane: 1)));
        RearEnd(ChaseRole.Front, HeatRules.PoliceHit, after: car => car.Blinded = 1);
    }

    /// <summary>
    /// You (35 m/s) rear-end a police car in <paramref name="role"/> that is above its cruise speed, after its driver has
    /// decided once; asserts the heat it costs.
    /// </summary>
    static void RearEnd(ChaseRole role, double heat, Action<World>? setup = null, Action<AgentCar>? after = null)
    {
        var w = PursuitWorld(75);
        setup?.Invoke(w);
        w.Player.Speed = 35;
        var car = Chaser(w, role, 0, 0, 30);
        car.CruiseSpeed = 25;                           // above its cruise speed: the slow-police rule does not apply
        car.Distance = w.Player.Distance + (car.Stats.Length + w.Player.Stats.Length) / 2 - 0.5;
        car.Driver.Decide(car, new WorldView(w), Step);   // its driver acts on this step (as Front, or as Trail near a roadblock)
        after?.Invoke(car);
        double before = w.Heat.Value;
        Collisions.Resolve(w);
        Assert.True(w.Events.OfType<PlayerDamaged>().Any(), "the cars touched");
        Assert.Equal(before + heat, w.Heat.Value, 9);
    }

    [Fact]
    public void Patrol_cars_keep_their_lanes_and_never_hit_you()
    {
        // Fix round 1, Critical (the reviewer's probe): Patrol level, you in the centre lane at 25 m/s, a patrol car 25 m
        // ahead of you, and two overtaking you in the outer lanes. They cover all three lanes near you, but patrol cars are
        // neutral traffic: none is moved out of its lane, so none runs into you.
        var w = PursuitWorld(55);
        var cars = new[] { (25.0, 0.0, 30.0), (-60.0, -5.0, 30.0), (-75.0, 5.0, 31.0) }
            .Select(c => { var a = AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, c.Item1, c.Item2, c.Item3); w.Agents.Add(a); return a; })
            .ToList();
        bool allThree = false;
        for (int i = 0; i < 30 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(55);
            Hold(w, 25, 0);
            // All three near you at once, within the distance where the three-lane rule acts on lane changes.
            allThree |= cars.All(c => Math.Abs(c.Distance - w.Player.Distance) <= PoliceLanes.HoldBeyond(w.Player.Speed));
            Assert.All(cars, c => Assert.InRange(c.Lateral, Track.LaneCentres[0] - 1, Track.LaneCentres[2] + 1));
            Assert.InRange(cars[1].Lateral, -5.5, -4.5);
            Assert.InRange(cars[2].Lateral, 4.5, 5.5);
            Assert.All(cars, c => Assert.Same(PatrolDriver.Instance, c.Driver));
        }
        Assert.True(allThree, "the three patrol cars were never near you at once, so the test proves nothing");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
        Assert.True(cars[1].Distance > w.Player.Distance && cars[2].Distance > w.Player.Distance, "both overtook you");
    }

    [Fact]
    public void Backing_off_is_announced_only_for_a_live_chaser()
    {
        var w = PursuitWorld(60);                     // Patrol: every chaser backs off
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 60));
        var wreck = Chaser(w, ChaseRole.Trail, -30, 0, 0);
        wreck.State = VehicleState.Wrecked;
        wreck.StateTimer = 1.5;
        dispatcher.Update(w, MissionStage.Approach);
        Assert.Same(PatrolDriver.Instance, wreck.Driver);
        Assert.DoesNotContain(w.Events, e => e is Message { Text: "POLICE BACKING OFF" });
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(5)]
    public void Police_never_fill_all_three_lanes_around_you(double playerLane)
    {
        // Review Focus 1. Presence 2 at heat 85, three chasers and two patrol cars placed across all three lanes near you
        // (an illegal start), then 30 s: from 12 s on (F2: 45 m at 4 m/s takes about 11.3 s) never three lanes covered.
        var w = PursuitWorld(85);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(2, 85));
        w.Player.Lateral = playerLane;
        var lanes = Track.LaneCentres;
        Chaser(w, ChaseRole.Trail, -15, lanes[0], 40);
        Chaser(w, ChaseRole.Trail, -25, lanes[1], 40);
        Chaser(w, ChaseRole.Trail, 20, lanes[2], 40);
        foreach (var (dz, lane) in new[] { (35.0, lanes[0]), (-35.0, lanes[2]) })
            w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, dz, lane, 30));
        var view = new WorldView(w);
        Assert.Equal(3, PoliceLanes.CoveredLanes(view, null));   // the illegal start really is illegal

        int first = -1, worst = 0;
        for (int i = 0; i < 30 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(85);
            HoldCareful(w, 40, playerLane, dispatcher);
            w.Hazards.RemoveAll(h => h is Roadblock);   // a player held in its lane cannot take a gap (roadblocks have their own test)
            int covered = PoliceLanes.CoveredLanes(view, null);
            if (covered <= 2 && first < 0) first = i;
            if (i >= 12 * 120) worst = Math.Max(worst, covered);
            if (i >= 12 * 120) Assert.True(covered <= 2, $"three lanes covered at {w.Time:0.00} s");
            if (first >= 0) Assert.True(covered <= 2, $"three lanes covered again at {w.Time:0.00} s");
        }
        Assert.InRange(first, 0, 12 * 120);
        // Not vacuous: the police are still on you, in two lanes.
        Assert.True(Police(w).Count(c => IsChasing(c) && Math.Abs(c.Distance - w.Player.Distance) <= 45) >= 3, "the police left");
        Assert.Equal(2, worst);
        Assert.All(Police(w), c => Assert.Equal(VehicleState.Driving, c.State));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(5)]
    public void Police_never_fill_all_three_lanes_while_you_brake_hard(double playerLane)
    {
        // Fix round 1 guard. Pursuit (presence 2, heat 85, chasers seeded from the start). You cycle: accelerate at 6 m/s²
        // to 60 m/s (or for at most 8 s), brake hard at 15 m/s² to 8 m/s, and go again, never driving into a car ahead.
        // The first hard brake is from 60 m/s with the chasers closing from behind; later ones are from whatever the Front
        // car lets you reach. Every step: never three lanes covered, and no contact at all.
        var w = PursuitWorld(85);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(2, 85));
        var view = new WorldView(w);
        w.Player.Lateral = playerLane;
        w.Player.Speed = 55;
        double v = 55, phase = 0;
        bool braking = false;
        var brakesFrom = new List<double>();
        for (int i = 0; i < 60 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(85);
            v = w.Player.Speed;
            phase += Step;
            if (braking)
            {
                v = Math.Max(8, v - 15 * Step);
                if (v <= 8) { braking = false; phase = 0; }
            }
            else
            {
                v = Math.Min(60, v + 6 * Step);
                if (v >= 60 || phase >= 8) { braking = true; brakesFrom.Add(w.Player.Speed); }
            }
            HoldCareful(w, v, playerLane, dispatcher);
            w.Hazards.RemoveAll(h => h is Roadblock);   // a player held in its lane cannot take a gap
            Assert.True(PoliceLanes.CoveredLanes(view, null) <= 2, $"three lanes covered at {w.Time:0.00} s");
        }
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
        Assert.True(brakesFrom.Count >= 4, $"only {brakesFrom.Count} hard brakes");
        Assert.True(brakesFrom[0] >= 59.9, $"the first hard brake was from {brakesFrom[0]:0.0} m/s");
        Assert.True(Police(w).Count(c => IsChasing(c) && Math.Abs(c.Distance - w.Player.Distance) <= 45) >= 2, "the police left");
    }

    /// <summary>Full stick toward <paramref name="lane"/>, easing off as the car gets there (a straight road).</summary>
    static double SteerTo(World w, double lane)
    {
        var p = w.Player;
        double wantHeading = Math.Clamp(0.12 * (lane - p.Lateral), -0.3, 0.3);
        return Math.Clamp((wantHeading - p.Heading) * 4 / w.Feel.TurnRateAt(p.Speed) + p.Heading * w.Feel.Straighten / w.Feel.TurnRateAt(p.Speed), -1, 1);
    }

    [Fact]
    public void Braking_breaks_a_box_in()
    {
        var w = PursuitWorld();
        var front = Chaser(w, ChaseRole.Front, 12, 0, 36, side: -1);
        var side = Chaser(w, ChaseRole.Side, 0, -5, 40, side: -1);
        for (int i = 0; i < 120; i++) Hold(w, 40, 0);
        // The box is in place: Front ahead of you in your lane, Side beside you on the left.
        Assert.InRange(front.Distance - w.Player.Distance, 5, 25);
        Assert.True(PoliceLanes.Covers(front, 0));
        Assert.True(Math.Abs(side.Distance - w.Player.Distance) < 3);
        Assert.True(PoliceLanes.Covers(side, -5));

        bool free = false;
        for (int i = 0; i < 4 * 120 && !free; i++)
        {
            var input = i < 180
                ? new ControlFrame { PadConnected = true, PadBrake = 1 }
                : new ControlFrame { PadConnected = true, PadGas = 1, PadSteer = SteerTo(w, 5) };
            w.Player.Armor = 100;
            w.StepOnce(input);
            Assert.True(w.Player.Speed > 5 / Units.MphPerMs, "slow enough to be busted");
            var p = w.Player;
            free = DriverHelpers.NearestLane(p.Lateral) == 5
                && !Police(w).Any(c => PoliceLanes.Covers(c, 5) && c.Distance - p.Distance is > 0 and <= 30);
        }
        Assert.True(free, "braking and changing lane never found a lane with no police ahead");
    }

    [Fact]
    public void Trail_stops_behind_a_stopped_player_and_side_rolls_on()
    {
        var w = PursuitWorld();
        w.Player.Speed = 15;
        var trail = Chaser(w, ChaseRole.Trail, -20, 0, 15);
        var side = Chaser(w, ChaseRole.Side, 0, -5, 15, side: -1);
        var stop = new ControlFrame { PadConnected = true, PadBrake = 1 };
        double stoppedAt = -1, sideLeftAt = -1;
        for (int i = 0; i < 12 * 120; i++)
        {
            w.StepOnce(stop);
            Assert.True(side.Speed >= 3 - 1e-6, $"side at {side.Speed:0.00} m/s");
            Assert.True(trail.Speed >= 0);
            if (stoppedAt < 0 && w.Player.Speed == 0) stoppedAt = w.Time;
            if (stoppedAt >= 0 && sideLeftAt < 0 && Math.Abs(side.Distance - w.Player.Distance) >= 3) sideLeftAt = w.Time;
        }
        Assert.True(stoppedAt >= 0, "the player never stopped");
        Assert.True(sideLeftAt >= 0 && sideLeftAt - stoppedAt <= 2, $"side left at {sideLeftAt:0.00} s, you stopped at {stoppedAt:0.00} s");
        Assert.True(side.Distance - w.Player.Distance > 3, "side rolled on past you");
        double bumper = w.Player.Distance - trail.Distance - (trail.Stats.Length + w.Player.Stats.Length) / 2;
        Assert.InRange(bumper, 5.5, 6.5);
        Assert.InRange(trail.Lateral, -0.5, 0.5);
        Assert.Equal(VehicleState.Driving, trail.State);
    }

    [Fact]
    public void No_police_car_stops_in_a_roadblock_gap()
    {
        var w = PursuitWorld();
        var rb = Roadblock.Place(w.Player.Distance + 140, gapLane: 1);   // the gap is your lane, the centre
        w.Hazards.Add(rb);
        var front = Chaser(w, ChaseRole.Front, 12, 0, 26, side: -1);
        var side = Chaser(w, ChaseRole.Side, 0, -5, 30, side: -1);
        bool passed = false;
        for (int i = 0; i < 12 * 120; i++)
        {
            Hold(w, 30, 0);
            var p = w.Player;
            if (!passed && p.Distance >= rb.Distance)
            {
                passed = true;
                Assert.True(front.Distance < p.Distance, $"front {front.Distance - p.Distance:0.0} m from you as you pass the roadblock");
                Assert.True(side.Distance < p.Distance, $"side {side.Distance - p.Distance:0.0} m from you as you pass the roadblock");
            }
            if (!passed)
                Assert.All(new[] { front, side }, c => Assert.True(c.Distance < rb.Distance, "a police car reached the roadblock before you"));
            Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
        }
        Assert.True(passed);
        Assert.All(new[] { front, side }, c =>
        {
            Assert.Equal(VehicleState.Driving, c.State);
            Assert.True(c.Distance > rb.Distance, "it came through the gap behind you");
        });
    }

    [Fact]
    public void Smoke_shakes_the_police()
    {
        var w = PursuitWorld();
        var a = Chaser(w, ChaseRole.Trail, -20, 0, 45);
        var b = Chaser(w, ChaseRole.Side, -20, -5, 45, side: -1);
        while (w.Loadout.SelectedRear != RearWeapon.Smoke) w.Handle(new GameAction(ActionKind.CycleRear));
        w.Handle(new GameAction(ActionKind.DropRear));
        Assert.Contains(w.Hazards, h => h is SmokeCloud);
        for (int i = 0; i < 60; i++) Hold(w, 45, 0);
        Assert.All(new[] { a, b }, c => Assert.True(c.Blinded > 0, "not blinded"));
        for (int i = 0; i < 240; i++) Hold(w, 45, 0);
        Assert.All(new[] { a, b }, c => Assert.True(c.Speed <= 0.6 * c.CruiseSpeed + 1, $"still at {c.Speed:0.0} m/s"));
        Assert.Equal(2, w.Events.OfType<Message>().Count(m => m.Text == "POLICE LOST YOU IN THE SMOKE" && m.Tone == Tone.Good));
        // Blinded police cannot bust you: Task 10 checks it (F17).
    }

    [Fact]
    public void Police_back_off_when_heat_drops()
    {
        var w = PursuitWorld(75);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 75));
        for (int i = 0; i < 10 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(75);
            HoldCareful(w, 45, 0, dispatcher);
        }
        var chasers = Police(w).Where(IsChasing).ToList();
        Assert.NotEmpty(chasers);
        int start = w.Events.Count;
        w.Heat.Set(69);
        HoldCareful(w, 45, 0, dispatcher);
        Assert.All(chasers, c =>
        {
            Assert.Same(PatrolDriver.Instance, c.Driver);
            Assert.Equal(25, c.CruiseSpeed);
        });
        for (int i = 0; i < 6 * 120; i++) HoldCareful(w, 45, 0, dispatcher);
        Assert.All(chasers.Where(c => w.Agents.Contains(c)), c => Assert.True(c.Speed <= 25 + 1e-6, $"still at {c.Speed:0.0} m/s"));
        Assert.DoesNotContain(Police(w), IsChasing);
        var backing = w.Events.Skip(start).OfType<Message>().Where(m => m.Text == "POLICE BACKING OFF").ToList();
        Assert.Equal(Tone.Good, Assert.Single(backing).Tone);
    }

    // ---- Carried from the Task 7 review ----

    [Fact]
    public void A_roadblock_that_waited_is_still_placed_800_m_ahead()
    {
        var run = Run(1, 0, approach: 20000);
        Tick(run, 50, autopilot: true);
        run.World.Heat.Set(55);
        Tick(run, 50, autopilot: true);
        var old = Assert.Single(run.World.Hazards.OfType<Roadblock>());
        // Leave Patrol and come straight back: the restart wants a roadblock now, but the old one is still ahead.
        run.World.Heat.Set(0);
        Tick(run, 50, autopilot: true);
        run.World.Heat.Set(55);
        Tick(run, 50, autopilot: true);
        Assert.Same(old, Assert.Single(run.World.Hazards.OfType<Roadblock>()));
        Roadblock? next = null;
        for (int i = 0; i < 60 * 120 && next is null; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(55);
            Tick(run, 50, autopilot: true);
            next = run.World.Hazards.OfType<Roadblock>().FirstOrDefault(r => r != old);
        }
        Assert.NotNull(next);
        Assert.InRange(next.Distance - run.World.Player.Distance, 799, 801);
    }

    [Fact]
    public void Re_entering_patrol_keeps_traps_1500_m_apart()
    {
        var run = Run(1, 0);
        Tick(run, 50, autopilot: true);
        run.World.Heat.Set(55);
        Tick(run, 50, autopilot: true);
        var first = Assert.Single(run.World.Hazards.OfType<SpeedTrap>());
        run.World.Heat.Set(0);
        for (int i = 0; i < 5 * 120; i++) Tick(run, 50, autopilot: true);    // 250 m at Calm: the next trap is not due yet
        run.World.Heat.Set(55);
        Tick(run, 50, autopilot: true);
        Assert.Same(first, Assert.Single(run.World.Hazards.OfType<SpeedTrap>()));   // no second trap on re-entry
        var all = new List<SpeedTrap> { first };
        for (int i = 0; i < 70 * 120; i++)
        {
            if (i % 240 == 0) run.World.Heat.Set(55);
            Tick(run, 50, autopilot: true);
            foreach (var t in run.World.Hazards.OfType<SpeedTrap>()) if (!all.Contains(t)) all.Add(t);
        }
        all = all.OrderBy(t => t.Distance).ToList();
        Assert.True(all.Count >= 3, $"only {all.Count} traps");
        for (int i = 1; i < all.Count; i++) Assert.Equal(1500, all[i].Distance - all[i - 1].Distance, 6);
    }

    // ---- Task 9: Manhunt ----

    static bool IsInterceptor(AgentCar a) => a.Driver is InterceptorDriver;

    /// <summary>Police that are not wrecked, within the dispatcher's window (500 m behind to 1500 m ahead of you).</summary>
    static int LivePoliceInWindow(World w) =>
        w.Agents.Count(a => KindRules.IsPolice(a.Kind) && a.State != VehicleState.Wrecked
                            && a.Distance - w.Player.Distance is >= -500 and <= 1500);

    /// <summary>The first formed rolling roadblock, or null.</summary>
    static RollingRoadblock? FormedPair(World w) => w.RollingRoadblocks.FirstOrDefault(r => r.Formed);

    /// <summary>Within <paramref name="range"/> (default 45 m, the three-lane rule's range) of you along the road.</summary>
    static bool Near(World w, AgentCar a, double range = PoliceLanes.CountRange) => Math.Abs(a.Distance - w.Player.Distance) <= range;

    [Fact]
    public void Manhunt_brings_interceptors_that_ram()
    {
        // Presence 1, heat 95, you held at 50 m/s in each lane (never steering), TrafficRng seeds 1-6: from the moment the
        // pair forms, 60 s each. An interceptor's sideswipe is attributed by its contact cooldown being set on that very
        // step (F17); it never costs heat. Fix round 1: before the fix the interceptor sat out most of the minute (behind
        // an SUV, or held off), about 1 sideswipe a run; now the 18 runs together get at least 27 (1.5 a minute), and at
        // least 15 of them at least one.
        int total = 0, runsWithRam = 0;
        foreach (double lane in Track.LaneCentres)
            for (int seed = 1; seed <= 6; seed++)
            {
                var w = PursuitWorld(95);
                w.TrafficRng = new SeededRng(seed);
                w.Player.Lateral = lane;
                var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
                var seen = new HashSet<int>();
                double formedAt = -1;
                int rams = 0;
                for (int i = 0; i < 150 * 120 && (formedAt < 0 || w.Time < formedAt + 60); i++)
                {
                    if (i % 240 == 0) w.Heat.Set(95);
                    int before = w.Events.Count;
                    Hold(w, 50, lane, dispatcher);
                    if (formedAt < 0 && FormedPair(w) is not null) formedAt = w.Time;
                    foreach (var c in w.Agents.Where(a => a.Kind == AgentKind.Interceptor))
                        if (seen.Add(c.Id))
                        {
                            Assert.InRange(c.Distance - w.Player.Distance, -250, -150);   // behind, like the cruisers (F10)
                            Assert.True(IsInterceptor(c));
                        }
                    Assert.True(w.Agents.Count(a => IsInterceptor(a) && a.State != VehicleState.Wrecked) <= 1, "min(2, round(1)) is 1 interceptor");
                    var events = w.Events.Skip(before).ToList();
                    bool hit = events.Any(e => e is PlayerDamaged { Reason: "SIDESWIPED" })
                               && w.Agents.Any(a => a.Kind == AgentKind.Interceptor && Math.Abs(a.ContactCooldown - 0.25) < 1e-9);
                    if (!hit) continue;
                    Assert.DoesNotContain(events, e => e is HeatChanged { Amount: > 0 });   // police ramming you gives no heat
                    if (formedAt >= 0) rams++;
                }
                Assert.True(formedAt >= 0, $"lane {lane} seed {seed}: the pair never formed");
                total += rams;
                if (rams > 0) runsWithRam++;
            }
        Assert.True(total >= 27, $"only {total} interceptor sideswipes in 18 minutes after forming");
        Assert.True(runsWithRam >= 15, $"only {runsWithRam} of 18 runs had an interceptor sideswipe");
    }

    /// <summary>TrafficRng seeds 1-6 for each of your three lanes, with the careful and the plain held player.</summary>
    public static TheoryData<int, double, bool> SeedsLanesAndDrivers()
    {
        var data = new TheoryData<int, double, bool>();
        for (int seed = 1; seed <= 6; seed++)
            foreach (double lane in Track.LaneCentres)
                foreach (bool careful in new[] { true, false })
                    data.Add(seed, lane, careful);
        return data;
    }

    /// <summary>The lane a pair formed with you in <paramref name="lane"/> must leave open: the outer lane farthest from yours, or its planned outer lane.</summary>
    static int ExpectedOpen(double lane, RollingRoadblock rb) => lane < 0 ? 2 : lane > 0 ? 0 : rb.PlannedOpen;

    /// <summary>
    /// When each police car was last shoved by a collision (|PushVel| &gt; 0.3) or spinning, and when you last touched a
    /// car: a car is excused for 1.5 s after either, because physics, not its steering, put it where it is (fix round 1:
    /// a window per car, not an instant).
    /// </summary>
    sealed class Shoves(World w)
    {
        readonly Dictionary<int, double> _last = new();
        double _contact = double.NegativeInfinity;

        public void Step(int eventsBefore)
        {
            if (w.Events.Skip(eventsBefore).Any(e => e is PlayerDamaged)) _contact = w.Time;
            foreach (var c in Police(w))
                if (Math.Abs(c.PushVel) > 0.3 || c.State == VehicleState.Spinning) _last[c.Id] = w.Time;
        }

        public bool Excused(AgentCar c) =>
            w.Time - _contact <= 1.5 || (_last.TryGetValue(c.Id, out double t) && w.Time - t <= 1.5);

        /// <summary>Any police car within 45 m of you is excused.</summary>
        public bool AnyNearExcused() => Police(w).Any(c => Near(w, c) && Excused(c));
    }

    [Theory]
    [MemberData(nameof(SeedsLanesAndDrivers))]
    public void Rolling_roadblock_always_leaves_a_lane_open(int seed, double lane, bool careful)
    {
        // Presence 1 at heat 95, you held in your lane at up to 45 m/s (careful: never driving into a car ahead), up to
        // 120 s. Once formed: the gap is the lane the rule says and never changes; the SUVs hold the two other lanes side by
        // side; no police car covers the gap within 60 m of you (fix round 1, Critical: SUVs used to stay in their own gap),
        // except just after a collision (under 1 s in all for the careful player, under 5 s for the plain one, who rams the
        // pair); and Side acts as Trail while the pair is within 80 m.
        var w = PursuitWorld(95);
        w.TrafficRng = new SeededRng(seed);
        w.Player.Lateral = lane;
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
        var shoves = new Shoves(w);
        RollingRoadblock? rb = null;
        int open = -1, excused = 0;
        double formedAt = 0;
        bool pairNear = false;
        for (int i = 0; i < 120 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(95);
            int before = w.Events.Count;
            if (careful) HoldCareful(w, 45, lane, dispatcher); else Hold(w, 45, lane, dispatcher);
            w.Hazards.RemoveAll(h => h is Roadblock);   // a player held in its lane cannot take a parked roadblock's gap
            shoves.Step(before);
            var p = w.Player;
            if (rb is null)
            {
                rb = FormedPair(w);
                if (rb is null) continue;
                open = rb.OpenLane;
                formedAt = w.Time;
                Assert.NotEqual(1, rb.PlannedOpen);                     // drawn when sent: an outer lane
                Assert.Equal(ExpectedOpen(lane, rb), open);
                Assert.All(rb.Cars, c => Assert.True(c.Distance - p.Distance >= 60, "formed before both were 60 m ahead"));
            }
            if (!w.RollingRoadblocks.Contains(rb)) break;              // rammed apart (the plain player); the check ends here
            Assert.Equal(open, rb.OpenLane);                           // locked
            double gap = Track.LaneCentres[open];
            foreach (var c in Police(w).Where(c => c.State == VehicleState.Driving && Near(w, c, 60) && PoliceLanes.Covers(c, gap)))
            {
                Assert.True(shoves.Excused(c), $"{c.Kind} in the gap {c.Distance - p.Distance:0} m from you at {w.Time:0.00} s");
                excused++;
            }
            if (rb.Cars.Any(c => Near(w, c, 80)))
                foreach (var c in Police(w).Where(c => ChaseDriver.RoleOf(c) == ChaseRole.Side && c.Blinded <= 0))
                    Assert.Equal(ChaseRole.Trail, ((ChaseDriver.State)c.DriverState!).ActingAs);
            if (w.Time - formedAt < 4) continue;                       // still moving into their lanes
            if (rb.Cars.Any(shoves.Excused)) continue;
            var lanes = rb.Cars.Select(c => DriverHelpers.NearestLane(c.Lateral)).ToList();
            // Side by side in adjacent lanes. (A plain player rams them, and an SUV may then change lane round a slower
            // car ahead of it, into its partner's lane; never into the gap, which is checked above for every car.)
            if (careful) Assert.Equal(5, Math.Abs(lanes[0] - lanes[1]), 9);
            Assert.DoesNotContain(gap, lanes);
            Assert.All(rb.Cars, c => Assert.False(PoliceLanes.Covers(c, gap)));
            pairNear |= rb.Cars.All(c => c.Distance - p.Distance is > 0 and <= 45);
        }
        Assert.NotNull(rb);
        // A plain player rams the pair again and again, and the shoves can put an SUV in the gap (it is broken up after 2 s).
        double allowed = careful ? 1 : 5;
        Assert.True(excused * Step < allowed, $"police were in the gap after collisions for {excused * Step:0.00} s");
        Assert.True(pairNear, "the formed pair never came within 45 m ahead of you, so the test proves nothing");
    }

    /// <summary>
    /// Manhunt with presence 1 at heat 95 and only the rolling roadblock on the road (every other police car is taken
    /// off each step): you hold <paramref name="speed"/> on your line. Steps until the pair has formed; then
    /// <paramref name="each"/> runs after every step (with the step count since forming) for up to <paramref name="seconds"/>
    /// s, until it returns false.
    /// </summary>
    static RollingRoadblock OnlyTheRollingRoadblock(World w, double speed, double seconds, Func<RollingRoadblock, int, bool> each)
    {
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
        RollingRoadblock? rb = null;
        for (int i = 0, after = 0; i < 120 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(95);
            Hold(w, speed, w.Player.Lateral, dispatcher);
            w.Agents.RemoveAll(a => KindRules.IsPolice(a.Kind) && a.Driver is not RollingRoadblockDriver);
            w.Hazards.RemoveAll(h => h is Roadblock);
            rb ??= FormedPair(w);
            if (rb is null) continue;
            if (++after > seconds * 120 || !each(rb, after)) break;
        }
        Assert.NotNull(rb);
        return rb;
    }

    [Fact]
    public void Rolling_roadblock_slows_to_your_speed_minus_6()
    {
        var w = PursuitWorld(95);
        double within = -1;
        int checkedSteps = 0;
        OnlyTheRollingRoadblock(w, 45, 30, (rb, _) =>
        {
            var p = w.Player;
            var gaps = rb.Cars.Select(c => c.Distance - p.Distance).ToList();
            if (gaps.Min() - (p.Stats.Length + rb.Cars[0].Stats.Length) / 2 < 3) return false;   // about to touch you
            if (gaps.All(g => g is > 0 and <= 40))
            {
                if (within < 0) within = w.Time;
                if (w.Time - within >= 1)                              // F22: from 1 s after you came within 40 m
                {
                    checkedSteps++;
                    Assert.All(rb.Cars, c => Assert.True(c.Speed <= p.Speed - 6 + 0.5, $"SUV at {c.Speed:0.00} m/s, you at {p.Speed:0.00}"));
                }
            }
            return true;
        });
        Assert.True(checkedSteps >= 120, $"only {checkedSteps} steps checked");
    }

    /// <summary>TrafficRng seeds 1-6 for each of your three lanes.</summary>
    public static TheoryData<int, double> SeedsAndLanes()
    {
        var data = new TheoryData<int, double>();
        for (int seed = 1; seed <= 6; seed++)
            foreach (double lane in Track.LaneCentres)
                data.Add(seed, lane);
        return data;
    }

    [Theory]
    [MemberData(nameof(SeedsAndLanes))]
    public void Rolling_roadblock_gap_is_locked_when_it_forms(int seed, double lane)
    {
        // You change lane after the pair forms: into the gap, which makes another lane the one farthest from yours (or,
        // from the middle, takes you out of the middle). The gap and the SUVs' lanes never change, and you get there.
        var w = PursuitWorld(95);
        w.TrafficRng = new SeededRng(seed);
        w.Player.Lateral = lane;
        int open = -1, moves = 0;
        int[] lanes = new int[2];
        bool reached = false;
        OnlyTheRollingRoadblock(w, 40, 15, (rb, after) =>
        {
            var p = w.Player;
            if (after == 1)
            {
                open = rb.OpenLane;
                Assert.Equal(ExpectedOpen(lane, rb), open);
                lanes = new[] { rb.LaneOf(0), rb.LaneOf(1) };
            }
            double target = Track.LaneCentres[open];
            if (Math.Abs(target - p.Lateral) > 1e-9)
            {
                p.Lateral += Math.Clamp(target - p.Lateral, -3 * Step, 3 * Step);   // 3 m/s sideways
                moves++;
            }
            reached |= DriverHelpers.NearestLane(p.Lateral) == target;
            Assert.Equal(open, rb.OpenLane);
            Assert.Equal(lanes[0], rb.LaneOf(0));
            Assert.Equal(lanes[1], rb.LaneOf(1));
            return true;
        });
        Assert.True(moves > 0 && reached, "you never changed lane");
        Assert.NotEqual(1, open);   // an outer gap: from it the farthest lane is the other outer one
    }

    [Fact]
    public void Manhunt_never_exceeds_6_police_nearby()
    {
        var run = Run(2, 100);
        var w = run.World;
        int peak = 0;
        bool pair = false, twoInterceptors = false;
        for (int i = 0; i < 90 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(100);
            Tick(run, 50, autopilot: true);
            int n = LivePoliceInWindow(w);
            Assert.True(n <= 6, $"{n} police in the window at {w.Time:0.00} s");
            peak = Math.Max(peak, n);
            pair |= w.Agents.Count(a => a.Driver is RollingRoadblockDriver) == 2;
            twoInterceptors |= w.Agents.Count(a => IsInterceptor(a) && a.State != VehicleState.Wrecked) == 2;
        }
        Assert.Equal(6, peak);
        Assert.True(pair, "no rolling roadblock pair");
        Assert.True(twoInterceptors, "never two interceptors (min(2, round(2)))");
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(5)]
    public void Police_never_fill_all_three_lanes_in_a_manhunt(double playerLane)
    {
        // Task 8's theory at Manhunt (heat 95, presence 2, everything seeded from the start): cruisers, interceptors and
        // the rolling roadblock, you held in your lane at up to 40 m/s, 60 s. Never three lanes covered by steering.
        var w = PursuitWorld(95);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(2, 95));
        var check = new ManhuntLanes(w);
        w.Player.Lateral = playerLane;
        for (int i = 0; i < 60 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(95);
            int before = w.Events.Count;
            HoldCareful(w, 40, playerLane, dispatcher);
            w.Hazards.RemoveAll(h => h is Roadblock);
            check.Step(before);
        }
        check.AssertMeaningful();
    }

    /// <summary>
    /// The three-lane check for the Manhunt theories. Interceptors ram by design, and a collision shoves a car sideways
    /// whatever its steering says (Task 8's stress check found the same), so a step is excused only while a police car
    /// within 45 m of you was shoved (|PushVel| &gt; 0.3) or spun in the last 1.5 s, or within 1.5 s of a contact with you
    /// (<see cref="Shoves"/>, a window per car); every other step must have at most two lanes covered, and the excused
    /// steps must stay under 3 s of the minute. <see cref="AssertMeaningful"/> also checks the formed pair and an
    /// interceptor really came within 45 m of you.
    /// </summary>
    sealed class ManhuntLanes(World w)
    {
        readonly WorldView _view = new(w);
        readonly Shoves _shoves = new(w);
        int _excused;
        bool _pairNear, _interceptorNear;

        public void Step(int eventsBefore)
        {
            _shoves.Step(eventsBefore);
            _pairNear |= FormedPair(w) is { } rb && rb.Cars.Any(c => Near(w, c));
            _interceptorNear |= w.Agents.Any(a => IsInterceptor(a) && a.State == VehicleState.Driving && Near(w, a));
            if (PoliceLanes.CoveredLanes(_view, null) <= 2) return;
            Assert.True(_shoves.AnyNearExcused(), $"three lanes covered at {w.Time:0.00} s with no collision to explain it");
            _excused++;
        }

        public void AssertMeaningful()
        {
            Assert.True(_excused * World.Step < 3, $"three lanes covered after collisions for {_excused * World.Step:0.00} s");
            Assert.True(_pairNear, "no SUV of a formed pair ever came within 45 m of you");
            Assert.True(_interceptorNear, "no interceptor ever came within 45 m of you");
        }
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(5)]
    public void Police_never_fill_all_three_lanes_in_a_manhunt_while_you_brake_hard(double playerLane)
    {
        // Carried from the Task 8 review (c): the braking variant at Manhunt, with the rolling roadblock and interceptors.
        var w = PursuitWorld(95);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(2, 95));
        var check = new ManhuntLanes(w);
        w.Player.Lateral = playerLane;
        w.Player.Speed = 55;
        double phase = 0;
        bool braking = false;
        int brakes = 0;
        for (int i = 0; i < 60 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(95);
            double v = w.Player.Speed;
            phase += Step;
            if (braking)
            {
                v = Math.Max(8, v - 15 * Step);
                if (v <= 8) { braking = false; phase = 0; }
            }
            else
            {
                v = Math.Min(60, v + 6 * Step);
                if (v >= 60 || phase >= 8) { braking = true; brakes++; }
            }
            int before = w.Events.Count;
            HoldCareful(w, v, playerLane, dispatcher);
            w.Hazards.RemoveAll(h => h is Roadblock);
            check.Step(before);
        }
        Assert.True(brakes >= 4, $"only {brakes} hard brakes");
        check.AssertMeaningful();
    }

    [Fact]
    public void A_broken_rolling_roadblock_is_dropped_and_its_other_suv_trails()
    {
        // Wrecked, blinded, or gone from the road (a respawn clear or a cull, F12): the record goes at once, and the other
        // SUV chases as Trail, so no ghost roadblock is left.
        foreach (var breakIt in new Action<World, AgentCar>[]
                 {
                     (w, car) => w.Destroy(car, KillCause.Rammed),
                     (w, car) => car.Blinded = 1,
                     (w, car) => w.Agents.Remove(car),
                 })
        {
            var w = PursuitWorld(95);
            var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
            Hold(w, 40, 0, dispatcher);
            var rb = Assert.Single(w.RollingRoadblocks);
            var (gone, other) = (rb.Cars[0], rb.Cars[1]);
            breakIt(w, gone);
            Hold(w, 40, 0);
            Assert.DoesNotContain(rb, w.RollingRoadblocks);
            Assert.IsType<ChaseDriver>(other.Driver);
            Assert.Equal(ChaseRole.Trail, ChaseDriver.RoleOf(other));
        }
    }

    [Fact]
    public void A_wreck_in_a_formed_suvs_lane_breaks_the_rolling_roadblock()
    {
        // The pair cannot hold a lane with a wreck in it (it would have to swerve into the gap): it breaks up at once and
        // both SUVs chase as Trail, which steers round the wreck and stops short of it.
        var w = PursuitWorld(95);
        AgentCar[] cars = [];
        var rb = OnlyTheRollingRoadblock(w, 45, 3, (r, _) => { cars = r.Cars.ToArray(); return true; });
        Assert.Contains(rb, w.RollingRoadblocks);
        var lead = rb.Cars[0];
        w.Hazards.Add(new Wreck { Distance = lead.Distance + 60, Lateral = Track.LaneCentres[rb.LaneOf(0)], Width = 1.9, Length = 4.4 });
        Hold(w, 45, w.Player.Lateral);
        Assert.Empty(w.RollingRoadblocks);
        Assert.All(cars, c => Assert.Equal(ChaseRole.Trail, ChaseDriver.RoleOf(c)));
    }

    [Fact]
    public void Manhunt_cars_back_off_below_manhunt()
    {
        var w = PursuitWorld(95);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
        for (int i = 0; i < 5 * 120; i++)
        {
            if (i % 240 == 0) w.Heat.Set(95);
            HoldCareful(w, 40, 0, dispatcher);
        }
        var manhunt = w.Agents.Where(a => a.Kind is AgentKind.Interceptor or AgentKind.ArmoredSuv).ToList();
        Assert.Contains(manhunt, IsInterceptor);
        Assert.Contains(manhunt, a => a.Driver is RollingRoadblockDriver);
        int start = w.Events.Count;
        w.Heat.Set(75);                                            // Pursuit: the cruisers keep chasing
        for (int i = 0; i < 3 * 120; i++) HoldCareful(w, 40, 0, dispatcher);
        Assert.Empty(w.RollingRoadblocks);
        Assert.All(manhunt.Where(w.Agents.Contains), a =>
        {
            Assert.Same(PatrolDriver.Instance, a.Driver);          // they never rejoin the chase at Pursuit
            Assert.Equal(25, a.CruiseSpeed);
        });
        Assert.Contains(Police(w), IsChasing);
        Assert.Single(w.Events.Skip(start).OfType<Message>(), m => m.Text == "POLICE BACKING OFF");
    }

    [Fact]
    public void Allowed_lateral_never_steers_toward_a_blocker()
    {
        // Carried from the Task 8 review (a): its own lane is blocked by a wreck ahead and the other lanes have cars beside
        // it, so there is no lane to offer: it keeps its line (and stops short of the wreck), not the blocked lane's centre.
        var w = PursuitWorld();
        var car = Chaser(w, ChaseRole.Trail, -60, 0.6, 30);
        foreach (double lane in new[] { -5.0, 5.0 })
            w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Civilian, car.Distance, lane, 30));
        w.Hazards.Add(new Wreck { Distance = car.Distance + 50, Lateral = 0, Width = 1.9, Length = 4.4 });
        Assert.Equal(0.6, PoliceLanes.AllowedLateral(new WorldView(w), car), 9);
    }

    [Theory]
    [MemberData(nameof(SeedsAndLanes))]
    public void Manhunt_sends_the_suv_pair_from_behind(int seed, double lane)
    {
        // F10: both 150-250 m behind you at your speed + 15, one behind the other. Fix round 1: the random outer lane is
        // drawn now, and the pair starts in a lane that is neither yours nor the one it would leave open.
        var w = PursuitWorld(95);
        w.TrafficRng = new SeededRng(seed);
        w.Player.Lateral = lane;
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
        Hold(w, 40, lane, dispatcher);
        var rb = Assert.Single(w.RollingRoadblocks);
        Assert.False(rb.Formed);
        Assert.Equal(-1, rb.OpenLane);
        Assert.Contains(rb.PlannedOpen, new[] { 0, 2 });
        Assert.Equal(ExpectedOpen(lane, rb), rb.PredictedOpen(lane));
        double start = rb.Cars[0].Lateral;
        Assert.NotEqual(lane, start);
        Assert.NotEqual(Track.LaneCentres[rb.PredictedOpen(lane)], start);
        Assert.All(rb.Cars, c =>
        {
            Assert.Equal(AgentKind.ArmoredSuv, c.Kind);
            Assert.Equal(82, c.Stats.TopSpeed);                      // F1
            Assert.InRange(c.Distance - w.Player.Distance, -250, -150);
            Assert.Equal(w.Player.Speed + 15, c.Speed, 9);
            Assert.Equal(start, c.Lateral, 9);
            Assert.IsType<RollingRoadblockDriver>(c.Driver);
        });
        Assert.True(rb.Cars[0].Distance > rb.Cars[1].Distance, "slot 0 leads");
    }

    [Fact]
    public void A_new_pair_waits_20_s_after_one_breaks_and_never_makes_more_than_2_suvs()
    {
        // Fix round 1 (Minor 7). One SUV of a pair is cleared away: the record goes and the other chases as Trail. While it
        // is on the road no new pair comes (it would make 3 SUVs); once it has gone too, the next pair waits until 20 s
        // after the break.
        var w = PursuitWorld(95);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
        Hold(w, 40, 0, dispatcher);
        var rb = Assert.Single(w.RollingRoadblocks);
        var survivor = rb.Cars[1];
        w.Agents.Remove(rb.Cars[0]);
        double brokeAt = w.Time;
        double? next = null;
        for (int i = 0; i < 30 * 120 && next is null; i++)
        {
            if (i % 240 == 0) w.Heat.Set(95);
            if (Math.Abs(w.Time - brokeAt - 10) < Step / 2) w.Agents.Remove(survivor);
            Hold(w, 40, 0, dispatcher);
            w.Hazards.RemoveAll(h => h is Roadblock);   // a parked roadblock ahead would hold the next pair back too
            Assert.True(w.Agents.Count(a => a.Kind == AgentKind.ArmoredSuv && a.State != VehicleState.Wrecked) <= 2, "more than 2 SUVs");
            if (w.Agents.Contains(survivor)) Assert.Empty(w.RollingRoadblocks);
            if (w.RollingRoadblocks.Count > 0) next = w.Time;
        }
        Assert.NotNull(next);
        Assert.InRange(next.Value - brokeAt, 20, 20.5);
    }

    [Fact]
    public void Manhunt_makes_room_only_by_removing_unseen_lower_priority_police()
    {
        // F12 and call 14: a full window (6 patrol cars, 2 of them out of sight far ahead). The SUV pair comes first and
        // takes the 2 unseen cars' places; the interceptor then waits, as no car in view is ever removed; once a slot
        // frees, the interceptor gets it before a cruiser does.
        var w = PursuitWorld(95);
        var dispatcher = new PoliceDispatcher(new PoliceDefinition(1, 95));
        var inView = new List<AgentCar>();
        for (int i = 0; i < 4; i++)
        {
            var car = AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, w.Player.Distance + 700 + 120 * i, Track.LaneCentres[i % 3], 30);
            w.Agents.Add(car);
            inView.Add(car);
        }
        for (int i = 0; i < 2; i++)
            w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, w.Player.Distance + 1300 + 100 * i, Track.LaneCentres[i], 30));
        Hold(w, 40, 0, dispatcher);
        Assert.Equal(6, LivePoliceInWindow(w));
        Assert.Single(w.RollingRoadblocks);
        Assert.All(inView, c => Assert.Contains(c, w.Agents));
        Assert.DoesNotContain(w.Agents, IsInterceptor);
        Assert.DoesNotContain(w.Agents, a => a.Kind == AgentKind.PoliceCruiser);
        w.Agents.Remove(inView[3]);
        for (int i = 0; i < 60; i++) Hold(w, 40, 0, dispatcher);
        Assert.Equal(6, LivePoliceInWindow(w));
        Assert.Single(w.Agents, IsInterceptor);
        Assert.DoesNotContain(w.Agents, a => a.Kind == AgentKind.PoliceCruiser);
    }
}
