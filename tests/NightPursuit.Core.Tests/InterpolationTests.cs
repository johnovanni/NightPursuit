using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

/// <summary>
/// A 75 Hz monitor gives 1.6 fixed steps per frame, so frames alternate 1 and 2 steps. Drawing the state after the last
/// whole step judders; drawing Prev + (Cur - Prev) * Alpha moves evenly.
/// </summary>
public class InterpolationTests
{
    const double Frame = 1.0 / 75;
    const double Speed = 40;

    /// <summary>A flat straight world where holding the analog gas exactly cancels coasting drag at <see cref="Speed"/>, so speed stays constant.</summary>
    static World SteadyWorld()
    {
        var feel = new FeelSettings();
        double rr = Speed / feel.MaxSpeed;
        feel.MaxAccel = 0.6 / (1 - rr * rr);   // engine force at full throttle == coast drag (0.6 m/s^2 at full throttle)
        return new World(TestWorlds.Straight(), feel, new SeededRng(1));
    }

    static readonly ControlFrame GasHeld = new() { PadConnected = true, PadGas = 1 };

    static double Lerp(double prev, double cur, double alpha) => prev + (cur - prev) * alpha;

    /// <summary>Per-frame advance of a position over 2 s of 1/75 s frames, ignoring the first frame (it only primes the one-step lag).</summary>
    static List<double> FrameDeltas(World w, Func<double> position)
    {
        var deltas = new List<double>();
        double last = double.NaN;
        for (int i = 0; i < 150; i++)
        {
            w.Player.Speed = Speed;
            foreach (var a in w.Agents) a.Speed = Speed - 10;
            w.Advance(GasHeld, Frame);
            double now = position();
            if (i >= 2) deltas.Add(now - last);
            last = now;
        }
        return deltas;
    }

    [Fact]
    public void Interpolated_player_distance_advances_evenly_at_75_hz()
    {
        var w = SteadyWorld();
        var p = w.Player;
        var smooth = FrameDeltas(w, () => Lerp(p.PrevDistance, p.Distance, w.Alpha));
        Assert.All(smooth, d => Assert.Equal(Speed * Frame, d, 1e-6));
    }

    [Fact]
    public void Uninterpolated_player_distance_judders_at_75_hz()
    {
        // The contrast that motivates the fix: frames alternate 1 and 2 steps.
        var w = SteadyWorld();
        var raw = FrameDeltas(w, () => w.Player.Distance);
        Assert.True(raw.Max() - raw.Min() > 0.9 * Speed * World.Step, $"spread was {raw.Max() - raw.Min()}");
    }

