using NightPursuit.Core.Audio;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

/// <summary>Plan 8: a boss can get away (the mission fails and costs a spare car), and extra cars every 25,000 points.</summary>
public class BossEscapeTests
{
    const double Dt = 1.0 / 120;

    static BossDefinition Boss(AgentKind first = AgentKind.Runner, int cunning = 1) =>
        new("Test", 100, [new(first, 0.5), new(AgentKind.Blocker, 0)]) { Cunning = cunning };

    static MissionDefinition Def(string id = "m", int bonus = 1000) =>
        new(id, id, "Briefing", new TrackProfile(7), 300, 5, Array.Empty<WaveDefinition>(), Boss: Boss(), Bonus: bonus);

    static MissionDefinition NoBoss(int bonus) =>
        new("n", "n", "Briefing", new TrackProfile(7), 300, 5, Array.Empty<WaveDefinition>(), Bonus: bonus);

    static World Straight() => TestWorlds.World(1, TestWorlds.Straight());

    static BossCar Spawn(World w, double distance, double speed = 50)
    {
        var b = BossCar.Create(w.NextId(), Boss(), distance, 0, speed);
        b.Engaged = true;
        w.Agents.Add(b);
        return b;
    }

    // ---- extra cars ----

    [Fact]
    public void ExtraCars_Earned_counts_25000_thresholds()
    {
        Assert.Equal(25000, ExtraCars.Every);
        Assert.Equal(5, ExtraCars.MaxSpare);
        Assert.Equal(1, ExtraCars.Earned(24_999, 25_000));
        Assert.Equal(0, ExtraCars.Earned(0, 24_999));
        Assert.Equal(3, ExtraCars.Earned(20_000, 80_000));
        Assert.Equal(0, ExtraCars.Earned(50_000, 50_000));
        Assert.Equal(1, ExtraCars.Earned(49_000, 51_000));
    }

    [Fact]
    public void Crossing_25000_in_play_gives_a_car_and_says_so()
    {
        var w = Straight();
        w.Score = 24_900;
        w.Player.SpareCars = 2;
        var gunner = AgentCar.Create(w.NextId(), AgentKind.Gunner, 100, 0, 30);
        w.Agents.Add(gunner);
        w.Destroy(gunner, KillCause.Gunfire);
        Assert.Equal(25_500, w.Score);
        Assert.Equal(3, w.Player.SpareCars);
        Assert.Contains(w.Events, e => e is Message { Text: "EXTRA CAR", Tone: Tone.Good });
        Assert.Contains(w.Events, e => e is ExtraCarEarned);
    }

    [Fact]
    public void A_missile_shot_down_counts_toward_an_extra_car()
    {
        var w = Straight();
        w.Score = 24_990;
        w.Player.SpareCars = 1;
        w.AddScore(50);
        Assert.Equal(2, w.Player.SpareCars);
    }

    [Fact]
    public void Spare_cars_never_go_above_5()
    {
        var w = Straight();
        w.Score = 24_900;
        w.Player.SpareCars = 5;
        w.AddScore(600);
        Assert.Equal(5, w.Player.SpareCars);
        Assert.DoesNotContain(w.Events, e => e is ExtraCarEarned);
    }

    static void Tick(Game g) => g.Update(ControlFrame.Empty, 1 / 60.0);

    static void Wreck(Game g)
    {
        g.Run!.World.Player.Armor = 0;
        Tick(g);
        for (int i = 0; i < 20 && g.Run.State == RunState.Respawning; i++) g.Update(ControlFrame.Empty, 0.25);
    }

    [Fact]
    public void The_mission_bonus_can_earn_a_car()
    {
        var g = new Game([NoBoss(2000), NoBoss(1000)], new FeelSettings(), new GameSettings(), false, 5);
        g.StartMission();
        g.Run!.World.Score = 24_000;
        g.Run.World.Player.Armor = 0.5;            // no armor points, so the bonus is 2000 + 1000 (no car lost)
        g.Run.World.Player.Distance = g.Mission.ApproachLength;
        Tick(g);
        Assert.Equal(GameStage.MissionComplete, g.Stage);
        Assert.Equal(27_000, g.Score);
        Assert.Equal(4, g.SpareCars);
        Assert.Equal(1, g.LastExtraCars);
    }

    [Fact]
    public void A_car_lost_and_a_car_earned_still_loses_the_clean_bonus()
    {
        var g = new Game([NoBoss(1000)], new FeelSettings(), new GameSettings(), false, 5);
        g.StartMission();
        Wreck(g);
        Assert.Equal(2, g.Run!.World.Player.SpareCars);
        Assert.Equal(1, g.Run.World.Player.CarsLost);
        g.Run.World.Score = 24_900;
        g.Run.World.AddScore(600);
        Assert.Equal(3, g.Run.World.Player.SpareCars);   // as many as at the start
        g.Run.World.Player.Distance = g.Mission.ApproachLength;
        Tick(g);
        Assert.Equal(GameStage.MissionComplete, g.Stage);
        Assert.False(g.LastBonus!.NoCarLost);
    }

