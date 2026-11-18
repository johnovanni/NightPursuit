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
    public void Player_pinned_behind_a_stopped_wreck_can_steer_round_it()
    {
        var w = Straight();
        w.Player.Speed = 20;
        var wreck = new Wreck { Distance = 20, Lateral = 0, Width = 1.9, Length = 4.4 };
        w.Hazards.Add(wreck);
        var gasRight = new ControlFrame { Gas = true, SteerDir = 1 };
        double clearWidth = (wreck.Width + w.Player.Stats.Width) / 2;

        int clearAt = -1;
        for (int i = 0; i < 120 * 2 && clearAt < 0; i++)
        {
            w.StepOnce(gasRight);
            if (w.Player.Lateral - wreck.Lateral >= clearWidth) clearAt = i;
        }
        Assert.True(clearAt >= 0, $"still pinned after 2 s, lateral {w.Player.Lateral}");

        for (int i = 0; i < 120 * 6; i++) w.StepOnce(Gas);
        Assert.True(w.Player.Distance > wreck.Distance + wreck.Length, $"distance {w.Player.Distance}");
        Assert.Equal(70, w.Player.Armor, 9);
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
        // 5 m apart, so the two cars don't touch each other (4.4 m would); the fast tracer sweeps 27 -> 37 m in one
        // step, across both of them.
        var far = AgentCar.Create(w.NextId(), AgentKind.Runner, 35, 0, 0);
        var near = AgentCar.Create(w.NextId(), AgentKind.Runner, 30, 0, 0);
        w.Agents.Add(far);                                        // list order: the farther car first
        w.Agents.Add(near);
        w.Tracers.Add(new Tracer { Pose = new RoadPose(27, 0, 0), Speed = 1200, OwnerId = 0 });
        w.StepOnce(ControlFrame.Empty);
        Assert.Equal(VehicleStats.Runner.Armor - 1, near.Armor);
        Assert.Equal(VehicleStats.Runner.Armor, far.Armor);
    }

    [Fact]
    public void Player_beside_a_wreck_is_pushed_out_sideways_and_hurt_once()
    {
        var w = Straight();
        var wreck = new Wreck { Distance = 100, Lateral = 0, Width = 1.9, Length = 4.4 };
        w.Hazards.Add(wreck);
        w.Player.Distance = 99; w.Player.Lateral = 2.2;
        double halfW = (1.9 + w.Player.Stats.Width) / 2, contactZ = (4.4 + w.Player.Stats.Length) / 2;
        double worstOverlap = 0;
        for (int i = 0; i < 120 * 2; i++)
        {
            w.Player.Speed = 5;                                     // creeping past, nose held pointing into the wreck
            w.Player.Heading = -0.5;
            w.StepOnce(new ControlFrame { SteerDir = -1 });
            if (Math.Abs(wreck.Distance - w.Player.Distance) < contactZ)
                worstOverlap = Math.Max(worstOverlap, halfW - Math.Abs(w.Player.Lateral - wreck.Lateral));
        }
        Assert.True(worstOverlap < 0.05, $"overlapped by {worstOverlap:F3} m");
        int damaged = w.Events.Count(e => e is PlayerDamaged);       // StepOnce keeps every event of the run
        Assert.True(damaged <= 1, $"{damaged} damage events");
        Assert.True(w.Player.Distance > 100, "should have driven past, not been dragged back");
    }

    [Fact]
    public void Nothing_collides_with_a_player_wrecked_earlier_in_the_same_step()
    {
        var w = Straight();
        w.Player.Armor = 1;
        w.Player.Speed = 30;
        // Two cars overlap the player; the first hit kills the player, so the second must not touch the dead car.
        var first = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, 1.8, 30);
        var second = AgentCar.Create(w.NextId(), AgentKind.Runner, 0, -1.8, 30);
        w.Agents.Add(first); w.Agents.Add(second);
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Wrecked, w.Player.State);
        Assert.Equal(0, second.ContactCooldown);
        Assert.Equal(1, w.Events.Count(e => e is PlayerDamaged));
    }

    // Agent-vs-agent contact. These call Collisions.Resolve directly, as the ram tests above do: a full step would
    // overwrite LateralVel in StepAgent.

    static AgentCar StillRunner(World w, double distance, double lateral, double speed)
    {
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, distance, lateral, speed);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    [Fact]
    public void Agents_closing_fast_side_by_side_both_spin()
    {
        var w = Straight();
        var left = StillRunner(w, 50, -0.8, 30);
        var right = StillRunner(w, 50, 0.8, 30);
        left.LateralVel = 3; right.LateralVel = -3;               // closing at 6 m/s
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Spinning, left.State);
        Assert.Equal(VehicleState.Spinning, right.State);
        Assert.Equal(2, w.Events.OfType<VehicleSpun>().Count());
        Assert.Single(w.Events, e => e is Message { Text: "RUNNER AND RUNNER COLLIDED" });   // one collision, one message
        Assert.True(left.PushVel < 0 && right.PushVel > 0, $"push {left.PushVel:0.00} / {right.PushVel:0.00}");   // each spins away from the other
        Assert.Equal(100, w.Player.Armor);
    }

    [Fact]
    public void Gentle_side_contact_pushes_apart_without_spinning()
    {
        var w = Straight();
        var left = StillRunner(w, 50, -0.8, 30);
        var right = StillRunner(w, 50, 0.8, 30);
        left.LateralVel = 0.5; right.LateralVel = -0.5;           // closing at 1 m/s
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Driving, left.State);
        Assert.Equal(VehicleState.Driving, right.State);
        Assert.Equal(-(2.5 + 0.8 * 1), left.PushVel, 9);
        Assert.Equal(2.5 + 0.8 * 1, right.PushVel, 9);
        Assert.True(right.Lateral - left.Lateral >= 1.9 - 1e-9, $"apart {right.Lateral - left.Lateral}");
        Assert.DoesNotContain(w.Events, e => e is VehicleSpun);
    }

    [Fact]
    public void Rear_end_between_agents_slows_the_follower()
    {
        var w = Straight();
        var leader = StillRunner(w, 50, 0, 20);
        var follower = StillRunner(w, 46, 0, 35);
        Collisions.Resolve(w);
        Assert.Equal(18, follower.Speed, 9);
        Assert.Equal(45.6, follower.Distance, 9);
        Assert.Equal(20, leader.Speed, 9);
        Assert.Equal(50, leader.Distance, 9);
        Assert.Equal(VehicleStats.Runner.Armor - 0.5, follower.Armor, 9);
        Assert.Equal(VehicleStats.Runner.Armor - 0.5, leader.Armor, 9);
        Assert.Equal(0.25, follower.AgentContactCooldown, 9);
        Assert.Equal(0.25, leader.AgentContactCooldown, 9);
        Assert.Equal(0, follower.ContactCooldown);                // ContactCooldown still means "touched the player"
        Assert.Equal(0, leader.ContactCooldown);

        follower.Distance = 46;                                   // overlapping again while the cooldown runs: skipped
        Collisions.Resolve(w);
        Assert.Equal(VehicleStats.Runner.Armor - 0.5, follower.Armor, 9);
        Assert.Equal(46, follower.Distance, 9);
    }

    [Fact]
    public void Car_cut_off_by_a_faster_car_is_not_sped_up()
    {
        var w = Straight();
        var leader = StillRunner(w, 50, 0, 35);                   // pulled in just ahead, faster
        var follower = StillRunner(w, 46, 0, 20);
        Collisions.Resolve(w);
        Assert.Equal(20, follower.Speed, 9);
        Assert.Equal(45.6, follower.Distance, 9);
    }

    [Fact]
    public void Agent_contact_that_empties_armor_destroys_without_a_wreck()
    {
        var w = Straight();
        var leader = StillRunner(w, 50, 0, 20);
        var follower = StillRunner(w, 46, 0, 35);
        follower.Armor = 0.5;
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Wrecked, follower.State);
        Assert.Equal(VehicleState.Driving, leader.State);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.Rammed } d && d.VehicleId == follower.Id);
        Assert.Empty(w.Hazards);
    }

    [Fact]
    public void Broad_phase_with_25_cars_is_deterministic()
    {
        static (ulong Hash, int Contacts) Run()
        {
            var w = TestWorlds.World(4, TestWorlds.Straight());
            for (int i = 0; i < 25; i++)
                w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Runner, 30 + 12 * i, Track.LaneCentres[i % 3], 30 + (i * 7) % 20));
            int contacts = 0;
            bool CountContacts()
            {
                foreach (var a in w.Agents)
                    if (a.AgentContactCooldown == 0.25) contacts++;      // set this step
                return false;
            }
            for (int f = 0; f < 60 * 30; f++) w.Advance(Gas, 1.0 / 60, CountContacts);
            return (w.StateHash(), contacts);
        }
        var first = Run();
        Assert.True(first.Contacts > 0, "no car touched another, so the pass was never exercised");
        Assert.Equal(first, Run());
    }
}
