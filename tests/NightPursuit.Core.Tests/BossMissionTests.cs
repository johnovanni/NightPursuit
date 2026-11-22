using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class BossMissionTests
{
    static string MissionPath(string file) => Path.Combine(AppContext.BaseDirectory, "content", "missions", file);

    static BossDefinition Two => new("Test", 100, [new(AgentKind.Blocker, 0.5), new(AgentKind.Pursuer, 0)]);

    static MissionDefinition Def(double approach, BossDefinition? boss, params WaveDefinition[] waves) =>
        new("test", "Test", "Briefing", new TrackProfile(7), approach, 5, waves,
            StartingLoadout: new LoadoutDefinition(5, 2, 2, 2, 1), Boss: boss);

    static MissionRun Run(MissionDefinition def) => new(def, new FeelSettings(), 3);

    /// <summary>A run whose boss has just appeared: the player at 500 m doing 40 m/s.</summary>
    static (MissionRun Run, BossCar Boss) RunAtBoss()
    {
        var run = Run(Def(500, Two));
        run.World.Player.Distance = 500;
        run.World.Player.Speed = 40;
        run.Director.Update(run.World);
        return (run, run.Director.Boss!);
    }

    static void WaitForRespawn(MissionRun run)
    {
        for (int i = 0; i < 20 && run.State == RunState.Respawning; i++) run.Update(ControlFrame.Empty, 0.25);
    }

    // ---- the mission files

    [Fact]
    public void Mission_01_has_a_three_phase_boss_and_a_loadout()
    {
        var def = MissionLoader.LoadFile(MissionPath("mission-01.json"));
        var boss = def.Boss!;
        Assert.Equal("Black Heron", boss.Name);
        Assert.Equal(180, boss.Health);
        Assert.Equal(
            new[] { (AgentKind.Blocker, 0.6), (AgentKind.Pursuer, 0.25), (AgentKind.Runner, 0.0) },
            boss.Phases.Select(p => (p.Behaviour, p.EndsBelow)).ToArray());
        Assert.All(boss.Phases, p => Assert.False(p.FiresMissiles));
        Assert.Equal(new LoadoutDefinition(5, 1, 1, 1, 0), def.Loadout);
        Assert.Equal(
            new[]
            {
                new CrateDefinition(1500, 1, SupplyItem.Missile, 1), new CrateDefinition(2600, -1, SupplyItem.Smoke, 1),
                new CrateDefinition(3800, 0, SupplyItem.Caltrops, 1), new CrateDefinition(5000, 1, SupplyItem.Armor, 1),
            },
            def.Crates!.ToArray());
    }

    [Fact]
    public void Proving_ground_loads_with_every_enemy_type_and_crate_item()
    {
        var def = MissionLoader.LoadFile(MissionPath("proving-ground.json"));
        Assert.Equal("proving-ground", def.Id);
        Assert.Equal("Proving Ground", def.Title);
        Assert.Equal(21, def.Road.Seed);
        Assert.Equal(4500, def.ApproachLength);
        Assert.Equal(new LoadoutDefinition(5, 3, 3, 2, 1), def.Loadout);
        Assert.Equal(8, def.Waves.Count);
        Assert.Equal(6, def.Waves.Select(w => w.Kind).Distinct().Count());
        Assert.Equal(Enum.GetValues<SupplyItem>().OrderBy(i => i), def.Crates!.Select(c => c.Item).Distinct().OrderBy(i => i));
        Assert.Equal(7, def.Crates!.Count);
        var boss = def.Boss!;
        Assert.Equal("Glass Wasp", boss.Name);
        Assert.Equal(160, boss.Health);
        Assert.Equal(
            new[] { (AgentKind.Gunner, 0.66, false), (AgentKind.Dropper, 0.33, false), (AgentKind.Pursuer, 0.0, true) },
            boss.Phases.Select(p => (p.Behaviour, p.EndsBelow, p.FiresMissiles)).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(def.Briefing));
    }

    // ---- the loader

    const string Head = """{ "id": "m", "title": "T", "briefing": "B", "road": { "seed": 7 }, "approachLength": 6000, """;
    const string GoodBoss = """
        "boss": { "name": "Test", "health": 100, "phases": [ { "behaviour": "Blocker", "endsBelow": 0.5 }, { "behaviour": "pursuer", "endsBelow": 0, "missiles": true } ] }
        """;
    const string GoodWaves = """
        "waves": [ { "atDistance": 100, "kind": "Runner", "count": 1, "spawnOffset": 400 } ]
        """;

    static string Json(string boss = GoodBoss, string waves = GoodWaves) => Head + boss + ", " + waves + " }";

    /// <summary>A "waves" section replaces the good waves; any other section replaces the good boss.</summary>
    static string With(string section) => section.StartsWith("\"waves\"") ? Json(waves: section) : Json(boss: section);

    static string Phases(string phases) => "\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ " + phases + " ] }";

    static string Phase(string behaviour, double endsBelow) => $"{{ \"behaviour\": \"{behaviour}\", \"endsBelow\": {endsBelow} }}";

    [Fact]
    public void Boss_section_loads_and_a_mission_without_one_has_none()
    {
        var def = MissionLoader.Load(Json(), "m.json");
        Assert.Equal(new BossDefinition("Test", 100, def.Boss!.Phases), def.Boss);
        Assert.Equal(new BossPhaseDefinition(AgentKind.Blocker, 0.5, false), def.Boss.Phases[0]);
        Assert.Equal(new BossPhaseDefinition(AgentKind.Pursuer, 0, true), def.Boss.Phases[1]);

        var none = MissionLoader.Load(Head + GoodWaves + " }", "m.json");
        Assert.Null(none.Boss);
    }

    [Theory]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.phases must have 2 or 3 phases (got 1)")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Blocker\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0.6 }, { \"behaviour\": \"Runner\", \"endsBelow\": 0 } ] }",
        "boss.phases[1].endsBelow must be below boss.phases[0].endsBelow")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Gunner\", \"endsBelow\": 0.66 }, { \"behaviour\": \"Dropper\", \"endsBelow\": 0.33 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0.1 } ] }",
        "boss.phases[2].endsBelow must be 0 for the last phase")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Boss\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.phases[0].behaviour 'Boss' is not a behaviour")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 5, \"phases\": [ { \"behaviour\": \"Blocker\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.health must be from 20 to 1000 (got 5)")]
    [InlineData("\"waves\": [ { \"atDistance\": 100, \"kind\": \"Boss\", \"count\": 1, \"spawnOffset\": 400 } ]",
        "waves[0].kind 'Boss' cannot be a wave (set the boss in \"boss\")")]
    [InlineData("\"waves\": [ { \"atDistance\": 100, \"kind\": \"Runner\", \"count\": 1, \"spawnOffset\": 400 }, { \"atDistance\": 200, \"kind\": \"Runner\", \"count\": 1, \"spawnOffset\": 400 }, { \"atDistance\": 300, \"kind\": \"Runner\", \"count\": 1, \"spawnOffset\": 400 }, { \"atDistance\": 6500, \"kind\": \"Runner\", \"count\": 1, \"spawnOffset\": 400 } ]",
        "waves[3].atDistance 6500 must be less than approachLength 6000")]
    public void Boss_and_wave_errors_name_the_field(string section, string message)
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(With(section), "m.json"));
        Assert.Contains("m.json: " + message, ex.Message);
    }

    [Fact]
    public void Phases_must_end_in_strictly_descending_order()
    {
        // Equal thresholds are not descending either.
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(
            With(Phases(Phase("Gunner", 0.5) + ", " + Phase("Dropper", 0.5) + ", " + Phase("Pursuer", 0))), "m.json"));
        Assert.Equal("m.json: boss.phases[1].endsBelow must be below boss.phases[0].endsBelow", ex.Message);
    }

    [Fact]
    public void Every_phase_but_the_last_must_end_above_zero()
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(
            With(Phases(Phase("Gunner", 0) + ", " + Phase("Pursuer", 0))), "m.json"));
        Assert.Equal("m.json: boss.phases[0].endsBelow must be above 0 (only the last phase ends at 0)", ex.Message);
    }

    [Fact]
    public void The_last_phase_must_end_at_zero()
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(
            With(Phases(Phase("Gunner", 0.5) + ", " + Phase("Pursuer", 0.2))), "m.json"));
        Assert.Equal("m.json: boss.phases[1].endsBelow must be 0 for the last phase", ex.Message);
    }

    [Theory]
    [InlineData("Boss", 0)]
    [InlineData("boss", 1)]
    public void No_phase_may_drive_as_a_boss(string behaviour, int index)
    {
        string phases = index == 0
            ? Phase(behaviour, 0.5) + ", " + Phase("Pursuer", 0)
            : Phase("Gunner", 0.5) + ", " + Phase(behaviour, 0);
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(With(Phases(phases)), "m.json"));
        Assert.Equal($"m.json: boss.phases[{index}].behaviour 'Boss' is not a behaviour", ex.Message);
    }

    [Theory]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 1001, \"phases\": [] }", "boss.health must be from 20 to 1000 (got 1001)")]
    [InlineData("\"boss\": { \"name\": \"\", \"health\": 100, \"phases\": [] }", "boss.name must not be empty")]
    [InlineData("\"boss\": { \"health\": 100, \"phases\": [] }", "boss.name is missing")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phase\": [] }", "boss.phase is not a known field (name, health, phases, look)")]
    [InlineData("\"boss\": 7", "boss must be an object")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": 3 }", "boss.phases must be an array")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ {}, {}, {}, {} ] }", "boss.phases must have 2 or 3 phases (got 4)")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Tank\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.phases[0].behaviour 'Tank' is not a known enemy type")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Gunner\", \"endsBelow\": 1 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.phases[0].endsBelow must be at least 0 and below 1 (got 1)")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Gunner\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": -0.1 } ] }",
        "boss.phases[1].endsBelow must be at least 0 and below 1 (got -0.1)")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Gunner\", \"endsBelow\": 0.5, \"missiles\": 1 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.phases[0].missiles must be true or false")]
    [InlineData("\"boss\": { \"name\": \"Test\", \"health\": 100, \"phases\": [ { \"behaviour\": \"Gunner\", \"endsBelow\": 0.5, \"missile\": true }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }",
        "boss.phases[0].missile is not a known field (behaviour, endsBelow, missiles)")]
    [InlineData("\"waves\": [ { \"atDistance\": 6000, \"kind\": \"Runner\", \"count\": 1, \"spawnOffset\": 400 } ]",
        "waves[0].atDistance 6000 must be less than approachLength 6000")]
    public void Other_boss_and_wave_errors_name_the_field(string section, string message)
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(With(section), "m.json"));
        Assert.Equal("m.json: " + message, ex.Message);
    }

    // ---- the director

    [Fact]
    public void Boss_appears_300_m_ahead_when_the_approach_ends()
    {
        var run = Run(Def(500, Two));
        Assert.Equal(MissionStage.Approach, run.Director.Stage);
        Assert.Null(run.Director.Boss);
        run.World.Player.Distance = 499.9;
        run.World.Player.Speed = 40;
        run.Director.Update(run.World);
        Assert.Null(run.Director.Boss);

        run.World.Player.Distance = 500;
        run.Director.Update(run.World);
        var boss = run.Director.Boss!;
        Assert.Equal(MissionStage.Boss, run.Director.Stage);
        Assert.Contains(boss, run.World.Agents);
        Assert.Equal(800, boss.Distance);
        Assert.Equal(800, boss.PrevDistance);
        Assert.Equal(0, boss.Lateral);
        Assert.Equal(30, boss.Speed);
        Assert.False(boss.Engaged);
        Assert.Equal(0, boss.PhaseIndex);
        Assert.Single(run.World.Events.OfType<BossAppeared>(), e => e.Name == "Test");
        Assert.Contains(run.World.Events, e => e is Message { Text: "BOSS AHEAD: TEST", Tone: Tone.Bad });
        Assert.Equal(0, run.Director.DistanceRemaining(run.World));
        Assert.False(run.Director.Completed);

        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(0, run.Director.DistanceRemaining(run.World));
    }

    [Fact]
    public void A_stopped_player_gets_a_stopped_boss()
    {
        var run = Run(Def(500, Two));
        run.World.Player.Distance = 500;
        run.World.Player.Speed = 4;
        run.Director.Update(run.World);
        Assert.Equal(0, run.Director.Boss!.Speed);
    }

    [Fact]
    public void No_waves_spawn_after_the_boss_appears()
    {
        // The loader would reject a wave past the approach; the director must still ignore it.
        var run = Run(Def(500, Two, new WaveDefinition(600, AgentKind.Runner, 1, 400)));
        for (double d = 0; d <= 700; d += 10)
        {
            run.World.Player.Distance = d;
            run.Director.Update(run.World);
        }
        // No Advance ran, so every event published on the way is still in Events.
        Assert.Single(run.World.Events.OfType<BossAppeared>());
        var only = Assert.Single(run.World.Agents);
        Assert.Same(run.Director.Boss, only);
        Assert.Equal(MissionStage.Boss, run.Director.Stage);
    }

    [Fact]
    public void Destroying_the_boss_completes_the_mission()
    {
        var (run, boss) = RunAtBoss();
        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Playing, run.State);

        run.World.Destroy(boss, KillCause.Missile);
        Assert.Contains(run.World.Events, e => e is Message { Text: "TEST DESTROYED  +5000", Tone: Tone.Good });
        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Completed, run.State);
        Assert.True(run.Director.Completed);
    }

    [Fact]
    public void The_boss_kill_message_uses_its_name_and_other_kills_their_kind()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        var boss = BossCar.Create(w.NextId(), new BossDefinition("Black Heron", 180, Two.Phases), 100, 0, 30);
        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, 200, 0, 30);
        w.Agents.Add(boss);
        w.Agents.Add(runner);
        w.Destroy(runner, KillCause.Gunfire);   // first: once the boss is beaten, nothing scores (the finale)
        w.Destroy(boss, KillCause.Gunfire);
        Assert.Contains(w.Events, e => e is Message { Text: "BLACK HERON DESTROYED  +5000" });
        Assert.Contains(w.Events, e => e is Message m && m.Text == "RUNNER DESTROYED  +" + VehicleStats.Runner.Points);
    }

    // ---- the run

    [Theory]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(-100)]     // behind, inside the clear radius
    public void Respawn_puts_the_boss_300_m_ahead_with_its_health_and_phase(double ahead)
    {
        var (run, boss) = RunAtBoss();
        var p = run.World.Player;
        boss.Distance = p.Distance + ahead;
        boss.Lateral = 3;
        boss.SnapPrev();
        boss.Engaged = true;
        boss.Armor = 40;
        boss.PhaseIndex = 1;
        boss.PassLane = 4;
        boss.SlowLeft = 20;     // still slowed at respawn: it must not re-engage itself from 300 m

        p.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Respawning, run.State);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);

        Assert.Contains(boss, run.World.Agents);
        Assert.Same(boss, run.Director.Boss);
        Assert.Equal(40, boss.Armor);
        Assert.Equal(1, boss.PhaseIndex);
        Assert.Equal(p.Distance + 300, boss.Distance, 1e-9);
        Assert.Equal(boss.Distance, boss.PrevDistance);
        Assert.Equal(0, boss.Lateral);
        Assert.Equal(0, boss.PrevLateral);
        Assert.Equal(0, boss.Speed);
        Assert.False(boss.Engaged);
        Assert.Null(boss.DriverState);
        Assert.Null(boss.PassLane);
        Assert.Equal(0, boss.SlowLeft);
        Assert.Equal(VehicleState.Driving, boss.State);

        // It eases in: still waiting a second later, with the player stopped.
        run.Update(ControlFrame.Empty, 0.25);
        Assert.False(boss.Engaged);
    }

    [Fact]
    public void Returning_the_boss_keeps_health_phase_and_shield_and_ends_a_spin()
    {
        var b = BossCar.Create(1, Two, 100, 2, 40);
        b.Armor = 40;
        b.PhaseIndex = 1;
        b.Shield = 1.25;
        b.Engaged = true;
        b.State = VehicleState.Spinning;
        b.StateTimer = 1;
        b.SpinAngle = 2;
        b.SpinRate = 7;
        b.Heading = 0.3;
        b.PushVel = 5;
        b.LateralVel = 5;
        b.DriverState = new object();
        b.ArrivalAvoid.Active = true;
        b.PassAvoid.Active = true;
        b.AheadMemory.Active = true;

        b.ReturnTo(900, 12);

        Assert.Equal((40.0, 1, 1.25), (b.Armor, b.PhaseIndex, b.Shield));
        Assert.Equal((900.0, 0.0, 12.0), (b.Distance, b.Lateral, b.Speed));
        Assert.Equal((900.0, 0.0, 0.0, 0.0), (b.PrevDistance, b.PrevLateral, b.PrevHeading, b.PrevSpinAngle));
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.Equal((0.0, 0.0, 0.0, 0.0, 0.0, 0.0), (b.StateTimer, b.SpinAngle, b.SpinRate, b.Heading, b.PushVel, b.LateralVel));
        Assert.False(b.Engaged);
        Assert.Null(b.DriverState);
        Assert.False(b.ArrivalAvoid.Active);
        Assert.False(b.PassAvoid.Active);
        Assert.False(b.AheadMemory.Active);
    }

    [Fact]
    public void Respawn_clears_hazards_where_the_boss_returns()
    {
        // The boss stage starts near the start of the road, so the respawn clamps at 0 and the main cleared zone is
        // [-200, 260]. The boss returns 300 m ahead of the new spot, outside that zone: only the boss-return clause
        // (20 m round where it lands) can clear the wreck there.
        var run = Run(Def(50, Two));
        var p = run.World.Player;
        p.Distance = 50;
        p.Speed = 40;
        run.Director.Update(run.World);
        var boss = run.Director.Boss!;
        p.Distance = 60;
        p.Speed = 0;                     // the wreck stays where it is: the crash point is 60, so the respawn is at 0
        boss.Distance = p.Distance + 500;
        boss.Speed = 0;
        boss.SnapPrev();
        var wreck = new NightPursuit.Core.Hazards.Wreck
        {
            Distance = 300, PrevDistance = 300, Lateral = 0, Width = 2.4, Length = 5.6,
        };
        run.World.Hazards.Add(wreck);

        p.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(0, p.Distance);
        Assert.Equal(300, boss.Distance, 1e-9);
        Assert.DoesNotContain(wreck, run.World.Hazards);

        double armor = boss.Armor;
        for (int i = 0; i < 4; i++) run.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(armor, boss.Armor);
        Assert.Equal(100, boss.Armor);
    }

    [Fact]
    public void Crossing_back_over_the_boss_line_keeps_the_boss_stage()
    {
        var run = Run(Def(1000, Two));
        var p = run.World.Player;
        p.Distance = 1000;
        p.Speed = 40;
        run.Director.Update(run.World);
        Assert.Equal(MissionStage.Boss, run.Director.Stage);
        p.Distance = 1050;
        p.Speed = 0;
        p.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Respawning, run.State);
        while (run.State == RunState.Respawning)
        {
            Assert.Equal(MissionStage.Boss, run.Director.Stage);
            run.Update(ControlFrame.Empty, 1 / 60.0);
        }
        Assert.Equal(900, p.Distance, 1e-3);
        for (int i = 0; i < 10; i++)
        {
            run.Update(ControlFrame.Empty, 1 / 60.0);
            Assert.Equal(MissionStage.Boss, run.Director.Stage);
        }
        var bosses = run.World.Agents.OfType<BossCar>().ToList();
        Assert.Single(bosses);
        Assert.Equal(p.Distance + 300, bosses[0].Distance, 1);
    }

    [Fact]
    public void A_boss_wrecked_at_the_respawn_stays_put_and_the_mission_completes()
    {
        var (run, boss) = RunAtBoss();
        run.World.Player.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Respawning, run.State);
        while (run.StateTimer > 1.0) run.Update(ControlFrame.Empty, 1 / 60.0);

        // Burnt out (not a sliding wreck), so it is still in the list for 1.5 s, past the respawn.
        boss.Speed = 0;
        boss.PushVel = 0;
        run.World.Destroy(boss, KillCause.Rammed);
        double at = boss.Distance;
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Contains(boss, run.World.Agents);
        Assert.Equal(VehicleState.Wrecked, boss.State);
        Assert.Equal(at, boss.Distance);

        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Completed, run.State);
    }

    [Fact]
    public void Respawn_still_clears_ordinary_cars_near_the_player()
    {
        var (run, boss) = RunAtBoss();
        var p = run.World.Player;
        var near = AgentCar.Create(run.World.NextId(), AgentKind.Blocker, p.Distance + 60, 4.5, 0);
        near.Driver = new StillDriver();
        run.World.Agents.Add(near);
        p.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        WaitForRespawn(run);
        Assert.DoesNotContain(near, run.World.Agents);
        Assert.Contains(boss, run.World.Agents);
    }

    [Fact]
    public void Respawn_makes_the_boss_forget_the_hazards_it_clears()
    {
        var (run, boss) = RunAtBoss();
        var p = run.World.Player;
        p.Speed = 0;
        var cleared = new NightPursuit.Core.Hazards.OilSlick { Distance = p.Distance + 50, Lateral = -5, Hostile = true };
        var kept = new NightPursuit.Core.Hazards.OilSlick { Distance = p.Distance + 900, Lateral = -5, Hostile = true };
        run.World.Hazards.Add(cleared);
        run.World.Hazards.Add(kept);
        boss.HazardsMet.Add(cleared);
        boss.HazardsMet.Add(kept);
        p.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        WaitForRespawn(run);
        Assert.Equal(RunState.Playing, run.State);
        Assert.DoesNotContain(cleared, run.World.Hazards);
        Assert.DoesNotContain(cleared, boss.HazardsMet);
        Assert.Contains(kept, run.World.Hazards);
        Assert.Contains(kept, boss.HazardsMet);
    }

    [Fact]
    public void Weapon_keys_do_nothing_while_paused_or_respawning()
    {
        var run = Run(Def(6000, null));
        var w = run.World;
        var target = AgentCar.Create(w.NextId(), AgentKind.Blocker, 120, 0, 0);
        target.Driver = new StillDriver();
        w.Agents.Add(target);

        void PressAll()
        {
            foreach (var k in new[] { ActionKind.FireMissile, ActionKind.FireStrike, ActionKind.DropRear, ActionKind.CycleRear })
                run.Handle(new GameAction(k));
        }

        (int, int, int, int, int, RearWeapon, int, int, bool) Snapshot()
        {
            var l = w.Loadout;
            return (l.Oil.Charges, l.Smoke.Charges, l.Caltrops.Charges, l.Missiles.Charges, l.Strike.Charges,
                l.SelectedRear, w.Missiles.Count, w.Hazards.Count, l.Strike.Targeting);
        }

        run.Handle(new GameAction(ActionKind.Pause));
        Assert.Equal(RunState.Paused, run.State);
        var before = Snapshot();
        PressAll();
        Assert.Equal(before, Snapshot());
        run.Handle(new GameAction(ActionKind.Pause));

        w.Player.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Respawning, run.State);
        before = Snapshot();
        PressAll();
        Assert.Equal(before, Snapshot());
        run.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(before, Snapshot());
    }
}