    // ---- the watch ----

    [Fact]
    public void The_warning_starts_beyond_600_m_and_the_boss_escapes_after_20_s()
    {
        var w = Straight();
        var b = Spawn(w, 601);
        var watch = new BossEscapeWatch();
        Assert.Equal((600.0, 1200.0, 20.0), (BossEscapeWatch.WarnBeyond, BossEscapeWatch.EscapeAt, BossEscapeWatch.Seconds));
        int steps = 0;
        bool escaped = false;
        while (!escaped && steps < 3000)
        {
            b.Distance = w.Player.Distance + 601;
            escaped = watch.Update(w, b, Dt);
            steps++;
            if (!escaped)
            {
                Assert.True(watch.Warning);
                Assert.InRange(watch.SecondsLeft, 0, 20);
                Assert.Equal(601, watch.Gap, 6);
            }
        }
        Assert.True(escaped);
        Assert.InRange(steps, 2399, 2401);

        var again = new BossEscapeWatch();
        b.Distance = w.Player.Distance + 700;
        for (int i = 0; i < 600; i++) again.Update(w, b, Dt);
        Assert.Equal(15, again.SecondsLeft, 6);
        b.Distance = w.Player.Distance + 599;
        Assert.False(again.Update(w, b, Dt));
        Assert.False(again.Warning);
        Assert.Equal(20, again.SecondsLeft, 6);       // back under 600 m resets it
    }

    [Fact]
    public void Reaching_1200_m_is_an_escape_at_once()
    {
        var w = Straight();
        var b = Spawn(w, 1200);
        Assert.True(new BossEscapeWatch().Update(w, b, Dt));
    }

    [Fact]
    public void No_watch_without_an_engaged_boss()
    {
        var w = Straight();
        var watch = new BossEscapeWatch();
        Assert.False(watch.Update(w, null, Dt));
        var b = Spawn(w, 1300);
        b.Engaged = false;
        Assert.False(watch.Update(w, b, Dt));
        Assert.False(watch.Warning);
    }

    /// <summary>A run in its boss stage with the boss engaged <paramref name="ahead"/> metres in front.</summary>
    static (MissionRun Run, BossCar Boss) BossRun(double ahead)
    {
        var run = new MissionRun(Def(), new FeelSettings(), 3);
        run.World.Player.Distance = run.World.Player.Distance + 300;
        run.Update(ControlFrame.Empty, Dt);
        var boss = run.Director.Boss!;
        boss.Engaged = true;
        boss.Distance = run.World.Player.Distance + ahead;
        return (run, boss);
    }

    [Fact]
    public void An_escape_ends_the_run()
    {
        var (run, boss) = BossRun(1300);
        run.Update(ControlFrame.Empty, Dt);
        Assert.Equal(RunState.Escaped, run.State);
        Assert.Contains(run.World.Events, e => e is Message { Text: "THE BOSS ESCAPED", Tone: Tone.Bad });
    }

    [Fact]
    public void It_only_counts_while_playing()
    {
        // Paused: nothing moves, nothing counts.
        var (paused, pb) = BossRun(700);
        paused.Handle(new GameAction(ActionKind.Pause));
        for (int i = 0; i < 30 * 60; i++) paused.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Paused, paused.State);
        Assert.False(paused.Escape.Warning);

        // Respawning: the wreck waits, the boss is far ahead, no count.
        var (respawn, rb) = BossRun(700);
        respawn.World.Player.Armor = 0;
        respawn.Update(ControlFrame.Empty, Dt);
        Assert.Equal(RunState.Respawning, respawn.State);
        rb.Distance = respawn.World.Player.Distance + 1300;
        respawn.Update(ControlFrame.Empty, Dt);
        Assert.NotEqual(RunState.Escaped, respawn.State);

