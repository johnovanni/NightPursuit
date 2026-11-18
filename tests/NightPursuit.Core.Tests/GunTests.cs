using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class GunTests
{
    static readonly ControlFrame Firing = new() { Fire = true };

    [Fact]
    public void Tracer_on_straight_keeps_its_line()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.StepOnce(Firing);
        var t = Assert.Single(w.Tracers);
        double side = w.Loadout.Guns.LeftGun ? -0.7 : 0.7;
        while (t.Travel < 300 && !t.Dead) w.StepOnce(ControlFrame.Empty);
        Assert.False(t.Dead);
        Assert.InRange(Math.Abs(t.Pose.Lateral - side), 0, 1e-6);
    }

    [Fact]
    public void Tracer_on_bend_leaves_road_on_outside()
    {
        var w = TestWorlds.World(track: TestWorlds.Bend(200));
        w.StepOnce(Firing);
        var t = Assert.Single(w.Tracers);
        while (t.Travel < 100) w.StepOnce(ControlFrame.Empty);
        // Tangent line from 2 m ahead of the car (about 102 m out) plus the 0.7 m gun offset: a little beyond the brief's 23.8.
        Assert.InRange(t.Pose.Lateral, -25.3, -22.3);
    }

    [Fact]
    public void Swept_hit_cannot_tunnel()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 70;                                   // tracer speed 330 m/s = 2.75 m per step
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 30, 0, 0);
        w.Agents.Add(car);
        w.StepOnce(Firing);
        for (int i = 0; i < 40; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Equal(VehicleStats.Runner.Armor - 1, car.Armor);
    }

    [Fact]
    public void Guns_overheat_after_4_s_and_lock_2_s()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        double lockedAt = -1, unlockedAt = -1;
        for (int i = 0; i < 120 * 8; i++)
        {
            w.StepOnce(Firing);
            if (lockedAt < 0 && w.Loadout.Guns.Locked) lockedAt = w.Time;
            else if (lockedAt >= 0 && unlockedAt < 0 && !w.Loadout.Guns.Locked) unlockedAt = w.Time;
        }
        Assert.InRange(lockedAt, 3.95, 4.05);
        Assert.InRange(unlockedAt, 5.95, 6.05);
    }

    [Fact]
    public void Guns_cool_when_trigger_released_and_never_go_below_zero()
    {
        var guns = new MachineGuns();
        for (int i = 0; i < 240; i++) guns.TryFire(true, 1.0 / 120);     // 2 s of fire: Heat 0.5
        Assert.InRange(guns.Heat, 0.49, 0.51);
        for (int i = 0; i < 120 * 3; i++) guns.TryFire(false, 1.0 / 120);
        Assert.Equal(0, guns.Heat);
    }

    [Fact]
    public void Guns_fire_every_75_ms_and_alternate()
    {
        var guns = new MachineGuns();
        var shots = new List<bool>();
        for (int i = 0; i < 120; i++)
            if (guns.TryFire(true, 1.0 / 120)) shots.Add(guns.LeftGun);
        Assert.InRange(shots.Count, 13, 14);                     // 1 s / 0.075 s
        Assert.True(shots.Zip(shots.Skip(1)).All(p => p.First != p.Second));
    }

    [Fact]
    public void Wrecked_player_cannot_fire()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.State = VehicleState.Wrecked;
        for (int i = 0; i < 30; i++) w.StepOnce(Firing);
        Assert.Empty(w.Tracers);
    }

    [Fact]
    public void Gun_kill_leaves_wreck_in_lane()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Lateral = 5;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 30, 5, 0);
        w.Agents.Add(car);
        var events = new List<GameEvent>();
        for (int i = 0; i < 240 && w.Agents.Count > 0; i++)
        {
            w.StepOnce(Firing);
            events.AddRange(w.Events);
        }
        var wreck = Assert.IsType<Wreck>(Assert.Single(w.Hazards));
        Assert.Equal(5, wreck.Lateral);
        Assert.Empty(w.Agents);
        Assert.Equal(100, w.Score);
        Assert.Contains(new VehicleDestroyed(events.OfType<VehicleDestroyed>().Single().Time, car.Id, AgentKind.Runner,
            KillCause.Gunfire, 100), events);
        Assert.Contains(events, e => e is Message { Text: "RUNNER DESTROYED  +100" });
    }

    [Fact]
    public void Destroy_twice_scores_once()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 30, 0, 0);
        w.Agents.Add(car);
        w.Destroy(car, KillCause.RanOffRoad);
        w.Destroy(car, KillCause.Gunfire);
        Assert.Equal(100, w.Score);
        Assert.Empty(w.Hazards);
        Assert.Contains(car, w.Agents);                         // non-gunfire kills keep the car as a wreck
        Assert.Equal(VehicleState.Wrecked, car.State);
    }

    [Fact]
    public void Wrecks_clear_600_m_behind()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        var wreck = new Wreck { Distance = 0, Lateral = 0 };
        w.Hazards.Add(wreck);
        w.Player.Distance = 601;
        w.StepOnce(ControlFrame.Empty);
        Assert.True(wreck.Expired);
        Assert.Empty(w.Hazards);
    }

    [Fact]
    public void Gunfire_wreck_slides_to_a_stop_in_its_lane()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 20, 0, 40);
        car.Armor = 1;
        w.Agents.Add(car);

        int steps = 0;
        while (w.Hazards.Count == 0 && steps++ < 600) w.StepOnce(Firing);
        var wreck = Assert.IsType<Wreck>(Assert.Single(w.Hazards));
        Assert.Empty(w.Agents);

        // The car now drives on while it is shot, so measure the slide from where the wreck appeared.
        // The kill step already slid the wreck once; count it as step 1 of the slide.
        double start = wreck.Distance;
        int slide = 1;
        while (wreck.Speed > 0) { w.StepOnce(ControlFrame.Empty); slide++; }
        Assert.InRange(slide * World.Step, 1.58, 1.62);
        Assert.InRange(wreck.Distance - start, 31.2, 32.2);
        Assert.Equal(0, wreck.Lateral);
    }
}
