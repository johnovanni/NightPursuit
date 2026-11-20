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
        foreach (var (role, heat) in new[] { (ChaseRole.Front, 0.0), (ChaseRole.Trail, HeatRules.PoliceHit) })
        {
            var w = PursuitWorld(75);
            w.Player.Speed = 35;
            var car = Chaser(w, role, 0, 0, 30);
            car.CruiseSpeed = 25;                           // above its cruise speed: the slow-police rule does not apply
            car.Distance = w.Player.Distance + (car.Stats.Length + w.Player.Stats.Length) / 2 - 0.5;
            double before = w.Heat.Value;
            Collisions.Resolve(w);
            Assert.True(w.Events.OfType<PlayerDamaged>().Any(), "the cars touched");
            Assert.Equal(before + heat, w.Heat.Value, 9);
        }
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
}
