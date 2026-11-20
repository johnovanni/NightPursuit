using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Police;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class WorldLoopTests
{
    [Fact]
    public void Advance_runs_whole_steps_and_carries_remainder()
    {
        var w = TestWorlds.World();
        Assert.Equal(1, w.Advance(ControlFrame.Empty, 0.01));
        Assert.Equal(1, w.Advance(ControlFrame.Empty, 0.01)); // 0.02 total = 2 steps, remainder 0.0033
        Assert.Equal(2 * World.Step, w.Time, 9);
    }

    [Fact]
    public void Long_stall_is_capped_at_quarter_second()
    {
        var w = TestWorlds.World();
        Assert.Equal(30, w.Advance(new ControlFrame { Gas = true }, 2.0));
    }

    [Fact]
    public void Early_exit_leftover_does_not_let_the_next_advance_exceed_30_steps()
    {
        var w = TestWorlds.World();
        Assert.Equal(1, w.Advance(ControlFrame.Empty, 0.25, () => true));   // stops after one step, ~29 steps of time unused
        int steps = w.Advance(ControlFrame.Empty, 0.25);
        Assert.True(steps <= 30, $"ran {steps} steps");
    }

    [Fact]
    public void Alpha_after_an_early_exit_is_within_0_1_and_equals_leftover_over_step()
    {
        var w = TestWorlds.World();
        w.Advance(ControlFrame.Empty, 2.5 * World.Step, () => true);   // one step run, 1.5 steps left unused
        Assert.InRange(w.Alpha, 0, 1);
        Assert.Equal(1.0, w.Alpha, 6);                                  // 1.5 steps leftover clamps to a full step
        var v = TestWorlds.World();
        v.Advance(ControlFrame.Empty, 1.5 * World.Step, () => true);   // one step run, 0.5 left
        Assert.Equal(0.5, v.Alpha, 6);
    }

    [Fact]
    public void Negative_frame_time_runs_no_steps()
    {
        Assert.Equal(0, TestWorlds.World().Advance(ControlFrame.Empty, -1));
    }

    [Fact]
    public void Same_seed_same_inputs_same_hash()
    {
        ulong Run()
        {
            var w = TestWorlds.World(seed: 11);
            for (int i = 0; i < 7200; i++)
                w.Advance(new ControlFrame { Gas = true, SteerDir = (i / 240) % 3 - 1 }, 1 / 120.0);
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Hash_changes_when_state_changes()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        ulong before = w.StateHash();
        w.Advance(new ControlFrame { Gas = true }, 0.25);
        Assert.NotEqual(before, w.StateHash());
    }

    /// <summary>
    /// A world with something of everything the hash covers: weapons below full and none cooling down, a boss with a
    /// missile rack, a Dropper with full droppers, and an oil slick.
    /// </summary>
    static (World W, BossCar Boss, AgentCar Dropper) HashWorld()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Loadout = new Loadout(new LoadoutDefinition(Oil: 4, Smoke: 1, Caltrops: 1, Missiles: 1, Strikes: 0));
        w.Player.Distance = 500;
        var def = new BossDefinition("Test", 100, [new(AgentKind.Gunner, 0.5), new(AgentKind.Pursuer, 0, FiresMissiles: true)]);
        var boss = BossCar.Create(w.NextId(), def, 700, 0, 30);
        w.Agents.Add(boss);
        var dropper = AgentCar.Create(w.NextId(), AgentKind.Dropper, 650, 2.5, 30);
        w.Agents.Add(dropper);
        w.Hazards.Add(new OilSlick { Distance = 600, Lateral = 1, Hostile = true });
        w.Hazards.Add(new SpeedTrap { Distance = 800, Lateral = 9, LimitMph = 70 });
        w.Hazards.Add(Roadblock.Place(900, 1));
        var suvA = AgentCar.Create(w.NextId(), AgentKind.ArmoredSuv, 560, -5, 30);
        var suvB = AgentCar.Create(w.NextId(), AgentKind.ArmoredSuv, 561, 5, 30);
        w.Agents.Add(suvA);
        w.Agents.Add(suvB);
        w.RollingRoadblockList.Add(new RollingRoadblock(suvA, suvB, 2));
        return (w, boss, dropper);
    }

    /// <summary>Each change touches one piece of state only (a use and a restock leave the charges as they were).</summary>
    public static TheoryData<string> HashedState() => new(HashChanges.Keys);

    static readonly Dictionary<string, Action<World, BossCar, AgentCar>> HashChanges = new()
    {
        ["hazard type"] = (w, _, _) => w.Hazards[0] = new Caltrops { Distance = 600, Lateral = 1, Hostile = true },
        ["hazard distance"] = (w, _, _) => w.Hazards[0].Distance += 0.5,
        ["hazard lateral"] = (w, _, _) => w.Hazards[0].Lateral += 0.5,
        ["boss phase"] = (_, b, _) => b.PhaseIndex = 1,
        ["boss shield"] = (_, b, _) => b.Shield = 0.5,
        ["boss slow"] = (_, b, _) => b.SlowLeft = 2,
        ["boss engaged"] = (_, b, _) => b.Engaged = !b.Engaged,
        ["boss missile rack cooldown"] = (_, b, _) => b.Arms.Missiles!.TryLaunch(),
        ["boss blinded"] = (_, b, _) => b.Blinded = 1,
        ["agent blinded"] = (_, _, d) => d.Blinded = 1,
        ["agent caltrops charges"] = (_, _, d) => { d.Arms.Caltrops!.TryUse(); d.Arms.Caltrops.Update(5); },
        ["agent oil cooldown"] = (_, _, d) => { d.Arms.Oil!.TryUse(); d.Arms.Oil.Restock(1); },
        ["player grip loss"] = (w, _, _) => w.Player.GripLoss = 1,
        ["player puncture"] = (w, _, _) => w.Player.PunctureLeft = 1,
        ["oil charges"] = (w, _, _) => w.Loadout.Oil.Restock(1),
        ["oil cooldown"] = (w, _, _) => { w.Loadout.Oil.TryUse(); w.Loadout.Oil.Restock(1); },
        ["oil recharge"] = (w, _, _) => w.Loadout.Oil.Update(1),
        ["smoke charges"] = (w, _, _) => w.Loadout.Smoke.Restock(1),
        ["smoke cooldown"] = (w, _, _) => { w.Loadout.Smoke.TryUse(); w.Loadout.Smoke.Restock(1); },
        ["caltrops charges"] = (w, _, _) => w.Loadout.Caltrops.Restock(1),
        ["caltrops cooldown"] = (w, _, _) => { w.Loadout.Caltrops.TryUse(); w.Loadout.Caltrops.Restock(1); },
        ["missile charges"] = (w, _, _) => w.Loadout.Missiles.Restock(1),
        ["missile cooldown"] = (w, _, _) => { w.Loadout.Missiles.TryUse(); w.Loadout.Missiles.Restock(1); },
        ["strike charges"] = (w, _, _) => w.Loadout.Strike.Restock(1),
        ["strike sweep"] = (w, _, _) => { w.Loadout.Strike.Restock(1); w.Loadout.Strike.TryBegin(); w.Loadout.Strike.Restock(1); },
        ["heat value"] = (w, _, _) => w.Heat.Set(30),
        ["heat quiet spell"] = (w, _, _) => w.Heat.Update(World.Step, false),
        ["busted timer"] = (w, _, _) => w.Busted.SetHeldForDiagnostics(1),
        ["busted armed"] = (w, _, _) => w.BustedArmed = true,
        ["busted pending"] = (w, _, _) => w.BustedPending = true,
        ["agent blame time"] = (_, _, d) => d.PlayerBlameUntil = 5,
        ["agent hit-heat cooldown"] = (_, _, d) => d.HitHeatCooldown = 1,
        ["speed trap triggered"] = (w, _, _) => ((SpeedTrap)w.Hazards[1]).Triggered = true,
        ["speed trap caught"] = (w, _, _) => ((SpeedTrap)w.Hazards[1]).Caught = true,
        ["roadblock gap lane"] = (w, _, _) => w.Hazards[2] = Roadblock.Place(900, 0),
        ["roadblock hit"] = (w, _, _) => ((Roadblock)w.Hazards[2]).HitPlayer = true,
        ["rolling roadblock formed"] = (w, _, _) => w.RollingRoadblockList[0].Form(0),
        ["rolling roadblock planned open"] = (w, _, _) =>
            w.RollingRoadblockList[0] = new RollingRoadblock(w.RollingRoadblockList[0].Cars[0], w.RollingRoadblockList[0].Cars[1], 0),
        ["rolling roadblock formed time"] = (w, _, _) => w.RollingRoadblockList[0].FormedSeconds = 1,
        ["rolling roadblock off-slot time"] = (w, _, _) => w.RollingRoadblockList[0].OffSlotSeconds = 1,
        ["rolling roadblock in-gap time"] = (w, _, _) => w.RollingRoadblockList[0].InGapSeconds = 1,
        ["rolling roadblock dropped"] = (w, _, _) => w.RollingRoadblockList.Clear(),
    };

    [Theory]
    [MemberData(nameof(HashedState))]
    public void Hash_covers_the_state_that_shapes_what_happens_next(string what)
    {
        var (w, boss, dropper) = HashWorld();
        ulong before = w.StateHash();
        Assert.Equal(before, HashWorld().W.StateHash());     // the same world hashes the same
        HashChanges[what](w, boss, dropper);
        Assert.NotEqual(before, w.StateHash());
    }


    /// <summary>Two heat meters at the same value and quiet-spell time; the hash must still tell them apart by level or by time in the level.</summary>
    static ulong HashOfHeat(Action<HeatMeter> script)
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        script(w.Heat);
        return w.StateHash();
    }

    [Fact]
    public void Hash_tells_two_levels_at_the_same_heat_apart()
    {
        // 47 is Watched when set outright, but Patrol when it fell from 55 (the level is held below its threshold).
        ulong set = HashOfHeat(h => h.Set(47));
        ulong fell = HashOfHeat(h => { h.Set(55); h.Add(-8); });
        Assert.NotEqual(set, fell);
    }

    [Fact]
    public void Hash_tells_how_long_the_level_has_been_held()
    {
        ulong early = HashOfHeat(h => { h.Update(5 * World.Step, false); h.Set(55); });
        ulong later = HashOfHeat(h => { h.Set(55); h.Update(5 * World.Step, false); });
        Assert.NotEqual(early, later);     // same value and quiet spell (5 steps), 0 and 5 steps in Patrol
    }

    [Fact]
    public void Hash_tells_which_lane_a_rolling_roadblock_leaves_open()
    {
        ulong Formed(double playerLateral)
        {
            var (w, _, _) = HashWorld();
            w.RollingRoadblockList[0].Form(playerLateral);
            return w.StateHash();
        }
        Assert.NotEqual(Formed(-5), Formed(5));     // the open lane locks to RIGHT, or to LEFT
    }
    [Fact]
    public void Hash_tells_a_sweep_part_way_from_one_just_begun()
    {
        var (a, _, _) = HashWorld();
        var (b, _, _) = HashWorld();
        foreach (var w in new[] { a, b }) { w.Loadout.Strike.Restock(1); w.Loadout.Strike.TryBegin(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Loadout.Strike.AdvanceSweep(0.5);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Selecting_lever_resets_target_to_current_speed()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 30;
        w.Handle(new GameAction(ActionKind.SelectScheme, 1));
        Assert.Equal(1, w.SchemeIndex);
        Assert.NotNull(w.Scheme.TargetSpeed);
        Assert.Equal(30, w.Scheme.TargetSpeed!.Value, 1);
    }

    [Fact]
    public void Select_scheme_value_is_clamped()
    {
        var w = TestWorlds.World();
        w.Handle(new GameAction(ActionKind.SelectScheme, 9));
        Assert.Equal(1, w.SchemeIndex);
        w.Handle(new GameAction(ActionKind.SelectScheme, -4));
        Assert.Equal(0, w.SchemeIndex);
    }

    [Fact]
    public void Pad_triggers_override_pedals_in_every_scheme()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Handle(new GameAction(ActionKind.SelectScheme, 1));
        for (int i = 0; i < 4; i++) w.Advance(new ControlFrame { PadConnected = true, PadGas = 1 }, 0.25);
        Assert.True(w.Player.Speed > 5);
    }

    [Fact]
    public void Next_id_counts_up_from_one_and_player_is_zero()
    {
        var w = TestWorlds.World();
        Assert.Equal(0, w.Player.Id);
        Assert.Equal(1, w.NextId());
        Assert.Equal(2, w.NextId());
    }

    [Fact]
    public void Events_clear_on_advance_and_messages_expire()
    {
        var w = TestWorlds.World();
        w.Publish(new Message(w.Time, "A", Tone.Info));
        w.Publish(new PlayerWrecked(w.Time, "x"));
        Assert.Equal(2, w.Events.Count);
        Assert.Single(w.RecentMessages);
        w.Advance(ControlFrame.Empty, 0.02);
        Assert.Empty(w.Events);
        Assert.Single(w.RecentMessages);
        for (int i = 0; i < 12; i++) w.Advance(ControlFrame.Empty, 0.25);
        Assert.Empty(w.RecentMessages);
    }

    [Fact]
    public void Recent_messages_keep_newest_four_first()
    {
        var w = TestWorlds.World();
        for (int i = 0; i < 6; i++) w.Publish(new Message(w.Time, $"m{i}", Tone.Good));
        Assert.Equal(4, w.RecentMessages.Count);
        Assert.Equal("m5", ((Message)w.RecentMessages[0]).Text);
        Assert.Equal("m2", ((Message)w.RecentMessages[3]).Text);
    }

    static World WorldWithAgents(int seed, Track? track = null)
    {
        var w = TestWorlds.World(seed: seed, track: track);
        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Runner, 150, -5, 35));
        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Blocker, 90, 5, 25));
        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Pursuer, -150, 0, 40));
        return w;
    }

    /// <summary>Feeds 60 s of the same per-frame inputs, each held for 1/60 s, in frames of the given length.</summary>
    static ulong RunWithFrames(int seed, double frame, Track? track = null)
    {
        var w = WorldWithAgents(seed, track);
        int framesPerInput = (int)Math.Round((1 / 60.0) / frame);
        for (int i = 0; i < 3600 * framesPerInput; i++)
        {
            int input = i / framesPerInput;
            w.Advance(new ControlFrame { Gas = true, SteerDir = (input / 120) % 3 - 1 }, frame);
        }
        return w.StateHash();
    }

    [Fact]
    public void Frame_length_does_not_change_the_result_with_agents_present()
    {
        Assert.Equal(RunWithFrames(11, 1 / 120.0), RunWithFrames(11, 1 / 60.0));
    }

    [Fact]
    public void Different_seeds_give_different_hashes_with_agents_present()
    {
        // Same track, different RNG seeds: only the agents' use of the RNG can make the hashes differ.
        var track = TestWorlds.Straight();
        Assert.NotEqual(RunWithFrames(11, 1 / 60.0, track), RunWithFrames(12, 1 / 60.0, track));
    }
}
