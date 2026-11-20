using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
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

    // ---- Final review fixes (I2, M3): only police that are chasing you, and not the Side car ----

    /// <summary>A still police car <paramref name="dz"/> m along and <paramref name="dl"/> m across from you, driven as a chaser acting as <paramref name="actingAs"/>.</summary>
    static AgentCar Chasing(World w, ChaseRole actingAs, double dz, double dl = 0, AgentKind kind = AgentKind.PoliceCruiser)
    {
        var car = Cruiser(w, dz, kind);
        car.Lateral = w.Player.Lateral + dl;
        var s = new ChaseDriver.State { Role = actingAs };
        s.Acted(actingAs);
        car.DriverState = s;
        return car;
    }

    [Fact]
    public void Stopping_beside_the_side_car_never_busts()
    {
        // The Side car rolls on past you at 3 m/s when you stop, so it stays within 15 m for up to 5 s: on its own it
        // busted a player who braked to a stop to get out of a box-in.
        var w = PursuitWorld(75);
        Chasing(w, ChaseRole.Side, 0, 5);
        Arm(w);
        for (int i = 0; i < 600; i++)
        {
            Hold(w, Slow);
            Assert.False(w.Busted.Counting);
        }
        Assert.Equal(VehicleState.Driving, w.Player.State);
    }

    [Theory]
    [InlineData(AgentKind.PoliceCruiser, ChaseRole.Trail)]
    [InlineData(AgentKind.PoliceCruiser, ChaseRole.Front)]
    [InlineData(AgentKind.Interceptor, ChaseRole.Trail)]
    [InlineData(AgentKind.ArmoredSuv, ChaseRole.Trail)]
    public void A_chaser_behind_still_busts_with_the_side_car_beside(AgentKind kind, ChaseRole role)
    {
        var w = PursuitWorld(75);
        Chasing(w, ChaseRole.Side, 0, 5);
        Chasing(w, role, -8, 0, kind);
        Arm(w);
        int bustedAt = 0;
        for (int i = 1; i <= 400 && bustedAt == 0; i++)
        {
            Hold(w, Slow);
            if (w.Events.Any(e => e is PlayerBusted)) bustedAt = i;
        }
        Assert.Equal(360, bustedAt);
    }

    [Theory]
    [InlineData(AgentKind.PolicePatrol)]
    [InlineData(AgentKind.PoliceCruiser)]
    [InlineData(AgentKind.Interceptor)]
    public void A_police_car_on_patrol_never_busts(AgentKind kind)
    {
        // At Pursuit a patrol car within 600 m starts chasing; one that has backed off (or an interceptor on patrol, which
        // only chases at Manhunt) is not chasing you, so it cannot bust you.
        var w = PursuitWorld(75);
        var car = Cruiser(w, -8, kind);
        car.Driver = PatrolDriver.Instance;
        car.CruiseSpeed = 0;   // it stays where it is
        Arm(w);
        for (int i = 0; i < 600; i++)
        {
            Hold(w, Slow);
            Assert.False(w.Busted.Counting);
        }
    }
}
