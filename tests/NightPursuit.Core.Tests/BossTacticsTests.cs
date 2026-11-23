using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

/// <summary>Plan 8: the boss's own pace (a 90 mph floor), its cunning, and its tactics when you hang back.</summary>
public class BossTacticsTests
{
    static World Straight(int seed = 1) => TestWorlds.World(seed, TestWorlds.Straight());

    static BossDefinition First(AgentKind kind, int cunning = 1) =>
        new("Test", 100, [new(kind, 0.5), new(AgentKind.Blocker, 0)]) { Cunning = cunning };

    static BossCar Spawn(World w, BossDefinition def, double distance, double speed = 40, bool engaged = true, double lateral = 0)
    {
        var b = BossCar.Create(w.NextId(), def, distance, lateral, speed);
        b.Engaged = engaged;
        w.Agents.Add(b);
        return b;
    }

    /// <summary>One step with the player held at <paramref name="speed"/> in the centre lane.</summary>
    static void Hold(World w, double speed, double lateral = 0)
    {
        var p = w.Player;
        p.Speed = speed;
        p.Lateral = lateral;
        p.LateralVel = 0;
        p.PushVel = 0;
        p.Heading = 0;
        w.StepOnce(ControlFrame.Empty);
    }

    static double Gap(World w, AgentCar b) => b.Distance - w.Player.Distance;

    // ---- cunning in the mission file ----

    const string Head = """{ "id": "m", "title": "T", "briefing": "B", "road": { "seed": 7 }, "approachLength": 6000, """;
    const string Waves = """ "waves": [ { "atDistance": 100, "kind": "Runner", "count": 1, "spawnOffset": 400 } ] """;

    static string WithCunning(string? cunning) =>
        Head + "\"boss\": { \"name\": \"Test\", \"health\": 100, "
        + (cunning is null ? "" : $"\"cunning\": {cunning}, ")
        + "\"phases\": [ { \"behaviour\": \"Blocker\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }, "
        + Waves + " }";

    static string MissionPath(string id) => Path.Combine(AppContext.BaseDirectory, "content", "missions", id + ".json");

    [Fact]
    public void Cunning_is_read_and_checked()
    {
        Assert.Equal(4, MissionLoader.Load(WithCunning("4"), "m.json").Boss!.Cunning);
        Assert.Equal(1, MissionLoader.Load(WithCunning(null), "m.json").Boss!.Cunning);
        foreach (var bad in new[] { "0", "7", "2.5", "\"3\"" })
        {
            var ex = Assert.ThrowsAny<Exception>(() => MissionLoader.Load(WithCunning(bad), "m.json"));
            Assert.Contains("boss.cunning must be a whole number from 1 to 6", ex.Message);
        }

        for (int i = 1; i <= 6; i++)
            Assert.Equal(i, MissionLoader.LoadFile(MissionPath($"mission-0{i}")).Boss!.Cunning);
        Assert.Equal(1, MissionLoader.LoadFile(MissionPath("patrol-ground")).Boss!.Cunning);
        Assert.Equal(1, MissionLoader.LoadFile(MissionPath("proving-ground")).Boss!.Cunning);
    }

    [Theory]
    [InlineData(1, 3.0, 56)]
    [InlineData(2, 2.5, 57)]
    [InlineData(3, 2.0, 59)]
    [InlineData(4, 1.5, 60)]
    [InlineData(5, 1.0, 62)]
    [InlineData(6, 1.0, 63)]
    public void Cunning_sets_reaction_time_and_running_pace(int cunning, double react, double pace)
    {
        Assert.Equal(react, BossCunning.ReactSeconds(cunning));
        Assert.Equal(pace, BossCunning.RunPace(cunning));
    }

    // ---- the floor ----

    [Theory]
    [InlineData(AgentKind.Runner)]
    [InlineData(AgentKind.Blocker)]
    [InlineData(AgentKind.Pursuer)]
    [InlineData(AgentKind.Gunner)]
    [InlineData(AgentKind.Dropper)]
    [InlineData(AgentKind.Armored)]
    public void An_engaged_boss_never_follows_you_below_90_mph(AgentKind phase)
    {
        Assert.Equal(40, BossMind.FloorSpeed);
        var w = Straight();
        var b = Spawn(w, First(phase), 60, speed: 45);
        double? outOfRange = null;
        for (int i = 0; i < 15 * 120; i++)
        {
            Hold(w, 0);
            if (i < 5 * 120) Assert.True(b.Speed >= BossMind.FloorSpeed - 1e-9, $"{phase}: {b.Speed:0.00} m/s at step {i}");
            if (outOfRange is null && Gap(w, b) > 380) outOfRange = w.Time;
        }
        Assert.NotNull(outOfRange);
    }

    [Fact]
    public void Above_the_floor_phases_still_pace_you()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, speed: 55);
        for (int i = 0; i < 5 * 120; i++)
        {
            Hold(w, 55);
            Assert.InRange(Gap(w, b), 55, 75);
        }
    }

    /// <summary>
    /// The floor acts only while the boss is ahead of you: behind you in your line, a phase that never overtakes must
    /// still be free to wait slower than you (here, stopped) rather than be pushed into your back.
    /// </summary>
    [Theory]
    [InlineData(AgentKind.Blocker, 0, 0)]
    [InlineData(AgentKind.Armored, 0, 0)]
    [InlineData(AgentKind.Blocker, 5, 5.5)]                     // outer lane: no way past on its side, so it waits behind
    [InlineData(AgentKind.Armored, -5, -5.5)]
    public void A_boss_behind_a_stopped_player_never_drives_into_them(AgentKind phase, double playerLateral, double bossLateral)
    {
        var w = Straight();
        var b = Spawn(w, First(phase), -25, speed: 10, lateral: bossLateral);
        for (int i = 0; i < 6 * 120; i++) Hold(w, 0, playerLateral);
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    [Fact]
    public void Caltrops_still_take_it_below_the_floor()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, speed: 40);
        b.SlowLeft = 3;
        var intent = BossDriver.Instance.Decide(b, new WorldView(w), World.Step);
        Assert.Equal(BossMind.FloorSpeed * BossDamage.CaltropSlowFactor, intent.TargetSpeed, 9);
    }

    [Fact]
    public void A_spinning_boss_is_not_held_at_the_floor()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, speed: 40);
        b.State = VehicleState.Spinning;
        b.StateTimer = 2;
        for (int i = 0; i < 120; i++) Hold(w, 0);
        Assert.True(b.Speed < BossMind.FloorSpeed, $"{b.Speed:0.00}");
    }

    [Fact]
    public void A_boss_far_ahead_no_longer_waits_for_you()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 700, speed: 50);
        w.Player.Speed = 10;
        var intent = BossDriver.Instance.Decide(b, new WorldView(w), World.Step);
        Assert.True(intent.TargetSpeed >= BossMind.FloorSpeed, $"{intent.TargetSpeed:0.00}");
    }

    [Fact]
    public void A_boss_easing_in_still_drives_below_you()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 400, speed: 20, engaged: false);
        w.Player.Speed = 20;
        var intent = BossDriver.Instance.Decide(b, new WorldView(w), World.Step);
        Assert.Equal(20 - BossCar.ArrivalSlower, intent.TargetSpeed, 9);
    }
}
