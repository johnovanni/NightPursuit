using NightPursuit.Core.Audio;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Tests.Audio;

public class MusicSlotsTests
{
    static MusicTrack T(string file) => new(file);

    static MissionDefinition Def(string id, MissionMusic? music) =>
        new(id, id, "Briefing", new TrackProfile(7), 300, 5, Array.Empty<WaveDefinition>(), Bonus: 0, Music: music);

    sealed class Rig
    {
        public readonly ScreenFlow Flow;
        public readonly MusicSlots Slots;
        public readonly GameSettings Settings = new();

        public Rig(MusicManifest manifest, MissionMusic? alpha = null, MissionMusic? beta = null)
        {
            var defs = new[] { Def("alpha", alpha), Def("beta", beta) };
            var feel = new FeelSettings();
            Flow = new ScreenFlow(practice => new Game(defs, feel, Settings, practice, 5), feel, Settings, new HighScoreTable(), new MemoryGameStore());
            Slots = new MusicSlots(manifest);
        }

        public Game G => Flow.Game!;
        public World W => G.Run!.World;
        public MusicCue Cue() => Slots.Update(Flow);
        public void Key(MenuInput i) => Flow.Handle(new MenuKey(i));
        public void Tick() => Flow.Update(ControlFrame.Empty, 1 / 60.0);
        public void ToPlay(bool practice = false) { Flow.StartGame(practice); Key(MenuInput.Confirm); }
        public void PauseNow() { Flow.HandleGameAction(new GameAction(ActionKind.Pause)); Tick(); }
        public void Select(string id)
        {
            for (int i = 0; i < 10 && Flow.Menu!.Current.Id != id; i++) Key(MenuInput.Down);
            Assert.Equal(id, Flow.Menu!.Current.Id);
        }
        public void ShowBoss() => W.Publish(new BossAppeared(W.Time, "Boss"));
        /// <summary>From Play: pause, quit, confirm, so the flow is on GameOver.</summary>
        public void QuitFromPlay()
        {
            PauseNow();
            Select("quit"); Key(MenuInput.Confirm);
            Key(MenuInput.Down); Key(MenuInput.Confirm);
        }
    }

    static readonly MusicManifest Full = new()
    {
        Title = T("title.mp3"), Briefing = T("briefing.mp3"), Results = T("results.mp3"),
        GameOver = T("over.mp3"), Finished = T("done.mp3"),
    };
    static readonly MissionMusic Alpha = new(T("a-theme.mp3"), T("a-boss.mp3"));

    [Fact]
    public void Title_uses_briefing_music_when_it_has_none()
    {
        var r = new Rig(new MusicManifest { Briefing = T("briefing.mp3") });
        var cue = r.Cue();
        Assert.Equal(T("briefing.mp3"), cue.Track);
        Assert.Equal(MusicSlot.Briefing, cue.Slot);   // the slot that supplies the track, so Title to Briefing does not restart it
        Assert.Equal(1.0, cue.Level);

        var own = new Rig(Full).Cue();
        Assert.Equal((MusicSlot.Title, T("title.mp3")), (own.Slot, own.Track));
        Assert.Null(new Rig(new MusicManifest()).Cue().Track);
    }

    [Fact]
    public void Briefing_and_results_use_their_own_tracks()
    {
        var r = new Rig(Full, Alpha);
        r.Flow.StartGame();
        var briefing = r.Cue();
        Assert.Equal((MusicSlot.Briefing, T("briefing.mp3")), (briefing.Slot, briefing.Track));
        r.Key(MenuInput.Confirm);
        r.Tick();
        r.W.Player.Distance = r.G.Mission.ApproachLength;
        r.Tick();
        Assert.Equal(Screen.MissionComplete, r.Flow.Current);
        var results = r.Cue();
        Assert.Equal((MusicSlot.Results, T("results.mp3")), (results.Slot, results.Track));
    }

