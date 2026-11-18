using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class DriverTests
{
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static Wreck AddWreck(World w, double distance, double lateral) =>
        AddHazard(w, new Wreck { Distance = distance, Lateral = lateral, Width = 1.9, Length = 4.4 });

    static Wreck AddHazard(World w, Wreck wreck) { w.Hazards.Add(wreck); return wreck; }

    [Fact]
    public void Blocker_moves_into_player_lane()
    {
        var w = Straight();
        w.Player.Speed = 25; w.Player.Lateral = 5;
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, 80, -5, 25);
        w.Agents.Add(blocker);
        for (int i = 0; i < 120 * 4; i++) { w.Player.Speed = 25; w.StepOnce(ControlFrame.Empty); }
        Assert.True(Math.Abs(blocker.Lateral - 5) < 0.6, $"lateral {blocker.Lateral}");
    }

    [Fact]
    public void Blocker_brake_checks_inside_30_m()
    {
        var w = Straight();
        w.Player.Speed = 30;
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, 20, 0, 40);
        w.Agents.Add(blocker);
        for (int i = 0; i < 120 * 3; i++) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }
        Assert.True(blocker.Speed < 30, $"speed {blocker.Speed}");
    }

    [Fact]
    public void Pursuer_cycles_through_phases()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -200, 0, 40);
        w.Agents.Add(car);
        PursuerPhase? Phase() => (car.DriverState as PursuerDriver.State)?.Phase;

        double attackAt = -1, retreatAt = -1;
        for (int i = 0; i < 120 * 40 && retreatAt < 0; i++)
        {
            w.Player.Speed = 30;
            w.StepOnce(ControlFrame.Empty);
            if (attackAt < 0 && Phase() == PursuerPhase.Attack) attackAt = w.Time;
            if (attackAt >= 0 && Phase() == PursuerPhase.Retreat) retreatAt = w.Time;
        }
        Assert.InRange(attackAt, 0.1, 30);
        Assert.True(retreatAt > attackAt, "never retreated after attacking");
    }

    [Fact]
    public void Pursuer_retreats_after_a_sideswipe()
    {
        var w = Straight();
        w.Player.Speed = 30;
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -3, 1.8, 30);
        w.Agents.Add(car);
        car.DriverState = new PursuerDriver.State { Phase = PursuerPhase.Attack, Timer = 3.5, Target = 1.8 };
        car.ContactCooldown = 0.2;
        w.Player.Speed = 30;
        w.StepOnce(ControlFrame.Empty);
        Assert.Equal(PursuerPhase.Retreat, ((PursuerDriver.State)car.DriverState!).Phase);
    }

    [Fact]
    public void Retreating_pursuer_drops_back_from_a_slow_player_without_ramming_it()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -5, 0, 5);
        car.DriverState = new PursuerDriver.State { Phase = PursuerPhase.Retreat, Target = 0 };
        w.Agents.Add(car);
        double deepest = 0;
        for (int i = 0; i < 120 * 9; i++)
        {
            w.Player.Speed = 5;
            w.StepOnce(ControlFrame.Empty);
            deepest = Math.Min(deepest, car.Distance - w.Player.Distance);
            Assert.Equal(100, w.Player.Armor);                    // never touched
        }
        Assert.True(deepest < -20, $"only fell back to {deepest:0.0} m");
    }

    [Fact]
    public void Retreating_pursuer_always_falls_back_to_80_m_without_creeping_up()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -5, 0, 5);
        car.DriverState = new PursuerDriver.State { Phase = PursuerPhase.Retreat, Target = 0 };
        w.Agents.Add(car);
        double prevDz = car.Distance - w.Player.Distance;
        for (int i = 0; i < 120 * 40; i++)
        {
            w.Player.Speed = 5;
            w.StepOnce(ControlFrame.Empty);
            double dz = car.Distance - w.Player.Distance;
            Assert.True(dz - prevDz <= 0.01, $"crept up {dz - prevDz:0.000} m at step {i}");
            Assert.Equal(100, w.Player.Armor);
            prevDz = dz;
            if (dz <= -80) return;
        }
        Assert.Fail($"only reached dz {prevDz:0.0} in 40 s");
    }

    static bool Overlaps(Vehicle a, Vehicle b) =>
        Math.Abs(a.Lateral - b.Lateral) < (a.Stats.Width + b.Stats.Width) / 2 &&
        Math.Abs(a.Distance - b.Distance) < (a.Stats.Length + b.Stats.Length) / 2;

    [Fact]
    public void Retreating_pursuer_at_the_road_edge_holds_its_line_instead_of_crossing_the_player()
    {
        var w = Straight();
        w.Player.Lateral = 5;
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, 0, 6, 5);
        car.DriverState = new PursuerDriver.State { Phase = PursuerPhase.Retreat, Target = 6 };
        w.Agents.Add(car);
        for (int i = 0; i < 120 * 12; i++)
        {
            w.Player.Speed = 5;
            w.StepOnce(ControlFrame.Empty);
            Assert.False(Overlaps(w.Player, car), $"overlapped the player at step {i}");
            if (car.Distance - w.Player.Distance > -20)
                Assert.True(car.Lateral >= w.Player.Lateral, $"crossed to the player's left at step {i}: {car.Lateral:0.00}");
        }
    }

    [Fact]
    public void Retreating_pursuer_alongside_never_steers_back_into_the_players_lane()
    {
        var w = Straight();
        w.Player.Lateral = 0;
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, 0, -2.2, 5);
        car.DriverState = new PursuerDriver.State { Phase = PursuerPhase.Retreat, Target = -2.2 };
        w.Agents.Add(car);
        for (int i = 0; i < 120 * 4; i++)
        {
            w.Player.Speed = 5;
            w.StepOnce(ControlFrame.Empty);
            bool overlap = Overlaps(w.Player, car);
            Assert.False(overlap, $"overlapped the player at step {i}");
        }
        Assert.True(Math.Abs(car.Lateral - w.Player.Lateral) >= 2.5, $"ended {car.Lateral - w.Player.Lateral:0.00} m from the player");
    }

    [Fact]
    public void Destroying_a_car_ahead_of_a_pursuer_blocks_it()
    {
        var w = Straight();
        w.Player.Distance = 400; w.Player.Speed = 30;           // far ahead, so the pursuer just approaches
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, 0, 2.7, 30);
        car.Side = 1;
        w.Agents.Add(car);
        var victim = AgentCar.Create(w.NextId(), AgentKind.Runner, 90, 2.7, 0);   // a car shot dead 90 m ahead of the pursuer
        w.Agents.Add(victim);
        w.Destroy(victim, KillCause.Gunfire);
        Assert.Single(w.Hazards);

        int stepsRun = 0;
        double before = car.Lateral;
        while (stepsRun < 120 * 0.2) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); stepsRun++; }
        Assert.True(Math.Abs(car.Lateral - before) < 0.05, "reacted before its 0.3 s delay");

        while (stepsRun < 120 * 2.5)
        {
            w.Player.Speed = 30;
            w.StepOnce(ControlFrame.Empty);
            stepsRun++;
            Assert.Equal(VehicleState.Driving, car.State);
        }
        Assert.True(car.Lateral > 4.6, $"lateral {car.Lateral}");   // clear of the wreck's edge
    }

    [Fact]
    public void Avoid_wrecks_picks_nearest_clear_lane()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 0, 30);
        AddWreck(w, 60, 0);
        AddWreck(w, 70, 5);
        var view = new WorldView(w);
        Assert.Equal(-5, DriverHelpers.AvoidWrecks(car, view, 0));
    }

    [Fact]
    public void Avoid_wrecks_ignores_far_and_passed_wrecks_and_keeps_free_desire()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 100, 0, 30);
        var view = new WorldView(w);
        AddWreck(w, 50, 0);                                     // behind the car
        AddWreck(w, 100 + 130, 0);                              // beyond the 120 m look-ahead
        Assert.Equal(0, DriverHelpers.AvoidWrecks(car, view, 0));
        AddWreck(w, 100 + 40, 0);
        Assert.Equal(5, DriverHelpers.AvoidWrecks(car, view, 1.2));      // 1.2 m is still inside the wreck's reach
    }

    [Fact]
    public void Avoid_wrecks_tie_goes_to_lane_nearest_the_car_and_no_clear_lane_keeps_desire()
    {
        var w = Straight();
        var view = new WorldView(w);
        AddWreck(w, 60, 0);
        var left = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, -3, 30);
        var right = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 3, 30);
        Assert.Equal(-5, DriverHelpers.AvoidWrecks(left, view, 0));
        Assert.Equal(5, DriverHelpers.AvoidWrecks(right, view, 0));

        AddWreck(w, 60, -5);
        AddWreck(w, 60, 5);
        Assert.Equal(0, DriverHelpers.AvoidWrecks(left, view, 0));
    }

    [Fact]
    public void Runner_changes_lane_at_once_when_hit_and_only_once_per_hit()
    {
        var w = Straight();
        w.Player.Distance = 300;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 0, 30);
        w.Agents.Add(car);
        w.StepOnce(ControlFrame.Empty);
        var state = (RunnerDriver.State)car.DriverState!;
        state.LaneTimer = 100;                                  // no scheduled change for a long while
        state.Target = 0;
        double[] lanes = [-5, 0, 5];
        int changes = 0;
        for (int hit = 0; hit < 6; hit++)
        {
            double before = state.Target;
            car.HitFlash = 0.08;                                // a tracer hit; it decays over ~10 steps
            for (int i = 0; i < 20; i++) w.StepOnce(ControlFrame.Empty);
            Assert.Contains(state.Target, lanes);
            if (state.Target != before) changes++;
            Assert.True(state.LaneTimer > 2 && state.LaneTimer < 7, "lane timer was reset by the hit");
            state.LaneTimer = 100;
        }
        Assert.InRange(changes, 1, 6);
    }

    [Fact]
    public void Runner_avoids_wreck_in_its_lane_after_reaction_delay()
    {
        var w = Straight();
        w.Player.Distance = -300;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 0, 30);
        w.Agents.Add(car);
        w.StepOnce(ControlFrame.Empty);
        ((RunnerDriver.State)car.DriverState!).LaneTimer = 1000;
        AddWreck(w, 100, 0);
        for (int i = 0; i < 120 * 3; i++) w.StepOnce(ControlFrame.Empty);
        Assert.True(Math.Abs(car.Lateral) > 3.9, $"lateral {car.Lateral}");
        Assert.Equal(VehicleState.Driving, car.State);
    }

    [Fact]
    public void Cars_get_their_default_driver_and_cruise_speed()
    {
        Assert.IsType<RunnerDriver>(AgentCar.Create(1, AgentKind.Runner, 0, 0, 33).Driver);
        Assert.IsType<BlockerDriver>(AgentCar.Create(1, AgentKind.Blocker, 0, 0, 33).Driver);
        Assert.IsType<PursuerDriver>(AgentCar.Create(1, AgentKind.Pursuer, 0, 0, 33).Driver);
        Assert.Equal(33, AgentCar.Create(1, AgentKind.Runner, 0, 0, 33).CruiseSpeed);
    }

    // Keeping clear of the car ahead. The player is stopped at 0 on a straight and every car is 200 m or more ahead,
    // so the Blocker is beyond its 140 m reach and just cruises.

    static (World W, AgentCar Blocker, AgentCar Runner) FastRunnerBehindSlowBlocker()
    {
        var w = Straight();
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, 260, 0, 25);
        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, 200, 0, 50);
        runner.DriverState = new RunnerDriver.State { Target = 0, LaneTimer = 100 };    // it wants to stay in lane 0
        w.Agents.Add(blocker);
        w.Agents.Add(runner);
        return (w, blocker, runner);
    }

    /// <summary>Bumper to bumper: from the back car's front to the front car's rear, metres.</summary>
    static double Gap(Vehicle back, Vehicle front) => front.Distance - back.Distance - (back.Stats.Length + front.Stats.Length) / 2;

    static void AssertNoAgentContact(World w, IEnumerable<AgentCar> cars, int step)
    {
        foreach (var car in cars)
        {
            Assert.True(car.Armor == car.Stats.Armor, $"car {car.Id} lost armor at step {step}");
            Assert.True(car.AgentContactCooldown == 0, $"car {car.Id} touched another car at step {step}");
        }
        Assert.DoesNotContain(w.Events, e => e is VehicleSpun);   // StepOnce keeps every event of the run
    }

    [Fact]
    public void Fast_runner_behind_a_slow_blocker_never_rear_ends_it()
    {
        var (w, blocker, runner) = FastRunnerBehindSlowBlocker();
        for (int i = 0; i < 120 * 20; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            AssertNoAgentContact(w, [blocker, runner], i);
        }
        bool ahead = Gap(blocker, runner) >= 0;
        Assert.True(ahead || Gap(runner, blocker) >= 0, $"overlapping at the end: gap {Gap(runner, blocker):0.00}");
    }

    [Theory]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(30)]
    public void Slowly_passing_runner_stays_out_until_clear_of_the_blocker(double runnerSpeed)
    {
        var (w, blocker, runner) = FastRunnerBehindSlowBlocker();
        runner.Speed = runner.CruiseSpeed = runnerSpeed;            // closing at 2-5 m/s (slower passes run past the 1500 m cull ahead of the stopped player)
        double passedAt = -1;
        for (int i = 0; i < 120 * 120 && (passedAt < 0 || w.Time < passedAt + 3); i++)
        {
            w.StepOnce(ControlFrame.Empty);
            AssertNoAgentContact(w, [blocker, runner], i);
            if (passedAt < 0 && Gap(blocker, runner) >= 0) passedAt = w.Time;
        }
        Assert.True(passedAt >= 0, "never passed the blocker in 120 s");
        Assert.Contains(blocker, w.Agents);                       // not culled 1500 m ahead, so the pass really ran
        Assert.Contains(runner, w.Agents);
    }

    [Fact]
    public void Boxed_in_car_follows_at_the_leaders_speed()
    {
        var (w, blocker, runner) = FastRunnerBehindSlowBlocker();
        var left = AgentCar.Create(w.NextId(), AgentKind.Runner, 260, -5, 25);
        var right = AgentCar.Create(w.NextId(), AgentKind.Runner, 260, 5, 25);
        left.Driver = right.Driver = new StillDriver();
        w.Agents.Add(left);
        w.Agents.Add(right);
        for (int i = 0; i < 120 * 10; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            AssertNoAgentContact(w, [blocker, runner, left, right], i);
        }
        Assert.InRange(runner.Speed, 25 - 0.5, 25 + 0.5);
        Assert.InRange(runner.Lateral, -0.3, 0.3);
        Assert.True(Gap(runner, blocker) > 0, $"gap {Gap(runner, blocker):0.00}");
    }

    [Fact]
    public void Runner_changes_to_a_clear_lane_after_its_reaction_delay()
    {
        var (w, blocker, runner) = FastRunnerBehindSlowBlocker();
        double withinAt = -1, leftLaneAt = -1, passedAt = -1;
        for (int i = 0; i < 120 * 20 && passedAt < 0; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            double closing = runner.Speed - blocker.Speed;
            double lookAhead = Math.Max(25, 5 + closing * DriverHelpers.ReactionDelay(AgentKind.Runner) + closing * closing / 20);
            if (withinAt < 0 && Gap(runner, blocker) <= lookAhead) withinAt = w.Time;
            var m = runner.AheadMemory;
            if (leftLaneAt < 0 && m.Active && !m.Follow && m.Lateral != 0) leftLaneAt = w.Time;   // its TargetLateral leaves 0
            if (Gap(blocker, runner) >= 0) passedAt = w.Time;
        }
        Assert.True(withinAt >= 0, "the blocker never came within the look-ahead");
        Assert.True(leftLaneAt >= 0, "never changed lane");
        Assert.True(leftLaneAt - withinAt >= 0.5 - 1e-9, $"changed lane {leftLaneAt - withinAt:0.000} s after the blocker came into range");
        Assert.True(passedAt >= 0, "never passed the blocker in 20 s");
    }

    [Fact]
    public void Blinded_car_does_not_keep_clear()
    {
        var (w, blocker, runner) = FastRunnerBehindSlowBlocker();
        bool reached = false;
        for (int i = 0; i < 120 * 20 && !reached; i++)
        {
            runner.Blinded = 2;
            w.StepOnce(ControlFrame.Empty);
            Assert.False(runner.AheadMemory.Active, $"kept clear while blinded at step {i}");
            reached = Gap(runner, blocker) <= 0;
        }
        Assert.True(reached, "never reached the blocker");
    }

    [Fact]
    public void Reaction_delays_are_the_drivers_wreck_delays()
    {
        Assert.Equal(0.5, DriverHelpers.ReactionDelay(AgentKind.Runner));
        Assert.Equal(0.8, DriverHelpers.ReactionDelay(AgentKind.Blocker));
        Assert.Equal(0.3, DriverHelpers.ReactionDelay(AgentKind.Pursuer));
    }

    [Fact]
    public void Wrecked_agents_are_removed_after_their_timer_and_far_agents_are_culled()
    {
        var w = Straight();
        var wrecked = AgentCar.Create(w.NextId(), AgentKind.Runner, 50, 0, 0);
        var farAhead = AgentCar.Create(w.NextId(), AgentKind.Runner, 1600, 0, 0);
        var farBehind = AgentCar.Create(w.NextId(), AgentKind.Runner, -600, 0, 0);
        var near = AgentCar.Create(w.NextId(), AgentKind.Runner, 1400, 0, 0);
        w.Agents.AddRange([wrecked, farAhead, farBehind, near]);
        w.Destroy(wrecked, KillCause.Rammed);
        w.StepOnce(ControlFrame.Empty);
        Assert.Equal(new[] { wrecked, near }, w.Agents.ToArray());               // far ones gone, wreck still burning
        for (int i = 0; i < 120 * 2; i++) w.StepOnce(ControlFrame.Empty);
        Assert.DoesNotContain(wrecked, w.Agents);
        Assert.Contains(near, w.Agents);
    }
}