        // The finale: the boss is beaten; nothing it does afterwards is an escape.
        var (finale, fb) = BossRun(100);
        finale.World.Destroy(fb, KillCause.Gunfire);
        finale.Update(ControlFrame.Empty, Dt);
        Assert.Equal(RunState.Finale, finale.State);
        for (int i = 0; i < 6 * 60 && finale.State == RunState.Finale; i++) finale.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Equal(RunState.Completed, finale.State);
    }

    [Fact]
    public void A_short_stop_never_sets_off_the_warning()
    {
        var w = Straight();
        var b = Spawn(w, 70, 50);
        w.Player.Speed = 50;
        var watch = new BossEscapeWatch();
        for (int i = 0; i < 90 * 120; i++)
        {
            var frame = i < 3 * 120
                ? new ControlFrame { Brake = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(w) }
                : new ControlFrame { Gas = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(w) };
            if (i < 3 * 120) w.Player.Speed = Math.Max(0, w.Player.Speed - 50.0 / 120);   // a hard stop, whatever the brakes do
            w.StepOnce(frame);
            watch.Update(w, b, Dt);
            Assert.False(watch.Warning, $"warning at {w.Time:0.0} s, gap {watch.Gap:0} m");
        }
    }

    // ---- what an escape costs ----

    static Game BossGame(int score = 1000, int spareCars = 3, bool practice = false)
    {
        var g = new Game([Def("a"), Def("b")], new FeelSettings(), new GameSettings(), practice, 5);
        g.StartMission();
        g.Run!.World.Score = score;
        g.Run.World.Player.SpareCars = spareCars;
        return g;
    }

    static void EscapeNow(Game g)
    {
        var run = g.Run!;
        run.World.Player.Distance = Math.Max(run.World.Player.Distance, g.Mission.ApproachLength);
        for (int i = 0; i < 10 && run.Director.Boss is null; i++) Tick(g);
        var boss = run.Director.Boss!;
        boss.Engaged = true;
        boss.Distance = run.World.Player.Distance + 1300;
        Tick(g);
    }

    [Fact]
    public void An_escape_fails_the_mission_keeps_the_points_and_costs_a_car()
    {
        var g = BossGame(1000, 3);
        Wreck(g);
        g.Run!.World.AddScore(500);
        Assert.Equal(2, g.Run.World.Player.SpareCars);
        EscapeNow(g);
        Assert.Equal(GameStage.MissionFailed, g.Stage);
        Assert.Equal(1500, g.Score);
        Assert.True(g.BossEscaped);
        Assert.Equal(1, g.SpareCars);

        g.RetryMission();
        Assert.Equal(GameStage.Briefing, g.Stage);
        Assert.Equal(0, g.MissionIndex);
        Assert.Null(g.Run);
        g.StartMission();
        Assert.Equal(1500, g.Run!.World.Score);
        Assert.Equal(1, g.Run.World.Player.SpareCars);
    }

    [Fact]
    public void An_escape_with_no_spare_cars_is_game_over()
    {
        var g = BossGame(1000, 1);
        Wreck(g);
        g.Run!.World.AddScore(200);
        Assert.Equal(0, g.Run.World.Player.SpareCars);
        EscapeNow(g);
        Assert.Equal(GameStage.GameOver, g.Stage);
        Assert.Equal(1200, g.Score);
        Assert.Equal(0, g.SpareCars);
        Assert.True(g.BossEscaped);                             // the game-over screen says why
    }

    // ---- the screens ----

    sealed class Rig
    {
        public readonly GameSettings Settings = new();
        public readonly ScreenFlow Flow;

        public Rig()
        {
            var feel = new FeelSettings();
            var defs = new[] { Def("a"), Def("b") };
            Flow = new ScreenFlow(practice => new Game(defs, feel, Settings, practice, 5), feel, Settings, new HighScoreTable(), new MemoryGameStore());
        }

        public Game G => Flow.Game!;
        public void Key(MenuInput i) => Flow.Handle(new MenuKey(i));
        public void Letter(char c) => Flow.Handle(new MenuKey(MenuInput.Letter, c));
        public void Tick() => Flow.Update(ControlFrame.Empty, 1 / 60.0);
        public void ToPlay(bool practice = false) { Flow.StartGame(practice); Key(MenuInput.Confirm); }
        public void Escape()
        {
            var run = G.Run!;
            run.World.Player.Distance = G.Mission.ApproachLength;
            for (int i = 0; i < 10 && run.Director.Boss is null; i++) Tick();
            run.Director.Boss!.Engaged = true;
            run.Director.Boss.Distance = run.World.Player.Distance + 1300;
            Tick();
        }
    }

    [Fact]
    public void ScreenFlow_shows_mission_failed_and_retries_on_enter()
    {
        var r = new Rig();
        r.ToPlay();
        r.Escape();
        Assert.Equal(Screen.MissionFailed, r.Flow.Current);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Briefing, r.Flow.Current);
        Assert.Equal(0, r.G.MissionIndex);
        Assert.Equal(2, r.G.SpareCars);
    }

    [Fact]
    public void In_practice_R_restarts_from_mission_failed()
    {
        var r = new Rig();
        r.ToPlay(practice: true);
        r.Escape();
        Assert.Equal(Screen.MissionFailed, r.Flow.Current);
        r.Letter('R');
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.Equal(RunState.Playing, r.G.Run!.State);
    }

    [Fact]
    public void Mission_failed_plays_the_game_over_music()
    {
        var r = new Rig();
        var slots = new MusicSlots(new MusicManifest { GameOver = new MusicTrack("over.mp3"), Results = new MusicTrack("results.mp3") });
        r.ToPlay();
        r.Escape();
        var cue = slots.Update(r.Flow);
        Assert.Equal((MusicSlot.GameOver, new MusicTrack("over.mp3")), (cue.Slot, cue.Track));
    }
}