    [Fact]
    public void Play_uses_the_mission_theme_then_the_boss_theme_after_BossAppeared()
    {
        var r = new Rig(Full, Alpha);
        r.ToPlay();
        r.Tick();
        var theme = r.Cue();
        Assert.Equal((MusicSlot.MissionTheme, T("a-theme.mp3")), (theme.Slot, theme.Track));

        r.Tick();
        r.ShowBoss();
        var boss = r.Cue();
        Assert.Equal((MusicSlot.BossTheme, T("a-boss.mp3")), (boss.Slot, boss.Track));
        Assert.Equal(theme.Generation, boss.Generation);

        // The event is gone after the next step, but the sighting is remembered, paused or not.
        r.Tick();
        Assert.Equal(MusicSlot.BossTheme, r.Cue().Slot);
        r.PauseNow();
        r.Tick();
        var paused = r.Cue();
        Assert.Equal((MusicSlot.BossTheme, MusicSlots.PauseLevel), (paused.Slot, paused.Level));
    }

    [Fact]
    public void A_boss_event_seen_while_paused_is_not_lost()
    {
        var r = new Rig(Full, Alpha);
        r.ToPlay();
        r.Tick();
        Assert.Equal(MusicSlot.MissionTheme, r.Cue().Slot);
        r.Tick();
        r.ShowBoss();
        r.PauseNow();   // the pause frame itself does not advance the world
        Assert.Equal(MusicSlot.BossTheme, r.Cue().Slot);
    }

    [Fact]
    public void Missing_boss_theme_keeps_the_mission_theme()
    {
        var r = new Rig(Full, new MissionMusic(T("a-theme.mp3"), null));
        r.ToPlay();
        r.Tick();
        var before = r.Cue();
        r.Tick();
        r.ShowBoss();
        Assert.Equal(before, r.Cue());
        Assert.Equal(T("a-theme.mp3"), r.Cue().Track);
    }

    [Fact]
    public void A_mission_without_music_is_silent_in_play()
    {
        var r = new Rig(Full);
        r.ToPlay();
        r.Tick();
        var cue = r.Cue();
        Assert.Equal(MusicSlot.MissionTheme, cue.Slot);
        Assert.Null(cue.Track);
    }

    [Fact]
    public void Game_over_and_finished_fall_back_to_results()
    {
        var results = new MusicManifest { Results = T("results.mp3") };
        var r = new Rig(results, Alpha);
        r.ToPlay();
        r.Tick();
        r.QuitFromPlay();
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        var over = r.Cue();
        Assert.Equal((MusicSlot.Results, T("results.mp3")), (over.Slot, over.Track));

        var own = new Rig(Full, Alpha);
        own.ToPlay();
        own.Tick();
        own.QuitFromPlay();
        var ownOver = own.Cue();
        Assert.Equal((MusicSlot.GameOver, T("over.mp3")), (ownOver.Slot, ownOver.Track));

        foreach (var (manifest, slot, file) in new[]
        {
            (results, MusicSlot.Results, "results.mp3"),
            (Full, MusicSlot.Finished, "done.mp3"),
        })
        {
            var f = new Rig(manifest, Alpha, Alpha);
            f.ToPlay();
            for (int i = 0; i < 2; i++)
            {
                f.Tick();
                f.W.Player.Distance = f.G.Mission.ApproachLength;
                f.Tick();
                Assert.Equal(Screen.MissionComplete, f.Flow.Current);
                f.Key(MenuInput.Confirm);                 // continue
                if (i == 0) f.Key(MenuInput.Confirm);     // the next mission's briefing
            }
            Assert.Equal(Screen.Finished, f.Flow.Current);
            var cue = f.Cue();
            Assert.Equal((slot, T(file)), (cue.Slot, cue.Track));
        }
    }

    [Fact]
    public void Settings_from_the_title_keeps_title_music_at_full_level()
    {
        var r = new Rig(Full);
        var title = r.Cue();
        r.Select("settings"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Settings, r.Flow.Current);
        Assert.Equal(title, r.Cue());
        r.Key(MenuInput.Back);
        r.Select("controls"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Controls, r.Flow.Current);
        Assert.Equal(title, r.Cue());
    }

