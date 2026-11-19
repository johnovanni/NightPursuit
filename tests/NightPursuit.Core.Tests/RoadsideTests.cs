using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Traffic;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class RoadsideTests
{
    static readonly ControlFrame Gas = new() { Gas = true };

    static World Straight()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 0;
        w.Player.SnapPrev();
        return w;
    }

    static double Centre(int lane) => Track.LaneCentres[lane];

    static SpeedTrap Trap(World w, double distance, double limit)
    {
        var t = new SpeedTrap { Distance = distance, Lateral = 9, LimitMph = limit };
        w.Hazards.Add(t);
        return t;
    }

    static Roadblock Block(World w, double distance, int gapLane)
    {
        var r = Roadblock.Place(distance, gapLane);
        w.Hazards.Add(r);
        return r;
    }

    /// <summary>Steps with the player's speed held until it has reached <paramref name="toDistance"/>.</summary>
    static void DriveHeld(World w, double speed, double toDistance)
    {
        for (int i = 0; i < 120 * 20 && w.Player.Distance < toDistance; i++)
        {
            w.Player.Speed = speed;
            w.StepOnce(ControlFrame.Empty);
        }
    }

    // ---- speed traps ----

    [Fact]
    public void Passing_a_trap_above_the_limit_costs_8_heat_once()
    {
        var w = Straight();
        var trap = Trap(w, 50, 70);
        var events = w.Events;   // StepOnce never clears them
        for (int i = 0; i < 120 * 3; i++)
        {
            w.Player.Speed = 40;                      // 89 mph
            w.StepOnce(ControlFrame.Empty);
        }
        Assert.Equal(8, w.Heat.Value, 9);
        Assert.True(trap.Triggered);
        Assert.True(trap.Caught);
        var passed = Assert.Single(events.OfType<SpeedTrapPassed>());
        Assert.Equal(70, passed.LimitMph);
        Assert.InRange(passed.Mph, 89, 90);
        Assert.Single(events, e => e is Message { Text: "SPEED TRAP 89 IN A 70  +8 HEAT", Tone: Tone.Bad });
        Assert.Single(events.OfType<HeatChanged>(), e => e.Reason == "SPEED TRAP");
    }

    [Fact]
    public void Passing_under_the_limit_is_free()
    {
        var w = Straight();
        var trap = Trap(w, 50, 70);
        var events = w.Events;   // StepOnce never clears them
        for (int i = 0; i < 120 * 4; i++)
        {
            w.Player.Speed = 30;                      // 67 mph
            w.StepOnce(ControlFrame.Empty);
        }
        Assert.Equal(0, w.Heat.Value);
        Assert.True(trap.Triggered);
        Assert.False(trap.Caught);
        Assert.Empty(events.OfType<SpeedTrapPassed>());
        Assert.Single(events, e => e is Message { Text: "SPEED TRAP: UNDER THE LIMIT", Tone: Tone.Good });
    }

    [Fact]
    public void A_trap_behind_the_player_never_triggers_and_expires_600_m_back()
    {
        var w = Straight();
        w.Player.Distance = 1000;
        w.Player.SnapPrev();
        var trap = Trap(w, 300, 60);
        w.StepOnce(ControlFrame.Empty);
        Assert.False(trap.Triggered);
        for (int i = 0; i < 120; i++) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }
        Assert.DoesNotContain(trap, w.Hazards);
    }

    [Fact]
    public void A_trap_never_collides()
    {
        var w = Straight();
        w.Player.Lateral = 7;
        w.Player.Speed = 20;
        Trap(w, 10, 80).Lateral = 7;
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 5, 7, 5);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        for (int i = 0; i < 120; i++) w.StepOnce(ControlFrame.Empty);
        Assert.NotEqual(VehicleState.Wrecked, car.State);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
    }

    // ---- roadblocks ----

    [Fact]
    public void Roadblock_always_leaves_its_gap_lane_open()
    {
        for (int gap = 0; gap < 3; gap++)
        {
            var r = Roadblock.Place(100, gap);
            Assert.Equal(Centre(gap), r.Lateral);
            Assert.False(r.Blocks(Centre(gap), 0.95));
            for (int lane = 0; lane < 3; lane++)
                if (lane != gap) Assert.True(r.Blocks(Centre(lane), 0.95), $"gap {gap}, lane {lane}");
        }
    }

    [Fact]
    public void Wreck_Blocks_is_the_avoid_test_with_its_0_3_margin()
    {
        var wreck = new Wreck { Distance = 10, Lateral = 2, Width = 1.9, Length = 4.4 };
        foreach (double lat in new[] { -1.0, 0.0, 0.35, 1.0, 2.0, 3.0, 4.9, 5.0 })
            Assert.Equal(Math.Abs(wreck.Lateral - lat) < (wreck.Width + 1.8) / 2 + 0.3, wreck.Blocks(lat, 1.8 / 2));
    }

    [Fact]
    public void Driving_through_the_gap_is_free()
    {
        var w = Straight();
        Block(w, 200, 2);
        w.Player.Lateral = Centre(2);
        w.Player.SnapPrev();
        DriveHeld(w, 50, 260);
        Assert.True(w.Player.Distance >= 260);
        Assert.Equal(100, w.Player.Armor);
        Assert.Equal(0, w.Heat.Value);
    }

    [Fact]
    public void Hitting_a_roadblock_costs_25_armor_15_heat_once()
    {
        var w = Straight();
        Block(w, 100, 2);
        w.Player.Lateral = Centre(0);
        w.Player.Speed = 30;
        w.Player.SnapPrev();
        var events = w.Events;   // StepOnce never clears them
        for (int i = 0; i < 120 * 4; i++)
        {
            w.StepOnce(Gas);
        }
        Assert.Equal(75, w.Player.Armor, 9);
        Assert.Single(events.OfType<RoadblockHit>());
        Assert.Single(events.OfType<PlayerDamaged>(), e => e.Reason == "HIT A ROADBLOCK");
        Assert.Single(events.OfType<HeatChanged>(), e => e.Reason == "ROADBLOCK" && e.Amount == 15);
        Assert.Contains(events, e => e is Message { Text: "ROADBLOCK  +15 HEAT", Tone: Tone.Bad });
        Assert.Equal(15, w.Heat.Value, 9);
        Assert.True(w.Player.Distance < 100, "held back by the barrier");
    }

    [Fact]
    public void The_hit_caps_speed_at_5()
    {
        var w = Straight();
        Block(w, 100, 2);
        w.Player.Lateral = Centre(1);
        w.Player.Speed = 40;
        w.Player.SnapPrev();
        for (int i = 0; i < 120 * 4 && !w.Events.OfType<RoadblockHit>().Any(); i++) w.StepOnce(Gas);
        Assert.Single(w.Events.OfType<RoadblockHit>());
        Assert.True(w.Player.Speed <= 5 + 1e-9, $"speed {w.Player.Speed}");
    }

    [Fact]
    public void You_crab_along_a_roadblock_at_5_ms()
    {
        var w = Straight();
        var rb = Block(w, 100, 2);                       // blocked: -5 and 0; the gap is at +5
        rb.HitPlayer = true;
        var p = w.Player;
        p.Lateral = Centre(0);
        p.Distance = rb.Distance - (Roadblock.Depth + p.Stats.Length) / 2;
        p.SnapPrev();
        var right = new ControlFrame { Gas = true, SteerDir = 1 };
        double clear = Roadblock.BarrierHalfWidth + p.Stats.Width / 2;      // 3.25 from the nearer blocked centre
        int cleared = -1;
        for (int i = 0; i < 120 * 2 && cleared < 0; i++)
        {
            w.StepOnce(right);
            if (p.Lateral - Centre(1) >= clear) cleared = i;
        }
        Assert.True(cleared >= 0 && cleared <= 204, $"clear after {cleared} steps, lateral {p.Lateral}");
        Assert.Equal(100, p.Armor);
    }

    [Fact]
    public void AI_cars_swerve_into_the_gap()
    {
        foreach (var (kind, speed) in new[] { (AgentKind.Civilian, 30.0), (AgentKind.Runner, 50.0) })
        {
            var w = Straight();
            Block(w, 500, 2);
            var car = AgentCar.Create(w.NextId(), kind, 100, Centre(0), speed);
            w.Agents.Add(car);
            var events = w.Events;   // StepOnce never clears them
            for (int i = 0; i < 120 * 20 && car.Distance < 560 && car.State != VehicleState.Wrecked; i++)
            {
                w.StepOnce(ControlFrame.Empty);
            }
            Assert.True(car.Distance >= 560, $"{kind} stopped at {car.Distance}, state {car.State}");
            Assert.DoesNotContain(events.OfType<VehicleDestroyed>(), e => e.Cause == KillCause.WreckCollision);
        }
    }

    [Fact]
    public void An_agent_driving_into_a_roadblock_is_destroyed_as_by_a_wreck()
    {
        var w = Straight();
        Block(w, 80, 2);
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 74, Centre(0), 30);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        for (int i = 0; i < 60; i++) w.StepOnce(ControlFrame.Empty);
        var events = w.Events;   // StepOnce never clears them
        Assert.Equal(VehicleState.Wrecked, car.State);
        Assert.Single(events.OfType<VehicleDestroyed>(), e => e.Cause == KillCause.WreckCollision);
    }

    [Fact]
    public void A_boss_is_never_destroyed_by_a_roadblock()
    {
        var w = Straight();
        Block(w, 80, 2);
        var def = new BossDefinition("Test", 100, [new(AgentKind.Blocker, 0.5), new(AgentKind.Pursuer, 0)]);
        var boss = BossCar.Create(w.NextId(), def, 74, Centre(0), 30);
        boss.Engaged = true;
        boss.Driver = new StillDriver();
        w.Agents.Add(boss);
        for (int i = 0; i < 60; i++) w.StepOnce(ControlFrame.Empty);
        Assert.NotEqual(VehicleState.Wrecked, boss.State);
        Assert.Equal(100, boss.Armor);
    }

    [Fact]
    public void A_roadblock_whose_gap_is_blocked_is_cleared()
    {
        var w = Straight();
        var rb = Block(w, 500, 2);
        w.Hazards.Add(new Wreck { Distance = 380, Lateral = Centre(2), Width = 1.9, Length = 4.4 });
        w.StepOnce(ControlFrame.Empty);
        Assert.DoesNotContain(rb, w.Hazards);
        Assert.Contains(w.Events, e => e is Message { Text: "ROADBLOCK CLEARED", Tone: Tone.Good });
    }

    [Fact]
    public void A_wreck_outside_the_gap_or_the_window_leaves_the_roadblock()
    {
        var w = Straight();
        var rb = Block(w, 500, 2);
        w.Hazards.Add(new Wreck { Distance = 380, Lateral = Centre(0), Width = 1.9, Length = 4.4 });   // a blocked lane
        w.Hazards.Add(new Wreck { Distance = 340, Lateral = Centre(2), Width = 1.9, Length = 4.4 });   // 160 m before
        w.Hazards.Add(new Wreck { Distance = 540, Lateral = Centre(2), Width = 1.9, Length = 4.4 });   // 40 m after
        w.StepOnce(ControlFrame.Empty);
        Assert.Contains(rb, w.Hazards);
    }

    // ---- carried from earlier tasks ----

    [Fact]
    public void A_spot_within_60_m_of_a_roadblock_is_not_free()
    {
        var w = Straight();
        Block(w, 500, 1);
        Assert.False(SpawnSpots.IsFree(w, 450, Centre(0)));
        Assert.False(SpawnSpots.IsFree(w, 555, Centre(2)));
        Assert.True(SpawnSpots.IsFree(w, 435, Centre(0)));
        Assert.True(SpawnSpots.IsFree(w, 565, Centre(2)));
    }

    [Fact]
    public void FreeNeighbourLane_treats_a_roadblocks_blocked_lanes_like_a_wreck()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Civilian, 100, 0, 30);
        w.Agents.Add(car);
        var view = new WorldView(w);
        Block(w, 130, 2);                                          // blocked: -5 and 0; the gap is +5
        Assert.Equal(5, DriverHelpers.FreeNeighbourLane(car, view, 5, 40, -1));
        w.Hazards.Clear();
        Block(w, 130, 0);                                          // blocked: 0 and +5; the gap is -5
        Assert.Equal(-5, DriverHelpers.FreeNeighbourLane(car, view, 5, 40, 1));
        w.Hazards.Clear();
        Block(w, 200, 0);                                          // too far ahead to matter
        Assert.Equal(5, DriverHelpers.FreeNeighbourLane(car, view, 5, 40, 1));
    }
}
