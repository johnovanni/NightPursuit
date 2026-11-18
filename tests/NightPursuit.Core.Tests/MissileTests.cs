using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class MissileTests
{
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static AgentCar Car(World w, AgentKind kind, double distance, double lateral, double speed = 0, IDriver? driver = null)
    {
        var car = AgentCar.Create(w.NextId(), kind, distance, lateral, speed);
        car.Driver = driver ?? new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    static int Count(World w, string text) => w.Events.OfType<Message>().Count(m => m.Text == text);

    [Fact]
    public void Lock_picks_the_nearest_hostile_ahead_inside_25_degrees()
    {
        var w = Straight();
        Car(w, AgentKind.Runner, 12, 0);        // too close
        Car(w, AgentKind.Runner, 50, -30);      // 31 degrees off the nose
        var want = Car(w, AgentKind.Runner, 200, 5);
        Car(w, AgentKind.Runner, 520, 0);       // out of range
        Assert.Same(want, w.FindMissileTarget());
    }

    [Fact]
    public void No_lock_on_cars_behind_or_wrecked()
    {
        var w = Straight();
        Car(w, AgentKind.Runner, -40, 0);
        var dead = Car(w, AgentKind.Runner, 100, 0);
        dead.State = VehicleState.Wrecked;
        Assert.Null(w.FindMissileTarget());
    }

    [Fact]
    public void Missile_homes_onto_a_lane_changer_and_leaves_a_wreck()
    {
        var w = Straight();
        var car = Car(w, AgentKind.Runner, 150, 0, 30, new RunnerDriver());
        car.DriverState = new RunnerDriver.State { Target = 5, LaneTimer = 100 };
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Single(w.Missiles);
        for (int i = 0; i < 300; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.Missile });
        var wreck = Assert.Single(w.Hazards.OfType<Wreck>());
        Assert.InRange(Math.Abs(wreck.Lateral - car.Lateral), 0, 1e-9);
        Assert.Equal(0, Count(w, "MISSILE LOST"));
    }

    [Fact]
    public void Missile_without_lock_flies_straight_and_says_so()
    {
        var w = Straight();
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Equal(1, Count(w, "MISSILE AWAY - NO LOCK"));
        Assert.Contains(w.Events, e => e is WeaponUsed { Weapon: WeaponKind.Missile });
        var m = Assert.Single(w.Missiles);
        double lateral = m.Pose.Lateral;
        for (int i = 0; i < 60; i++) w.StepOnce(ControlFrame.Empty);
        Assert.InRange(Math.Abs(m.Pose.Lateral - lateral), 0, 1e-6);
        Assert.Equal(0, m.Pose.Heading);
    }

    [Fact]
    public void Locked_launch_names_the_target()
    {
        var w = Straight();
        Car(w, AgentKind.Pursuer, 100, 0);
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Equal(1, Count(w, "MISSILE AWAY - LOCKED ON PURSUER"));
    }

    [Fact]
    public void Missile_dies_after_700_m_and_says_lost()
    {
        var w = Straight();
        w.Handle(new GameAction(ActionKind.FireMissile));
        var m = Assert.Single(w.Missiles);
        while (m.Travel < 690) w.StepOnce(ControlFrame.Empty);
        Assert.Single(w.Missiles);
        while (w.Missiles.Count > 0 && m.Travel < 710) w.StepOnce(ControlFrame.Empty);
        Assert.Empty(w.Missiles);
        Assert.Equal(1, Count(w, "MISSILE LOST"));
        Assert.Contains(w.Events, e => e is Message { Text: "MISSILE LOST", Tone: Tone.Info });
    }

    [Fact]
    public void Fire_missile_with_none_left_or_cooling_says_so()
    {
        var w = Straight();
        w.Loadout = new Loadout(LoadoutDefinition.Default with { Missiles = 0 });
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Empty(w.Missiles);
        Assert.Equal(1, Count(w, "NO MISSILES"));

        w = Straight();
        w.Loadout = new Loadout(LoadoutDefinition.Default with { Missiles = 2 });
        w.Handle(new GameAction(ActionKind.FireMissile));
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Single(w.Missiles);
        Assert.Equal(1, w.Loadout.Missiles.Charges);
        Assert.Equal(1, Count(w, "MISSILE NOT READY"));

        w = Straight();
        w.Player.State = VehicleState.Wrecked;
        int before = w.Events.Count;
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Empty(w.Missiles);
        Assert.Equal(1, w.Loadout.Missiles.Charges);
        Assert.Equal(before, w.Events.Count);
    }

    [Fact]
    public void Only_gunfire_missile_and_satellite_leave_wrecks()
    {
        Assert.True(KillCause.Gunfire.LeavesWreck());
        Assert.True(KillCause.Missile.LeavesWreck());
        Assert.True(KillCause.Satellite.LeavesWreck());
        Assert.False(KillCause.Rammed.LeavesWreck());
        Assert.False(KillCause.RanOffRoad.LeavesWreck());
        Assert.False(KillCause.WreckCollision.LeavesWreck());
    }

    [Fact]
    public void Swept_hit_core_overload_matches_the_vehicle_wrapper()
    {
        var car = AgentCar.Create(1, AgentKind.Runner, 100, 0.2, 0);   // 4.4 m long, 1.9 m wide
        Assert.True(World.SweptHit(97, 1.25, 103, 1.25, car, 0.15));
        Assert.False(World.SweptHit(97, 1.25, 103, 1.25, car, 0.0));

        // A target 1.2 m long and 1.6 m wide centred at (100, 0.2): a segment crossing it at lateral 1.2 is inside only with margin 0.6.
        Assert.True(World.SweptHit(98, 1.2, 102, 1.2, 100, 0.2, 0.6, 0.8, 0.6));
        Assert.False(World.SweptHit(98, 1.2, 102, 1.2, 100, 0.2, 0.6, 0.8, 0.0));
        Assert.False(World.SweptHit(90, 0, 95, 0, 100, 0.2, 0.6, 0.8, 0.6));
    }
}