    [Fact]
    public void Pause_drops_to_40_percent_and_settings_from_pause_is_full()
    {
        var r = new Rig(Full, Alpha);
        r.ToPlay();
        r.Tick();
        var playing = r.Cue();
        Assert.Equal(1.0, playing.Level);

        r.PauseNow();
        var paused = r.Cue();
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Equal((playing.Slot, playing.Track, 0.4), (paused.Slot, paused.Track, paused.Level));

        r.Select("settings"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Settings, r.Flow.Current);
        var settings = r.Cue();
        Assert.Equal((playing.Slot, playing.Track, 1.0), (settings.Slot, settings.Track, settings.Level));

        r.Key(MenuInput.Back);
        Assert.Equal(0.4, r.Cue().Level);

        r.Select("controls"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Controls, r.Flow.Current);
        Assert.Equal(0.4, r.Cue().Level);
        r.Key(MenuInput.Back);

        r.Select("quit"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.QuitConfirm, r.Flow.Current);
        Assert.Equal(0.4, r.Cue().Level);
        r.Key(MenuInput.Back);

        r.Select("resume"); r.Key(MenuInput.Confirm);
        Assert.Equal(1.0, r.Cue().Level);
    }

    [Fact]
    public void A_practice_restart_restarts_the_mission_theme()
    {
        var r = new Rig(Full, Alpha);
        r.ToPlay(practice: true);
        r.Tick();
        var first = r.Cue();
        var world = r.W;
        r.PauseNow();
        r.Select("restart"); r.Key(MenuInput.Confirm);
        Assert.NotSame(world, r.W);
        var second = r.Cue();
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.Equal((first.Slot, first.Track), (second.Slot, second.Track));
        Assert.True(second.Generation > first.Generation);
    }

    [Fact]
    public void A_restart_after_the_boss_goes_back_to_the_mission_theme()
    {
        var r = new Rig(Full, Alpha);
        r.ToPlay(practice: true);
        r.Tick();
        r.ShowBoss();
        Assert.Equal(MusicSlot.BossTheme, r.Cue().Slot);
        r.PauseNow();
        r.Select("restart"); r.Key(MenuInput.Confirm);
        Assert.Equal(MusicSlot.MissionTheme, r.Cue().Slot);
    }

    [Fact]
    public void A_respawn_keeps_the_music_going()
    {
        var r = new Rig(Full, Alpha);
        r.ToPlay();
        r.Tick();
        r.Tick();
        r.ShowBoss();
        var before = r.Cue();
        var world = r.W;

        r.W.Player.Armor = 0;
        r.Tick();
        Assert.Equal(RunState.Respawning, r.G.Run!.State);
        for (int i = 0; i < 20 && r.G.Run.State == RunState.Respawning; i++) r.Flow.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(RunState.Playing, r.G.Run.State);
        Assert.Same(world, r.W);
        Assert.Equal(before, r.Cue());
    }

    [Fact]
    public void The_next_mission_starts_its_own_theme_from_the_beginning()
    {
        var beta = new MissionMusic(T("a-theme.mp3"), null);   // the same file as the last mission, so only Generation can tell
        var r = new Rig(Full, Alpha, beta);
        r.ToPlay();
        r.Tick();
        var first = r.Cue();
        r.W.Player.Distance = r.G.Mission.ApproachLength;
        r.Tick();
        r.Cue();
        r.Key(MenuInput.Confirm);
        r.Key(MenuInput.Confirm);
        r.Tick();
        var second = r.Cue();
        Assert.Equal(MusicSlot.MissionTheme, second.Slot);
        Assert.True(second.Generation > first.Generation);
    }

    [Fact]
    public void FilesNamed_lists_every_named_file_once()
    {
        var manifest = new MusicManifest { Title = T("same.mp3"), Results = T("results.mp3"), Finished = new MusicTrack("SAME.mp3", 0.5) };
        var missions = new[]
        {
            Def("a", new MissionMusic(T("a-theme.mp3"), T("a-boss.mp3"))),
            Def("b", new MissionMusic(T("a-theme.mp3"), null)),
            Def("c", null),
        };
        var files = new MusicSlots(manifest).FilesNamed(missions).ToList();
        Assert.Equal(new[] { "same.mp3", "results.mp3", "a-theme.mp3", "a-boss.mp3" }, files);
        Assert.Empty(new MusicSlots(new MusicManifest()).FilesNamed(new[] { Def("c", null) }));
    }
}
