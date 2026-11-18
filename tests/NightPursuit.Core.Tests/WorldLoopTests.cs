using NightPursuit.Core.Control;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

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
