using NightPursuit.Core.Audio;
using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class FinaleRunTests
{
    static string MissionPath(string file) => Path.Combine(AppContext.BaseDirectory, "content", "missions", file);
    static BossDefinition BossDef => new("Test", 100, [new(AgentKind.Runner, 0)]);

    static MissionDefinition Def(double approach = 500, BossDefinition? boss = null) =>
        new("test", "Test", "Briefing", new TrackProfile(7), approach, 5, [],
            StartingLoadout: new LoadoutDefinition(5, 2, 2, 2, 1), Boss: boss ?? BossDef);

    const double Frame = 1 / 60.0;

    /// <summary>Puts the player at the end of the approach so the boss appears, and returns it.</summary>
    static BossCar ToBoss(MissionRun run)
    {
        run.World.Player.Distance = run.Director.DistanceRemaining(run.World) + run.World.Player.Distance;
        run.World.Player.Speed = 40;
        run.Update(ControlFrame.Empty, World.Step);
        return run.Director.Boss!;
    }

    static MissionRun RunAtBoss(out BossCar boss)
    {
        var run = new MissionRun(Def(), new FeelSettings(), 3);
        boss = ToBoss(run);
        return run;
    }

    static void Kill(MissionRun run, BossCar boss)
    {
        run.World.Destroy(boss, KillCause.Missile);
        run.Update(ControlFrame.Empty, World.Step);
    }

    static void RunOut(MissionRun run)
    {
        for (int i = 0; i < 1000 && run.State == RunState.Finale; i++) run.Update(ControlFrame.Empty, Frame);
    }

    [Fact]
    public void A_boss_kill_plays_the_finale_then_completes()
    {
        var run = RunAtBoss(out var boss);
        Kill(run, boss);
        Assert.Equal(RunState.Finale, run.State);
        Assert.True(run.Director.Completed);
        int frames = 0;
        while (run.State == RunState.Finale && frames < 1000) { run.Update(ControlFrame.Empty, Frame); frames++; }
        Assert.Equal(RunState.Completed, run.State);
        Assert.InRange(frames * Frame, BossFinale.TotalSeconds - 0.05, BossFinale.TotalSeconds + 0.05);
        Assert.True(run.World.Finale!.Exploded);
    }

    [Fact]
    public void The_mission_bonus_and_score_are_todays()
    {
        var def = Def();
        var g = new Game([def], new FeelSettings(), new GameSettings(), seed: 5);
        g.StartMission();
        var boss = ToBoss(g.Run!);
        g.Run!.World.Score = 700;
        g.Run.World.Player.Armor = 64;
        g.Run.World.Destroy(boss, KillCause.Missile);
        for (int i = 0; i < 1000 && g.Stage == GameStage.Playing; i++) g.Update(ControlFrame.Empty, Frame);

        Assert.Equal(GameStage.MissionComplete, g.Stage);
        var expected = MissionBonus.For(def, 64, false);
        Assert.Equal(expected.Total, g.LastBonus!.Total);
        Assert.Equal(700 + 5000 + expected.Total, g.Score);
        Assert.Equal(3, g.SpareCars);
    }

    [Fact]
    public void A_mission_without_a_boss_completes_at_once()
    {
        var run = new MissionRun(Def() with { Boss = null }, new FeelSettings(), 3);
        run.World.Player.Distance = 500;
        run.Update(ControlFrame.Empty, Frame);
        Assert.Equal(RunState.Completed, run.State);
        Assert.Null(run.World.Finale);
    }

    [Fact]
    public void Skip_ends_the_finale_at_once_without_an_explosion()
    {
        var run = RunAtBoss(out var boss);
        run.Handle(new GameAction(ActionKind.SkipFinale));   // not in the finale yet: nothing happens
        Assert.Equal(RunState.Playing, run.State);
        Kill(run, boss);
        run.Update(ControlFrame.Empty, 0.5);
        run.Handle(new GameAction(ActionKind.SkipFinale));   // too soon: a tap of fire just after the kill
        Assert.Equal(RunState.Finale, run.State);
        for (int i = 0; i < 60; i++) run.Update(ControlFrame.Empty, Frame);
        Assert.True(run.World.Finale!.Elapsed >= BossFinale.SkipAfterSeconds);

        run.Handle(new GameAction(ActionKind.SkipFinale));
        Assert.Equal(RunState.Completed, run.State);
        Assert.True(run.World.Finale!.Skipped);
        Assert.False(run.World.Finale.Exploded);
        run.Update(ControlFrame.Empty, Frame);
        Assert.DoesNotContain(run.World.Events, e => e is BossExploded);
    }

    [Fact]
    public void Held_fire_does_not_skip_the_finale()
    {
        var run = RunAtBoss(out var boss);
        Kill(run, boss);
        var fire = new ControlFrame { Fire = true, PadFire = true };
        for (int i = 0; i < 60; i++) run.Update(fire, Frame);
        Assert.Equal(RunState.Finale, run.State);
    }

    [Fact]
    public void Pausing_in_the_finale_resumes_the_finale()
    {
        var run = RunAtBoss(out var boss);
        Kill(run, boss);
        run.Update(ControlFrame.Empty, 0.5);
        run.Handle(new GameAction(ActionKind.Pause));
        Assert.Equal(RunState.Paused, run.State);
        double at = run.World.Finale!.Elapsed;
        for (int i = 0; i < 60; i++) run.Update(ControlFrame.Empty, Frame);
        Assert.Equal(at, run.World.Finale.Elapsed);
        run.Handle(new GameAction(ActionKind.Pause));
        Assert.Equal(RunState.Finale, run.State);
        run.Update(ControlFrame.Empty, Frame);
        Assert.True(run.World.Finale.Elapsed > at);
    }

    [Fact]
    public void Pause_then_R_restarts_a_practice_mission_from_the_finale()
    {
        var g = new Game([Def()], new FeelSettings(), new GameSettings(), practice: true, seed: 5);
        g.StartMission();
        var boss = ToBoss(g.Run!);
        g.Run!.World.Destroy(boss, KillCause.Missile);
        g.Update(ControlFrame.Empty, Frame);
        Assert.Equal(RunState.Finale, g.Run.State);
        var before = g.Run.World;

        g.Handle(new GameAction(ActionKind.Pause));
        g.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(RunState.Playing, g.Run.State);
        Assert.NotSame(before, g.Run.World);
        Assert.Null(g.Run.World.Finale);
    }

    [Fact]
    public void R_alone_does_not_restart_during_the_finale()
    {
        var g = new Game([Def()], new FeelSettings(), new GameSettings(), practice: true, seed: 5);
        g.StartMission();
        var boss = ToBoss(g.Run!);
        g.Run!.World.Destroy(boss, KillCause.Missile);
        g.Update(ControlFrame.Empty, Frame);
        var world = g.Run.World;
        g.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(RunState.Finale, g.Run.State);
        Assert.Same(world, g.Run.World);
        Assert.Contains(world.Events, e => e is Message { Text: "PRESS P TO PAUSE, THEN R TO RESTART" });
    }

    [Fact]
    public void A_wreck_in_the_kill_step_still_wins_and_keeps_the_spare()
    {
        var def = Def();
        var g = new Game([def], new FeelSettings(), new GameSettings(), seed: 5);
        g.StartMission();
        var run = g.Run!;
        var boss = ToBoss(run);
        run.World.WreckPlayer("TEST");               // wrecked earlier in the same step as the kill
        run.World.Destroy(boss, KillCause.Missile);
        g.Update(ControlFrame.Empty, World.Step);

        var p = run.World.Player;
        Assert.Equal(RunState.Finale, run.State);
        Assert.Equal((VehicleState.Driving, 0.0, 3), (p.State, p.Armor, p.SpareCars));
        for (int i = 0; i < 1000 && g.Stage == GameStage.Playing; i++)
        {
            g.Update(ControlFrame.Empty, Frame);
            Assert.Equal(VehicleState.Driving, p.State);
        }
        Assert.Equal(GameStage.MissionComplete, g.Stage);
        Assert.Equal(MissionBonus.For(def, 0, false).Total, g.LastBonus!.Total);
        Assert.Equal(3, g.SpareCars);
    }

    [Fact]
    public void A_bust_in_the_kill_step_shows_no_busted_banner()
    {
        var run = RunAtBoss(out var boss);
        run.World.BustPlayer();
        run.World.Destroy(boss, KillCause.Missile);
        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Finale, run.State);
        Assert.False(run.World.BustedPending);
        Assert.Null(run.World.Announcement);
        Assert.Equal(VehicleState.Driving, run.World.Player.State);
    }

    [Fact]
    public void A_boss_killed_during_the_respawn_wait_respawns_at_once_then_plays_the_finale()
    {
        var run = RunAtBoss(out var boss);
        run.World.Player.Armor = 0;
        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Respawning, run.State);

        run.World.Destroy(boss, KillCause.Missile);   // a missile already in flight
        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Finale, run.State);
        Assert.Equal(2, run.World.Player.SpareCars);  // the spare is used, as it always was
        Assert.Equal(VehicleState.Driving, run.World.Player.State);
        RunOut(run);
        Assert.Equal(RunState.Completed, run.State);
    }

    [Fact]
    public void Each_crash_comes_from_the_games_picker()
    {
        var expected = new FinalePicker(new SeededRng(11));
        var (first, second) = (expected.Next(), expected.Next());
        var g = new Game([Def()], new FeelSettings(), new GameSettings(), practice: true, seed: 5, finales: new FinalePicker(new SeededRng(11)));

        g.StartMission();
        var boss = ToBoss(g.Run!);
        g.Run!.World.Destroy(boss, KillCause.Missile);
        Assert.Equal(first, g.Run.World.Finale!.Choice);
        for (int i = 0; i < 1000 && g.Stage == GameStage.Playing; i++) g.Update(ControlFrame.Empty, Frame);
        Assert.Equal(GameStage.MissionComplete, g.Stage);

        Assert.True(g.RestartMission());
        boss = ToBoss(g.Run);
        g.Run.World.Destroy(boss, KillCause.Missile);
        Assert.Equal(second, g.Run.World.Finale!.Choice);
        Assert.NotEqual((first.Kind, first.Side), (second.Kind, second.Side));
    }

    [Fact]
    public void A_game_without_a_picker_still_varies_its_crashes()
    {
        var g = new Game([Def()], new FeelSettings(), new GameSettings(), practice: true, seed: 5);
        Assert.NotNull(g.Finales);
        var a = g.Finales.Next();
        var b = g.Finales.Next();
        Assert.NotEqual((a.Kind, a.Side), (b.Kind, b.Side));
    }

    [Fact]
    public void Nothing_new_appears_during_the_finale()
    {
        var def = MissionLoader.LoadFile(MissionPath("mission-02.json"));
        var run = new MissionRun(def, new FeelSettings(), 1);
        var boss = ToBoss(run);
        for (int i = 0; i < 120; i++) run.Update(ControlFrame.Empty, Frame);   // let traffic and police come in
        run.World.Destroy(boss, KillCause.Missile);
        var agents = run.World.Agents.ToHashSet();
        var hazards = run.World.Hazards.ToHashSet();
        while (run.State is RunState.Playing or RunState.Finale)
        {
            run.Update(ControlFrame.Empty, Frame);
            Assert.All(run.World.Agents, a => Assert.Contains(a, agents));
        }
        Assert.Equal(RunState.Completed, run.State);
        var added = run.World.Hazards.Where(h => !hazards.Contains(h)).ToList();
        Assert.IsType<NightPursuit.Core.Hazards.Wreck>(Assert.Single(added));
    }

    static (ScreenFlow Flow, Game Game) FlowAtFinale()
    {
        var feel = new FeelSettings();
        var settings = new GameSettings();
        var flow = new ScreenFlow(practice => new Game([Def()], feel, settings, practice, 5), feel, settings, new HighScoreTable(), new MemoryGameStore());
        flow.StartGame(false);
        flow.Handle(new MenuKey(MenuInput.Confirm));
        var g = flow.Game!;
        var boss = ToBoss(g.Run!);
        g.Run!.World.Destroy(boss, KillCause.Missile);
        flow.Update(ControlFrame.Empty, Frame);
        Assert.Equal(RunState.Finale, g.Run.State);
        Assert.Equal(Screen.Play, flow.Current);
        return (flow, g);
    }

    [Fact]
    public void Focus_loss_in_the_finale_pauses_it()
    {
        var (flow, g) = FlowAtFinale();
        flow.FocusLost();
        flow.Update(ControlFrame.Empty, Frame);
        Assert.Equal(RunState.Paused, g.Run!.State);
        Assert.Equal(Screen.Pause, flow.Current);
        flow.Handle(new MenuKey(MenuInput.Confirm));    // RESUME
        Assert.Equal(Screen.Play, flow.Current);
        Assert.Equal(RunState.Finale, g.Run.State);
    }

    [Fact]
    public void The_finale_reaches_the_results_screen()
    {
        var (flow, _) = FlowAtFinale();
        for (int i = 0; i < 1000 && flow.Current == Screen.Play; i++) flow.Update(ControlFrame.Empty, Frame);
        Assert.Equal(Screen.MissionComplete, flow.Current);
    }

    [Fact]
    public void Engine_sound_runs_in_the_finale()
    {
        var (flow, _) = FlowAtFinale();
        var director = new SoundDirector(new MusicSlots(new MusicManifest()));
        var frame = director.Update(flow, Frame);
        Assert.True(frame.Live.EngineOn);
    }
}
