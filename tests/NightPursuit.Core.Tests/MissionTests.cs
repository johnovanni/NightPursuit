using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

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
    public void Bonus_and_look_default_when_absent()
    {
        Assert.Equal(1000, MissionLoader.Load(Valid, "m.json").Bonus);
        var def = MissionLoader.LoadFile(Mission01Path);
        Assert.Equal(1000, def.Bonus);
        Assert.Equal(BossLook.Fin, def.Boss!.Look);
    }

    const string BossJson = "\"boss\": { \"name\": \"B\", \"health\": 100, {LOOK}\"phases\": [ { \"behaviour\": \"Gunner\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }";

    static string WithBonusAndBoss(string bonus, string look) =>
        Valid.Replace("\"id\"", (bonus.Length > 0 ? $"\"bonus\": {bonus}, " : "") + BossJson.Replace("{LOOK}", look) + ", \"id\"");

    [Fact]
    public void Bonus_and_look_are_read()
    {
        var def = MissionLoader.Load(WithBonusAndBoss("2500", "\"look\": \"dome\", "), "m.json");
        Assert.Equal(2500, def.Bonus);
        Assert.Equal(BossLook.Dome, def.Boss!.Look);
    }

    [Theory]
    [InlineData("-1", "", "m.json: bonus must be a whole number from 0 to 100000")]
    [InlineData("100001", "", "m.json: bonus must be a whole number from 0 to 100000")]
    [InlineData("1.5", "", "m.json: bonus must be a whole number from 0 to 100000")]
    [InlineData("\"lots\"", "", "m.json: bonus must be a whole number from 0 to 100000")]
    [InlineData("", "\"look\": \"Round\", ", "m.json: boss.look must be Fin, Twin or Dome, not 'Round'")]
    [InlineData("", "\"look\": \"1\", ", "m.json: boss.look must be Fin, Twin or Dome, not '1'")]
    [InlineData("", "\"look\": 1, ", "m.json: boss.look must be Fin, Twin or Dome, not '1'")]
    public void Bad_bonus_and_look_are_rejected(string bonus, string look, string message)
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(WithBonusAndBoss(bonus, look), "m.json"));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Patrol_ground_loads()
    {
        var def = MissionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "content", "missions", "patrol-ground.json"));
        Assert.Equal("Patrol Ground", def.Title);
        Assert.Equal(33, def.Road.Seed);
        Assert.Equal(5000, def.ApproachLength);
        Assert.Equal(6, def.Traffic!.CarsPerKm);
        Assert.Equal(1.5, def.Police!.Presence);
        Assert.Equal(85, def.Police.StartingHeat);   // pre-flight F10: 85, so Pursuit lasts long enough for the chasers to arrive
        Assert.Equal("Night Heron", def.Boss!.Name);
        Assert.Equal(2, def.Boss.Phases.Count);
        Assert.Equal(2, def.Waves.Count);
        Assert.Equal(6, def.Crates!.Count);
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
    public void A_boss_cannot_be_a_wave()
    {
        var ex = Assert.Throws<MissionFormatException>(() =>
            MissionLoader.Load(Valid.Replace("\"Pursuer\"", "\"boss\""), "m.json"));
        Assert.Equal("m.json: waves[1].kind 'Boss' cannot be a wave (set the boss in \"boss\")", ex.Message);
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
    public void Restart_rebuilds_everything_when_the_run_is_over()
    {
        var run = Run(Def(6000, new WaveDefinition(0, AgentKind.Runner, 1, 400)));
        run.Update(new ControlFrame { Gas = true }, 0.25);
        Wreck(run);
        run.ForceStateForDiagnostics(RunState.GameOver);
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
    public void Respawn_keeps_supply_crates()
    {
        // A respawn never silently costs you supplies: crates stay where they are, however near.
        var run = Run();
        var near = new SupplyCrate(SupplyItem.Missile, 1) { Distance = 40, PrevDistance = 40, Lateral = 4 };
        var behind = new SupplyCrate(SupplyItem.Armor, 1) { Distance = -120, PrevDistance = -120, Lateral = -4 };
        run.World.Hazards.Add(near);
        run.World.Hazards.Add(behind);
        run.World.Hazards.Add(new OilSlick { Distance = 40, Lateral = 0 });
        Wreck(run);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(new Hazard[] { near, behind }, run.World.Hazards.ToArray());
    }

    [Fact]
    public void Respawn_clears_enemy_bullets_but_not_yours()
    {
        var run = Run();
        Wreck(run);
        while (run.StateTimer > 0.1) run.Update(ControlFrame.Empty, 1 / 60.0);
        // Bullets still in the air at the respawn, with range to spare.
        var p = run.World.Player;
        var hostile = new Tracer
        {
            Pose = new RoadPose(p.Distance + 30, 0, Math.PI), PrevDistance = p.Distance + 30, PrevHeading = Math.PI,
            Speed = 20, MuzzleSpeed = 20, Range = 1000, Hostile = true, OwnerId = 99,
        };
        var own = new Tracer
        {
            Pose = new RoadPose(p.Distance + 30, 0, 0), PrevDistance = p.Distance + 30,
            Speed = 20, MuzzleSpeed = 20, Range = 1000, OwnerId = 0,
        };
        run.World.Tracers.Add(hostile);
        run.World.Tracers.Add(own);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(new[] { own }, run.World.Tracers.ToArray());
    }

    [Fact]
    public void Respawn_clears_grip_loss_and_puncture()
    {
        // Both only count down while the car is not wrecked, so without a reset they would carry into the new car.
        var run = Run();
        run.World.Player.GripLoss = 2.5;
        run.World.Player.PunctureLeft = 6;
        Wreck(run);
        Assert.True(run.World.Player.GripLoss > 0 && run.World.Player.PunctureLeft > 0);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(0, run.World.Player.GripLoss);
        Assert.Equal(0, run.World.Player.PunctureLeft);
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

    // ---- determinism and restart rules

    static (ulong Hash, RunState State, double Time, int Score, int Spares) RunMission01(IEnumerable<double> frames, int steer = 0)
    {
        var run = new MissionRun(MissionLoader.LoadFile(Mission01Path), new FeelSettings(), 5);
        var input = new ControlFrame { Gas = true, Fire = true, SteerDir = steer };
        foreach (var dt in frames) run.Update(input, dt);
        return (run.World.StateHash(), run.State, run.World.Time, run.World.Score, run.World.Player.SpareCars);
    }

    [Fact]
    public void Mission_run_is_the_same_however_the_frames_are_split()
    {
        var at120 = RunMission01(Enumerable.Repeat(1 / 120.0, 120 * 60));
        var at60 = RunMission01(Enumerable.Repeat(1 / 60.0, 60 * 60));
        var uneven = RunMission01(Enumerable.Range(0, 120 * 60).Select(i => i % 2 == 0 ? 1 / 240.0 : 3 / 240.0));
        Assert.Equal(at120, at60);
        Assert.Equal(at120, uneven);
    }

    [Fact]
    public void Mission_run_with_wrecks_and_respawns_is_the_same_however_the_frames_are_split()
    {
        // Holding right drives into the rail, so the player is wrecked and respawns several times.
        var at120 = RunMission01(Enumerable.Repeat(1 / 120.0, 120 * 60), steer: 1);
        var at60 = RunMission01(Enumerable.Repeat(1 / 60.0, 60 * 60), steer: 1);
        var uneven = RunMission01(Enumerable.Range(0, 120 * 60).Select(i => i % 2 == 0 ? 1 / 240.0 : 3 / 240.0), steer: 1);
        Assert.True(at120.Spares < 3, "expected at least one wreck");
        Assert.Equal(at120, at60);
        Assert.Equal(at120, uneven);
    }

    [Fact]
    public void Restart_does_nothing_while_playing_except_say_how()
    {
        var run = Run();
        run.World.Player.Distance = 50;
        run.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(50, run.World.Player.Distance);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Contains(run.World.RecentMessages, m => m is Message { Text: "PRESS P TO PAUSE, THEN R TO RESTART" });
    }

    [Fact]
    public void Restart_during_a_respawn_says_to_wait_for_it()
    {
        var run = Run();
        run.World.Player.Armor = 0;
        run.Update(ControlFrame.Empty, 0.02);
        Assert.Equal(RunState.Respawning, run.State);
        run.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(RunState.Respawning, run.State);
        Assert.Contains(run.World.RecentMessages, m => m is Message { Text: "RESTART AFTER THE RESPAWN", Tone: Tone.Info });
    }

    [Fact]
    public void Restart_works_when_paused_or_finished()
    {
        var run = Run();
        run.World.Player.Distance = 50;
        run.Handle(new GameAction(ActionKind.Pause));
        run.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(0, run.World.Player.Distance);
        Assert.Equal(RunState.Playing, run.State);

        run.ForceStateForDiagnostics(RunState.GameOver);
        run.World.Player.Distance = 50;
        run.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(0, run.World.Player.Distance);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(10)]
    public void Road_min_radius_below_50_is_rejected(int radius)
    {
        var json = Valid.Replace("\"seed\": 7", $"\"seed\": 7, \"minRadius\": {radius}, \"maxRadius\": 400");
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(json, "m.json"));
        Assert.Equal("m.json: road.minRadius must be at least 50 (got " + radius + ")", ex.Message);
    }

    [Fact]
    public void Road_min_radius_of_50_is_accepted()
    {
        var json = Valid.Replace("\"seed\": 7", "\"seed\": 7, \"minRadius\": 50, \"maxRadius\": 400");
        Assert.Equal(50, MissionLoader.Load(json, "m.json").Road.MinRadius);
    }

    [Theory]
    [InlineData(29, 1)]       // too close ahead
    [InlineData(0, 1)]
    [InlineData(1401, 1)]     // too far ahead
    [InlineData(1300, 10)]    // last car of the line lands at 1660
    [InlineData(-29, 1)]      // too close behind
    [InlineData(-451, 1)]     // too far behind
    [InlineData(-400, 3)]     // last car of the line lands at -480
    public void Wave_spawn_window_outside_the_allowed_range_is_rejected(int offset, int count)
    {
        var json = Valid.Replace("\"count\": 2, \"spawnOffset\": 400", $"\"count\": {count}, \"spawnOffset\": {offset}");
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(json, "m.json"));
        Assert.StartsWith("m.json: waves[0].spawnOffset", ex.Message);
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(1400, 1)]
    [InlineData(1000, 10)]    // 1000 .. 1360
    [InlineData(-30, 1)]
    [InlineData(-450, 1)]
    [InlineData(-100, 9)]     // -100 .. -420
    public void Wave_spawn_window_on_the_edge_is_accepted(int offset, int count)
    {
        var json = Valid.Replace("\"count\": 2, \"spawnOffset\": 400", $"\"count\": {count}, \"spawnOffset\": {offset}");
        Assert.Equal(offset, MissionLoader.Load(json, "m.json").Waves[0].SpawnOffset);
    }

    // ---- traffic and police settings

    static string WithSection(string section) => Valid.TrimEnd().TrimEnd('}') + ", " + section + " }";

    const string NotEnemy = " is not an enemy type (Runner, Blocker, Pursuer, Gunner, Dropper, Armored)";

    [Theory]
    [InlineData("Civilian", "waves[0].kind 'Civilian'")]
    [InlineData("PolicePatrol", "waves[0].kind 'PolicePatrol'")]
    public void Only_enemy_types_can_be_waves(string kind, string expected)
    {
        var json = Valid.Replace("\"Runner\"", $"\"{kind}\"");
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(json, "m.json"));
        Assert.Equal("m.json: " + expected + NotEnemy, ex.Message);
    }

    [Fact]
    public void Only_enemy_types_can_be_boss_behaviours()
    {
        var boss = "\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Interceptor\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }";
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(WithSection(boss), "m.json"));
        Assert.Equal("m.json: boss.phases[0].behaviour 'Interceptor'" + NotEnemy, ex.Message);
    }

    [Theory]
    [InlineData("\"traffic\": { \"carsPerKm\": 13 }", "traffic.carsPerKm must be from 0 to 12 (got 13)")]
    [InlineData("\"traffic\": { \"carsPerKm\": -1 }", "traffic.carsPerKm must be from 0 to 12 (got -1)")]
    [InlineData("\"traffic\": { \"density\": 3 }", "traffic.density is not a known field (carsPerKm)")]
    [InlineData("\"traffic\": { }", "traffic.carsPerKm is missing")]
    [InlineData("\"police\": { \"presence\": 3 }", "police.presence must be from 0 to 2 (got 3)")]
    [InlineData("\"police\": { \"startingHeat\": -1 }", "police.startingHeat must be from 0 to 100 (got -1)")]
    [InlineData("\"police\": { \"startingHeat\": 101 }", "police.startingHeat must be from 0 to 100 (got 101)")]
    [InlineData("\"police\": { \"heat\": 5 }", "police.heat is not a known field (presence, startingHeat)")]
    [InlineData("\"police\": { \"presence\": 0, \"startingHeat\": 10 }", "police.startingHeat must be 0 when police.presence is 0")]
    public void Bad_traffic_and_police_name_the_field(string section, string expected)
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(WithSection(section), "m.json"));
        Assert.Equal("m.json: " + expected, ex.Message);
    }

    [Fact]
    public void Traffic_and_police_sections_load_with_their_defaults()
    {
        var none = MissionLoader.Load(Valid, "m.json");
        Assert.Null(none.Traffic);
        Assert.Null(none.Police);   // no police section: presence 0, no police

        var def = MissionLoader.Load(WithSection("\"traffic\": { \"carsPerKm\": 6.5 }, \"police\": { }"), "m.json");
        Assert.Equal(6.5, def.Traffic!.CarsPerKm);
        Assert.Equal(new PoliceDefinition(1, 0), def.Police);   // a section without presence: presence 1

        var full = MissionLoader.Load(WithSection("\"police\": { \"presence\": 2, \"startingHeat\": 55 }"), "m.json");
        Assert.Equal(new PoliceDefinition(2, 55), full.Police);

        var none0 = MissionLoader.Load(WithSection("\"police\": { \"presence\": 0, \"startingHeat\": 0 }"), "m.json");
        Assert.Equal(new PoliceDefinition(0, 0), none0.Police);   // presence 0 with no heat is fine
    }

    [Fact]
    public void Starting_heat_is_applied()
    {
        var run = Run(Def() with { Police = new PoliceDefinition(1, 55) });
        Assert.Equal(55, run.World.Heat.Value);
        Assert.Equal(NightPursuit.Core.Police.HeatLevel.Patrol, run.World.Heat.Level);
    }
}
