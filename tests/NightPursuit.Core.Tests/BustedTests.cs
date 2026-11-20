using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class BustedTests
{
    const double Step = World.Step;
    const double Slow = 0;          // stopped

    static World PursuitWorld(double heat)
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Heat.Set(heat);
        return w;
    }

    static AgentCar Cruiser(World w, double dz = -8, AgentKind kind = AgentKind.PolicePatrol)
    {
        var car = AgentCar.Create(w.NextId(), kind, w.Player.Distance + dz, w.Player.Lateral, 0);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    static void Hold(World w, double speed)
    {
        var p = w.Player;
        p.Speed = speed; p.Heading = 0; p.PushVel = 0; p.Armor = 100;
        w.StepOnce(ControlFrame.Empty);
    }

    /// <summary>The player has moved above 5 mph once, which arms the watch (F11).</summary>
    static void Arm(World w) => Hold(w, 6);

    static MissionRun PoliceRun(double heat, int seed = 3) =>
        new(new MissionDefinition("t", "T", "B", new TrackProfile(7), 200000, 5, Array.Empty<WaveDefinition>(),
            Police: new PoliceDefinition(0, heat)), new FeelSettings(), seed);

    static void Tick(MissionRun run, double speed)
    {
        run.World.Player.Speed = speed;
        run.World.Player.Armor = 100;
        run.Update(ControlFrame.Empty, Step);
    }

    [Fact]
    public void Busted_needs_pursuit_level()
    {
        var w = PursuitWorld(60);
        Cruiser(w);
        Arm(w);
        for (int i = 0; i < 600; i++)
        {
            Hold(w, Slow);
            Assert.False(w.Busted.Counting);
            Assert.DoesNotContain(w.Events, e => e is PlayerBusted);
        }
        Assert.Equal(VehicleState.Driving, w.Player.State);
    }

    [Fact]
    public void Busted_does_not_count_before_the_first_move()
    {
        var w = PursuitWorld(75);
        Cruiser(w);
        for (int i = 0; i < 600; i++)
        {
            Hold(w, Slow);
            Assert.False(w.Busted.Counting);
            Assert.DoesNotContain(w.Events, e => e is PlayerBusted);
        }
        Arm(w);
        Hold(w, Slow);
        Assert.True(w.Busted.Counting);
    }

    [Fact]
    public void Busted_after_3_s_held()
    {
        var run = PoliceRun(75);
        var w = run.World;
        Cruiser(w);
        int spare = w.Player.SpareCars;
        Tick(run, 6);   // arms the watch
        int bustedAt = 0, heatSetMessages = 0;
        bool heatChanged = false;
        for (int i = 1; i <= 360 && bustedAt == 0; i++)
        {
            Tick(run, Slow);
            if (w.Events.Any(e => e is PlayerBusted)) bustedAt = i;
        }
        Assert.Equal(360, bustedAt);
        Assert.Contains(w.Events, e => e is PlayerWrecked { Reason: "BUSTED" });
        Assert.Contains(w.Events, e => e is Message { Text: "BUSTED - YOU LOSE A SPARE CAR", Tone: Tone.Bad });
        Assert.Equal("BUSTED", w.Announcement?.Title);
        Assert.Equal(RunState.Respawning, run.State);
        Assert.Equal(VehicleState.Wrecked, w.Player.State);

        for (int i = 0; i < 400 && run.State == RunState.Respawning; i++)
        {
            Tick(run, 0);
            heatChanged |= w.Events.Any(e => e is HeatChanged { Reason: "BUSTED" });
            heatSetMessages += w.Events.Count(e => e is Message { Text: "HEAT SET TO 50: PATROL", Tone: Tone.Info });
        }
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(spare - 1, w.Player.SpareCars);
        Assert.Equal(50, w.Heat.Value, 1);
        Assert.Equal(HeatLevel.Patrol, w.Heat.Level);
        Assert.True(heatChanged);
        Assert.Equal(1, heatSetMessages);
        Assert.Equal(0, w.Busted.Held);
        Assert.False(w.BustedPending);
    }

    [Fact]
    public void Countdown_shows_from_the_first_step_and_cancels_above_5_mph()
    {
        var w = PursuitWorld(75);
        Cruiser(w);
        Arm(w);
        Hold(w, 0);
        Assert.True(w.Busted.Counting);
        Assert.Equal(3, w.Busted.SecondsLeft);
        for (int i = 1; i < 180; i++) Hold(w, 0);
        Assert.Equal(2, w.Busted.SecondsLeft);

        for (int i = 0; i < 600; i++)
        {
            Hold(w, 3);   // 3 m/s is above 5 mph (2.235 m/s)
            Assert.False(w.Busted.Counting);
            Assert.Equal(0, w.Busted.Held);
            Assert.DoesNotContain(w.Events, e => e is PlayerBusted);
        }
        // StepOnce does not clear Events, so they hold the whole run.
        Assert.Equal(1, w.Events.Count(e => e is Message { Text: "BROKE FREE", Tone: Tone.Good }));
    }

    [Fact]
    public void Blinded_police_cannot_bust_you()
    {
        var w = PursuitWorld(75);
        var c = Cruiser(w);
        Arm(w);
        for (int i = 0; i < 600; i++)
        {
            c.Blinded = 10;
            Hold(w, Slow);
            Assert.False(w.Busted.Counting);
        }
        Assert.DoesNotContain(w.Events, e => e is PlayerBusted);
    }

    [Fact]
    public void Police_out_of_range_cannot_bust_you()
    {
        var w = PursuitWorld(75);
        Cruiser(w, -16);
        Arm(w);
        for (int i = 0; i < 300; i++) { Hold(w, Slow); Assert.False(w.Busted.Counting); }
    }

    [Fact]
    public void Nothing_counts_while_wrecked_or_respawning()
    {
        var run = PoliceRun(75);
        var w = run.World;
        Cruiser(w);
        Tick(run, 6);
        for (int i = 0; i < 100; i++) Tick(run, Slow);
        Assert.True(w.Busted.Counting);
        w.Player.Armor = 0;                      // scraped to pieces: wrecked, not busted
        run.Update(ControlFrame.Empty, Step);
        Assert.Equal(RunState.Respawning, run.State);
        Assert.False(w.Busted.Counting);
        Assert.False(w.BustedPending);
        for (int i = 0; i < 400 && run.State == RunState.Respawning; i++)
        {
            run.Update(ControlFrame.Empty, Step);
            Assert.Equal(0, w.Busted.Held);
        }
        Assert.Equal(RunState.Playing, run.State);
        // A fresh car must move before the watch counts again (F11).
        for (int i = 0; i < 50; i++) { Tick(run, Slow); Assert.False(w.Busted.Counting); }
    }

    [Fact]
    public void A_paused_run_does_not_count()
    {
        var run = PoliceRun(75);
        var w = run.World;
        Cruiser(w);
        Tick(run, 6);
        Tick(run, Slow);
        double held = w.Busted.Held;
        run.Handle(new GameAction(ActionKind.Pause));
        for (int i = 0; i < 600; i++) run.Update(ControlFrame.Empty, Step);
        Assert.Equal(held, w.Busted.Held);
        Assert.Equal(RunState.Paused, run.State);
    }

    [Fact]
    public void A_wreck_respawn_keeps_heat()
    {
        var run = PoliceRun(80);
        var w = run.World;
        Tick(run, 20);
        w.Player.Armor = 0;
        run.Update(ControlFrame.Empty, Step);
        Assert.Equal(RunState.Respawning, run.State);
        for (int i = 0; i < 400 && run.State == RunState.Respawning; i++) run.Update(ControlFrame.Empty, Step);
        Assert.Equal(RunState.Playing, run.State);
        Assert.InRange(w.Heat.Value, 75, 80);
        Assert.DoesNotContain(w.Events, e => e is HeatChanged { Reason: "BUSTED" });
    }
}
