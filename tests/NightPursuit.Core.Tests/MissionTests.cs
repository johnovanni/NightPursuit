using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class MissionTests
{
    static string Mission01Path => Path.Combine(AppContext.BaseDirectory, "content", "missions", "mission-01.json");

    static MissionDefinition Def(double approach = 6000, params WaveDefinition[] waves) =>
        new("test", "Test", "Briefing", new TrackProfile(7), approach, 5, waves);

    static MissionRun Run(MissionDefinition? def = null) => new(def ?? Def(), new FeelSettings(), 3);

    const string Valid = """
        { "id": "m", "title": "T", "briefing": "B", "road": { "seed": 7 }, "approachLength": 1000, "startingOil": 5,
          "waves": [ { "atDistance": 100, "kind": "Runner", "count": 2, "spawnOffset": 400 },
                     { "atDistance": 300, "kind": "Pursuer", "count": 1, "spawnOffset": -250 } ] }
        """;

    // ---- loader

    [Fact]
    public void Mission_01_loads()
    {
        var def = MissionLoader.LoadFile(Mission01Path);
        Assert.Equal("Midnight Run", def.Title);
        Assert.Equal(7, def.Road.Seed);
        Assert.Equal(6000, def.ApproachLength);
        Assert.Equal(5, def.StartingOil);
        Assert.Equal(6, def.Waves.Count);
        Assert.True(def.Waves.Select(w => w.AtDistance).SequenceEqual(def.Waves.Select(w => w.AtDistance).OrderBy(d => d)));
    }

    [Fact]
    public void Valid_json_loads_with_road_defaults()
    {
        var def = MissionLoader.Load(Valid, "m.json");
        Assert.Equal(180, def.Road.MinRadius);
        Assert.Equal(AgentKind.Pursuer, def.Waves[1].Kind);
        Assert.Equal(-250, def.Waves[1].SpawnOffset);
    }

    [Fact]
    public void Unknown_kind_names_source_and_field()
    {
        var ex = Assert.Throws<MissionFormatException>(() =>
            MissionLoader.Load(Valid.Replace("\"Pursuer\"", "\"Tank\""), "mission-01.json"));
        Assert.Equal("mission-01.json: waves[1].kind 'Tank' is not a known enemy type", ex.Message);
    }

    [Fact]
    public void Waves_out_of_order_are_rejected()
    {
        var json = Valid.Replace("\"atDistance\": 300", "\"atDistance\": 50");
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(json, "m.json"));
        Assert.StartsWith("m.json: waves[1].atDistance", ex.Message);
    }

    [Fact]
    public void Non_positive_approach_length_is_rejected()
    {
        var ex = Assert.Throws<MissionFormatException>(() =>
            MissionLoader.Load(Valid.Replace("\"approachLength\": 1000", "\"approachLength\": 0"), "m.json"));
        Assert.Equal("m.json: approachLength must be greater than 0", ex.Message);
    }

    [Theory]
    [InlineData("\"count\": 2", "\"count\": 0", "waves[0].count")]
    [InlineData("\"count\": 2", "\"count\": 11", "waves[0].count")]
    [InlineData("\"startingOil\": 5", "\"startingOil\": 6", "startingOil")]
    [InlineData("\"startingOil\": 5", "\"startingOil\": -1", "startingOil")]
    [InlineData("\"title\": \"T\",", "", "title")]
    [InlineData("\"seed\": 7", "\"x\": 1", "road.seed")]
    [InlineData("\"kind\": \"Runner\"", "\"kind\": 7", "waves[0].kind")]
    public void Bad_fields_name_the_field(string find, string replace, string field)
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(Valid.Replace(find, replace), "m.json"));
        Assert.StartsWith("m.json: " + field, ex.Message);
    }

    [Fact]
    public void Broken_json_and_missing_file_become_format_exceptions()
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load("{ nope", "m.json"));
        Assert.StartsWith("m.json:", ex.Message);
        var missing = Assert.Throws<MissionFormatException>(() => MissionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "nothing-here.json")));
        Assert.StartsWith("nothing-here.json:", missing.Message);
    }

    // ---- director

    [Fact]
    public void A_wave_spawns_exactly_once_ahead_in_lanes()
    {
        var run = Run(Def(6000, new WaveDefinition(100, AgentKind.Blocker, 3, 400)));
        run.World.Player.Distance = 99;
        run.Director.Update(run.World);
        Assert.Empty(run.World.Agents);

        run.World.Player.Distance = 101;
        run.Director.Update(run.World);
        run.Director.Update(run.World);
        Assert.Equal(3, run.World.Agents.Count);
        Assert.All(run.World.Agents, a => Assert.Contains(a.Lateral, Track.LaneCentres));
        Assert.Equal(new[] { 501.0, 541.0, 581.0 }, run.World.Agents.Select(a => a.Distance).ToArray());
        Assert.Equal(3, run.World.Agents.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Pursuers_spawn_behind_at_player_speed_plus_ten()
    {
        var run = Run(Def(6000, new WaveDefinition(0, AgentKind.Pursuer, 2, -250)));
        run.World.Player.Speed = 30;
        run.Director.Update(run.World);
        Assert.Equal(new[] { -250.0, -290.0 }, run.World.Agents.Select(a => a.Distance).ToArray());
        Assert.All(run.World.Agents, a => Assert.Equal(40, a.Speed));
    }

    [Fact]
    public void Reaching_approach_length_completes_the_run()
    {
        var run = Run(Def(500));
        Assert.False(run.Director.Completed);
        Assert.Equal(500, run.Director.DistanceRemaining(run.World));
        run.World.Player.Distance = 500;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Completed, run.State);
        Assert.True(run.Director.Completed);
        Assert.Equal(0, run.Director.DistanceRemaining(run.World));
    }

    // ---- run

    static void Wreck(MissionRun run)
    {
        run.World.Player.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
    }

    static void WaitForRespawn(MissionRun run)
    {
        for (int i = 0; i < 20 && run.State == RunState.Respawning; i++) run.Update(ControlFrame.Empty, 0.25);
    }

    [Fact]
    public void Wreck_with_spares_respawns_after_three_seconds()
    {
        var run = Run();
        run.World.Player.Speed = 30;
        Wreck(run);
        Assert.Equal(RunState.Respawning, run.State);
        Assert.Equal(3, run.World.Player.SpareCars);
        run.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(RunState.Respawning, run.State);

        WaitForRespawn(run);
        var p = run.World.Player;
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(2, p.SpareCars);
        Assert.Equal(100, p.Armor);
        Assert.Equal(0, p.Speed);
        Assert.Equal(0, p.Lateral);
        Assert.Equal(VehicleState.Driving, p.State);
    }

    [Fact]
    public void The_fourth_wreck_is_game_over()
    {
        var run = Run();
        for (int i = 0; i < 3; i++)
        {
            Wreck(run);
            Assert.Equal(RunState.Respawning, run.State);
            WaitForRespawn(run);
            Assert.Equal(RunState.Playing, run.State);
        }
        Assert.Equal(0, run.World.Player.SpareCars);
        Wreck(run);
        Assert.Equal(RunState.GameOver, run.State);
        double t = run.World.Time;
        run.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(t, run.World.Time);
    }

    [Fact]
    public void Respawn_removes_agents_within_200_m_only()
    {
        var run = Run();
        foreach (var d in new[] { 50.0, -150.0, 600.0 })
            run.World.Agents.Add(new AgentCar(AgentKind.Blocker, VehicleStats.Blocker)
            {
                Id = run.World.NextId(), Distance = d, Lateral = 5, Driver = new StillDriver(), Armor = 3,
            });
        Wreck(run);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(new[] { 600.0 }, run.World.Agents.Select(a => a.Distance).ToArray());
    }

    [Fact]
    public void Pause_toggles_and_freezes_the_world()
    {
        var run = Run();
        run.Update(new ControlFrame { Gas = true }, 0.1);
        run.Handle(new GameAction(ActionKind.Pause));
        Assert.Equal(RunState.Paused, run.State);
        double t = run.World.Time;
        run.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(t, run.World.Time);
        run.Handle(new GameAction(ActionKind.Pause));
        Assert.Equal(RunState.Playing, run.State);
    }

    [Fact]
    public void Pause_does_nothing_while_respawning()
    {
        var run = Run();
        Wreck(run);
        run.Handle(new GameAction(ActionKind.Pause));
        Assert.Equal(RunState.Respawning, run.State);
    }

    [Fact]
    public void Restart_rebuilds_everything_in_any_state()
    {
        var run = Run(Def(6000, new WaveDefinition(0, AgentKind.Runner, 1, 400)));
        run.Update(new ControlFrame { Gas = true }, 0.25);
        Wreck(run);
        var oldWorld = run.World;
        run.Handle(new GameAction(ActionKind.Restart));
        Assert.NotSame(oldWorld, run.World);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(0, run.World.Time);
        Assert.Equal(3, run.World.Player.SpareCars);
        Assert.Empty(run.World.Agents);
    }

    [Fact]
    public void Starting_oil_sets_the_oil_charges()
    {
        var def = Def() with { StartingOil = 2 };
        Assert.Equal(2, Run(def).World.Loadout.Oil.Charges);
    }

    [Fact]
    public void Other_actions_reach_the_world_only_while_playing()
    {
        var run = Run();
        run.Handle(new GameAction(ActionKind.SelectScheme, 1));
        Assert.Equal(1, run.World.SchemeIndex);
        run.Handle(new GameAction(ActionKind.Pause));
        run.Handle(new GameAction(ActionKind.SelectScheme, 0));
        Assert.Equal(1, run.World.SchemeIndex);
    }

    [Fact]
    public void Crossing_the_finish_while_wrecking_completes_and_keeps_the_spare()
    {
        var run = Run(Def(500));
        run.World.Player.Distance = 500;
        run.World.Player.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(3, run.World.Player.SpareCars);
    }

    [Fact]
    public void Respawn_removes_hazards_within_200_m_only()
    {
        var run = Run();
        foreach (var d in new[] { 40.0, -120.0, 600.0 })
            run.World.Hazards.Add(new OilSlick { Distance = d, Lateral = 0 });
        Wreck(run);
        WaitForRespawn(run);
        Assert.Equal(new[] { 600.0 }, run.World.Hazards.Select(h => h.Distance).ToArray());
    }

    [Fact]
    public void Respawn_resets_steering_and_throttle_scheme()
    {
        var run = Run();
        run.Handle(new GameAction(ActionKind.SelectScheme, 1));
        run.Update(new ControlFrame { Gas = true, SteerDir = 1 }, 0.25);
        run.World.Player.Speed = 30;
        run.Handle(new GameAction(ActionKind.LeverPreset, 3));
        Assert.NotEqual(0, run.World.Steering.Value);
        Wreck(run);
        Assert.NotEqual(0, run.World.Steering.Value);
        WaitForRespawn(run);
        Assert.Equal(0, run.World.Steering.Value);
        Assert.Equal(0, run.World.Scheme.TargetSpeed!.Value);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(20001)]
    [InlineData(2000000000)]
    public void Road_segments_out_of_range_are_rejected(int segments)
    {
        var json = Valid.Replace("\"seed\": 7", $"\"seed\": 7, \"segments\": {segments}");
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(json, "m.json"));
        Assert.StartsWith("m.json: road.segments", ex.Message);
    }

    [Fact]
    public void Null_json_is_a_format_exception()
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(null!, "m.json"));
        Assert.StartsWith("m.json:", ex.Message);
    }
}
