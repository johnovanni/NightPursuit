using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class CollisionTests
{
    static readonly ControlFrame Gas = new() { Gas = true };

    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    [Fact]
    public void Spinning_car_off_road_crashes_without_wreck()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 100, 5, 30);
        car.State = VehicleState.Spinning; car.StateTimer = 1.8; car.PushVel = 7; car.SpinRate = 6;
        w.Agents.Add(car);
        var events = new List<GameEvent>();
        for (int i = 0; i < 120 * 2; i++) { w.StepOnce(ControlFrame.Empty); events.AddRange(w.Events); }
        Assert.Contains(events, e => e is VehicleDestroyed { Cause: KillCause.RanOffRoad });
        Assert.Equal(VehicleState.Wrecked, car.State);
        Assert.DoesNotContain(w.Hazards, h => h is Wreck);
    }

    [Fact]
    public void Hard_side_ram_spins_target()
    {
        var w = Straight();
        w.Player.Speed = 30;
        w.Player.LateralVel = 6;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 1.8, 30);   // 1.8 m: inside the 1.9 m contact width
        w.Agents.Add(car);
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Spinning, car.State);
        Assert.True(w.Player.Armor < 100);
        Assert.True(w.Player.PushVel < 0);                      // shoved away from the agent
        Assert.Contains(w.Events, e => e is VehicleSpun v && v.VehicleId == car.Id);
        Assert.Contains(w.Events, e => e is PlayerDamaged);
    }

    [Fact]
    public void Soft_sideswipe_does_not_spin_target()
    {
        var w = Straight();
        w.Player.Speed = 30;
        w.Player.LateralVel = 2;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 1.8, 30);
        w.Agents.Add(car);
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Driving, car.State);
        Assert.True(w.Player.Armor < 100);
    }

    [Fact]
    public void Player_rear_ending_agent_damages_both_and_sets_cooldown()
    {
        var w = Straight();
        w.Player.Speed = 40;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 4, 0, 20);
        w.Agents.Add(car);
        Collisions.Resolve(w);
        Assert.Equal(4.4, car.Distance - w.Player.Distance, 6);
        Assert.True(car.Armor < VehicleStats.Runner.Armor);
        Assert.True(w.Player.Armor < 100);
        Assert.Equal(0.25, car.ContactCooldown, 6);
        Assert.True(car.Speed > 20);
    }

    [Fact]
    public void Agent_ramming_player_from_behind_is_slowed_and_player_hurt()
    {
        var w = Straight();
        w.Player.Speed = 20;
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -4, 0, 40);
        w.Agents.Add(car);
        Collisions.Resolve(w);
        Assert.Equal(-4.4, car.Distance - w.Player.Distance, 6);
        Assert.True(w.Player.Armor < 100);
        Assert.True(car.Speed < 40);
    }

    [Fact]
    public void Armor_depleted_by_ram_destroys_agent_as_rammed()
    {
        var w = Straight();
        w.Player.Speed = 60;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 4, 0, 10);
        car.Armor = 0.5;
        w.Agents.Add(car);
        Collisions.Resolve(w);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.Rammed });
        Assert.Empty(w.Hazards);
    }

    [Fact]
    public void Player_armor_zero_wrecks_player_once()
    {
        var w = Straight();
        w.Player.Armor = 1;
        w.Player.Speed = 40;
        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Runner, 4, 0, 20));
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Wrecked, w.Player.State);
        Assert.Equal(0, w.Player.Armor);
        Assert.Equal(3, w.Player.StateTimer);
        Assert.Single(w.Events.OfType<PlayerWrecked>());
        Assert.Contains(w.Events, e => e is Message { Text: var t } && t.StartsWith("WRECKED: "));
        Collisions.Resolve(w);                                   // no player collisions once wrecked
        Assert.Single(w.Events.OfType<PlayerWrecked>());
    }

    [Fact]
    public void Guard_rail_scraping_can_wreck_player()
    {
        var w = Straight();
        w.Player.Lateral = 12.5; w.Player.Speed = 30; w.Player.Armor = 0.01;
        for (int i = 0; i < 10; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Equal(VehicleState.Wrecked, w.Player.State);
        Assert.Equal(0, w.Player.Armor);
    }

    [Fact]
    public void Wrecked_player_rolls_to_a_stop_whatever_the_throttle()
    {
        var w = Straight();
        w.Player.State = VehicleState.Wrecked; w.Player.StateTimer = 3; w.Player.Speed = 30;
        for (int i = 0; i < 120 * 2; i++) w.StepOnce(Gas);
        Assert.Equal(0, w.Player.Speed);
    }

    [Fact]
    public void Player_hitting_wreck_stops_hard()
    {
        var w = Straight();
        w.Player.Speed = 60;
        w.Hazards.Add(new Wreck { Distance = 3, Lateral = 0, Width = 1.9, Length = 4.4 });
        w.StepOnce(ControlFrame.Empty);
        Assert.True(w.Player.Speed <= 5);
        Assert.True(w.Player.Armor <= 70);
        Assert.Contains(w.Events, e => e is Message { Text: "HIT A WRECK" });
    }

    [Fact]
    public void Wreck_damages_player_once_then_blocks()
    {
        var w = Straight();
        w.Player.Speed = 20;
        w.Hazards.Add(new Wreck { Distance = 20, Lateral = 0, Width = 1.9, Length = 4.4 });
        double edge = 20 - 4.4;
        for (int i = 0; i < 240; i++)
        {
            w.StepOnce(Gas);
            Assert.True(w.Player.Distance <= edge + 1e-9, $"step {i}: {w.Player.Distance}");
        }
        Assert.Equal(70, w.Player.Armor, 9);
        Assert.Equal(edge, w.Player.Distance, 3);
    }

    [Fact]
    public void Agent_hitting_wreck_is_destroyed_without_new_wreck()
    {
        var w = Straight();
        var wreck = new Wreck { Distance = 80, Lateral = 0, Width = 1.9, Length = 4.4 };
        w.Hazards.Add(wreck);
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 74, 0, 30);
        w.Agents.Add(car);
        for (int i = 0; i < 60; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Equal(VehicleState.Wrecked, car.State);
        Assert.Single(w.Hazards);
        Assert.Same(wreck, w.Hazards[0]);
        Assert.Equal(100, w.Score);
    }

    [Fact]
    public void Tracer_hits_nearest_car_along_its_path()
    {
        var w = Straight();
        var far = AgentCar.Create(w.NextId(), AgentKind.Runner, 31, 0, 0);
        var near = AgentCar.Create(w.NextId(), AgentKind.Runner, 30, 0, 0);
        w.Agents.Add(far);                                        // list order: the farther car first
        w.Agents.Add(near);
        w.Tracers.Add(new Tracer { Pose = new RoadPose(27, 0, 0), Speed = 330, OwnerId = 0 });
        w.StepOnce(ControlFrame.Empty);
        Assert.Equal(VehicleStats.Runner.Armor - 1, near.Armor);
        Assert.Equal(VehicleStats.Runner.Armor, far.Armor);
    }
}