    [Fact]
    public void Interpolated_agent_distance_advances_evenly_at_75_hz()
    {
        var w = SteadyWorld();
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 200, 4, Speed - 10);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        var smooth = FrameDeltas(w, () => Lerp(car.PrevDistance, car.Distance, w.Alpha));
        Assert.All(smooth, d => Assert.Equal((Speed - 10) * Frame, d, 1e-6));
    }

    [Fact]
    public void Alpha_is_the_leftover_fraction_of_a_step_and_does_not_change_the_hash()
    {
        var w = TestWorlds.World();
        Assert.Equal(0, w.Alpha);
        w.Advance(ControlFrame.Empty, 1.5 * World.Step);   // one step, half a step left over
        Assert.Equal(0.5, w.Alpha, 6);

        ulong Run(Func<World, double> touch)
        {
            var x = TestWorlds.World(seed: 3);
            double sink = 0;
            for (int i = 0; i < 600; i++) { x.Advance(new ControlFrame { Gas = true }, Frame); sink += touch(x); }
            return x.StateHash();
        }
        Assert.Equal(Run(_ => 0), Run(x => x.Alpha + x.Player.PrevDistance));
    }

    [Fact]
    public void Tracer_prev_fields_equal_their_values_at_the_start_of_the_latest_step()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.StepOnce(new ControlFrame { Fire = true });
        Assert.NotEmpty(w.Tracers);
        var t = w.Tracers[0];
        for (int i = 0; i < 5; i++)
        {
            var before = t.Pose;
            w.StepOnce(ControlFrame.Empty);
            Assert.Equal(before.Distance, t.PrevDistance);
            Assert.Equal(before.Lateral, t.PrevLateral);
            Assert.Equal(before.Heading, t.PrevHeading);
            Assert.NotEqual(before.Distance, t.Pose.Distance);
        }
    }

    [Fact]
    public void Missile_prev_pose_equals_its_pose_at_the_start_of_the_latest_step()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Handle(new GameAction(ActionKind.FireMissile));
        var m = Assert.Single(w.Missiles);
        Assert.Equal(m.Pose.Distance, m.PrevDistance);
        Assert.Equal(m.Pose.Lateral, m.PrevLateral);
        Assert.Equal(m.Pose.Heading, m.PrevHeading);
        for (int i = 0; i < 8; i++)
        {
            double d = 0, l = 0, h = 0;
            var pose = m.Pose;
            // Frames of 1/75 s do not line up with the 1/120 s step, so a frame runs one or two steps.
            w.Advance(ControlFrame.Empty, 1.0 / 75, () => { d = pose.Distance; l = pose.Lateral; h = pose.Heading; pose = m.Pose; return false; });
            Assert.Equal(d, m.PrevDistance, 1e-12);
            Assert.Equal(l, m.PrevLateral, 1e-12);
            Assert.Equal(h, m.PrevHeading, 1e-12);
            Assert.NotEqual(m.Pose.Distance, m.PrevDistance);
        }
    }

    [Fact]
    public void Enemy_missile_prev_fields_equal_their_values_at_the_start_of_the_latest_step()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Distance = 500;
        w.Player.Speed = 30;
        var def = new BossDefinition("Test", 100, [new(AgentKind.Pursuer, 0.5, FiresMissiles: true), new(AgentKind.Pursuer, 0)]);
        var boss = BossCar.Create(w.NextId(), def, 600, 0, 30);
        boss.Engaged = true;
        w.Agents.Add(boss);
        for (int i = 0; i < 240 && w.EnemyMissiles.Count == 0; i++)
        {
            boss.Distance = w.Player.Distance + 100;
            w.StepOnce(ControlFrame.Empty);
        }

        // On the launch step it is drawn where it starts.
        var m = Assert.Single(w.EnemyMissiles);
        Assert.Equal(m.Distance, m.PrevDistance);
        Assert.Equal(m.Lateral, m.PrevLateral);
        w.Agents.Remove(boss);
        w.Player.Lateral = 3;                  // so the missile also moves sideways

        (double D, double L) startOfNext = (m.Distance, m.Lateral), startOfLast = startOfNext;
        for (int frame = 0; frame < 8; frame++)
        {
            // Frames of 1/75 s do not line up with the 1/120 s step, so a frame runs one or two steps.
            int steps = w.Advance(ControlFrame.Empty, Frame, () =>
            {
                startOfLast = startOfNext;
                startOfNext = (m.Distance, m.Lateral);
                return false;
            });
            Assert.True(steps > 0);
            Assert.Equal(startOfLast.D, m.PrevDistance, 1e-12);
            Assert.Equal(startOfLast.L, m.PrevLateral, 1e-12);
            Assert.NotEqual(m.Distance, m.PrevDistance);
            Assert.NotEqual(m.Lateral, m.PrevLateral);
        }
    }

    [Fact]
    public void Wreck_prev_distance_equals_its_distance_at_the_start_of_the_latest_step()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        var wreck = new Wreck { Distance = 100, Lateral = 3, Width = 1.9, Length = 4.4, Speed = 20 };
        w.Hazards.Add(wreck);
        for (int i = 0; i < 5; i++)
        {
            double before = wreck.Distance;
            w.StepOnce(ControlFrame.Empty);
            Assert.Equal(before, wreck.PrevDistance);
            Assert.NotEqual(before, wreck.Distance);
        }
    }

    [Fact]
    public void Smoke_prev_distance_equals_its_distance_at_the_start_of_the_latest_step()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        var cloud = new SmokeCloud { Distance = 50, PrevDistance = 50, Lateral = 0, Speed = 20 };
        w.Hazards.Add(cloud);
        Assert.Equal(cloud.Distance, cloud.PrevDistance);

        double startOfNextStep = cloud.Distance, startOfLastStep = cloud.Distance;
        for (int frame = 0; frame < 60; frame++)
        {
            int steps = w.Advance(ControlFrame.Empty, 1.0 / 75, () =>
            {
                startOfLastStep = startOfNextStep;     // this step began where the previous one ended
                startOfNextStep = cloud.Distance;
                return false;
            });
            if (steps == 0) continue;
            Assert.Equal(startOfLastStep, cloud.PrevDistance, 1e-12);
            Assert.NotEqual(cloud.Distance, cloud.PrevDistance);
        }
    }

    [Fact]
    public void Agent_create_leaves_prev_equal_to_current()
    {
        var c = AgentCar.Create(3, AgentKind.Pursuer, 123.4, -2.5, 30);
        Assert.Equal(123.4, c.PrevDistance);
        Assert.Equal(-2.5, c.PrevLateral);
        Assert.Equal(c.Heading, c.PrevHeading);
        Assert.Equal(c.SpinAngle, c.PrevSpinAngle);
    }

    [Fact]
    public void A_car_added_mid_run_is_not_swept_in_from_the_origin()
    {
        var w = TestWorlds.World();
        w.Advance(ControlFrame.Empty, 0.5);
        var c = AgentCar.Create(w.NextId(), AgentKind.Runner, 400, 3, 20);
        w.Agents.Add(c);
        w.Advance(ControlFrame.Empty, Frame);
        Assert.InRange(Lerp(c.PrevDistance, c.Distance, w.Alpha), 399, 402);
    }

    [Fact]
    public void Respawn_leaves_player_prev_equal_to_current()
    {
        var def = new MissionDefinition("test", "Test", "Briefing", new TrackProfile(7), 6000, 5, Array.Empty<WaveDefinition>());
        var run = new MissionRun(def, new FeelSettings(), seed: 3);
        var p = run.World.Player;
        run.Update(new ControlFrame { Gas = true }, 1.0);   // get the car moving so a stale Prev would differ
        p.Armor = 0;
        run.Update(ControlFrame.Empty, Frame);
        Assert.Equal(RunState.Respawning, run.State);
        for (int i = 0; i < 400 && run.State == RunState.Respawning; i++) run.Update(ControlFrame.Empty, Frame);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(p.Distance, p.PrevDistance);
        Assert.Equal(p.Lateral, p.PrevLateral);
        Assert.Equal(p.Heading, p.PrevHeading);
        Assert.Equal(p.SpinAngle, p.PrevSpinAngle);
    }
}
