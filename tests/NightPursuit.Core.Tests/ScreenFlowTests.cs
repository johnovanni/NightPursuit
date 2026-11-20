using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Tests;

public class ScreenFlowTests
{
    static MissionDefinition Def(string id) =>
        new(id, id, "Briefing", new TrackProfile(7), 300, 5, Array.Empty<WaveDefinition>(), Bonus: 0);

    sealed class Rig
    {
        public readonly FeelSettings Feel = new();
        public readonly GameSettings Settings = new();
        public readonly HighScoreTable Scores = new();
        public readonly MemoryGameStore Store = new();
        public readonly ScreenFlow Flow;

        public Rig(int missions = 2)
        {
            var defs = new[] { Def("alpha"), Def("beta") }.Take(missions).ToArray();
            Flow = new ScreenFlow(practice => new Game(defs, Feel, Settings, practice, 5), Feel, Settings, Scores, Store);
        }

        public Game G => Flow.Game!;
        public void Key(MenuInput i) => Flow.Handle(new MenuKey(i));
        public void Letter(char c) => Flow.Handle(new MenuKey(MenuInput.Letter, c));
        public void Tick() => Flow.Update(ControlFrame.Empty, 1 / 60.0);
        public void ToPlay(bool practice = false) { Flow.StartGame(practice); Key(MenuInput.Confirm); }
        public void PauseNow() { Flow.HandleGameAction(new GameAction(ActionKind.Pause)); Tick(); }
        public void ToPause() { ToPlay(); PauseNow(); }
        public void Select(string id)
        {
            for (int i = 0; i < 10 && Flow.Menu!.Current.Id != id; i++) Key(MenuInput.Down);
            Assert.Equal(id, Flow.Menu!.Current.Id);
        }
        public void Complete()
        {
            G.Run!.World.Player.Distance = G.Mission.ApproachLength;
            Tick();
        }
        /// <summary>From Play with a score: pause, quit, confirm, so the flow is on GameOver.</summary>
        public void QuitFromPause()
        {
            Select("quit"); Key(MenuInput.Confirm);
            Key(MenuInput.Down); Key(MenuInput.Confirm);
        }
    }

    [Fact]
    public void InitialsEntry_edits()
    {
        Assert.Equal("JMA", new InitialsEntry("jm").Text);
        var e = new InitialsEntry("AAZ");
        e.Right(); e.Right();
        e.Up();
        Assert.Equal("AAA", e.Text);
        e.Down();
        Assert.Equal("AAZ", e.Text);
        e.Type('x');
        Assert.Equal("AAX", e.Text);
        Assert.Equal(2, e.Cursor);
        var f = new InitialsEntry("");
        f.Delete();
        Assert.Equal(0, f.Cursor);
        f.Type('1');
        Assert.Equal("AAA", f.Text);
        Assert.Equal(0, f.Cursor);
        f.Type('b');
        Assert.Equal("BAA", f.Text);
        Assert.Equal(1, f.Cursor);
        f.Left(); f.Left();
        Assert.Equal(0, f.Cursor);
    }

    [Fact]
    public void Menu_moves_and_wraps()
    {
        var m = new MenuModel("T", Array.Empty<string>(), new[] { new MenuEntry("a", "A"), new MenuEntry("b", "B"), new MenuEntry("c", "C") });
        m.Move(-1);
        Assert.Equal("c", m.Current.Id);
        m.Move(1);
        Assert.Equal("a", m.Current.Id);
        m.Move(4);
        Assert.Equal("b", m.Current.Id);
    }

    [Fact]
    public void Title_menu_copy()
    {
        var r = new Rig();
        Assert.Equal(Screen.Title, r.Flow.Current);
        Assert.Equal("NIGHT PURSUIT", r.Flow.Menu!.Title);
        Assert.Equal("SIX MISSIONS. THREE SPARE CARS. ONE LONG NIGHT.", r.Flow.Menu.Lines[0]);
        Assert.Equal(new[] { "START GAME", "HIGH SCORES", "SETTINGS", "CONTROLS", "QUIT" }, r.Flow.Menu.Items.Select(i => i.Label));
        r.Key(MenuInput.Back); r.Key(MenuInput.Pause);
        Assert.Equal(Screen.Title, r.Flow.Current);
        r.Select("quit"); r.Key(MenuInput.Confirm);
        Assert.True(r.Flow.ExitRequested);
    }

    [Fact]
    public void Start_game_goes_to_briefing_then_play_with_intro_help_only_first()
    {
        var r = new Rig();
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Briefing, r.Flow.Current);
        Assert.False(r.Flow.ShowIntroHelp);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.True(r.Flow.ShowIntroHelp);
        r.Flow.IntroHelpSeen();
        Assert.False(r.Flow.ShowIntroHelp);
        r.Complete();
        Assert.Equal(Screen.MissionComplete, r.Flow.Current);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Briefing, r.Flow.Current);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.False(r.Flow.ShowIntroHelp);
    }

    [Fact]
    public void Esc_in_play_pauses_and_Esc_in_pause_resumes_once()
    {
        var r = new Rig();
        r.ToPlay();
        r.PauseNow();
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Equal("PAUSED", r.Flow.Menu!.Title);
        Assert.Equal("MISSION 1 OF 2: ALPHA", r.Flow.Menu.Lines[0]);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.Equal(RunState.Playing, r.G.Run!.State);
        r.Tick();
        Assert.Equal(Screen.Play, r.Flow.Current);
    }

    [Fact]
    public void F1_in_play_pauses_and_returns()
    {
        var r = new Rig();
        r.ToPlay();
        r.Flow.HandleGameAction(new GameAction(ActionKind.ToggleHelp));
        r.Flow.HandleGameAction(new GameAction(ActionKind.FireMissile));   // ignored: the flow left Play
        Assert.Equal(Screen.Controls, r.Flow.Current);
        Assert.Equal(RunState.Paused, r.G.Run!.State);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.Equal(RunState.Playing, r.G.Run.State);
    }

    [Fact]
    public void F1_during_respawn_leaves_the_run_as_it_was()
    {
        var r = new Rig();
        r.ToPlay();
        r.G.Run!.World.Player.Armor = 0;
        r.Tick();
        Assert.Equal(RunState.Respawning, r.G.Run.State);
        r.Flow.HandleGameAction(new GameAction(ActionKind.ToggleHelp));
        Assert.Equal(RunState.Respawning, r.G.Run.State);
        r.Letter('p');
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.Equal(RunState.Respawning, r.G.Run.State);
    }

    [Fact]
    public void Quit_needs_confirmation()
    {
        var r = new Rig();
        r.Flow.StartGame();
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.QuitConfirm, r.Flow.Current);
        Assert.Equal("QUIT THIS GAME?", r.Flow.Menu!.Title);
        Assert.Equal("NO, KEEP PLAYING", r.Flow.Menu.Current.Label);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Briefing, r.Flow.Current);
        r.Key(MenuInput.Back); r.Key(MenuInput.Down); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        Assert.True(r.G.QuitEarly);
    }

    [Fact]
    public void Quitting_with_a_qualifying_score_still_asks_for_initials()
    {
        var r = new Rig();
        r.ToPlay();
        r.G.Run!.World.Score = 500;
        r.PauseNow();
        r.Select("quit"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.QuitConfirm, r.Flow.Current);
        r.Key(MenuInput.Down); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.EnterInitials, r.Flow.Current);
    }

    [Fact]
    public void A_game_over_without_a_qualifying_score_goes_to_the_table()
    {
        var r = new Rig();
        r.Flow.StartGame();
        r.Key(MenuInput.Back); r.Key(MenuInput.Down); r.Key(MenuInput.Confirm);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.HighScores, r.Flow.Current);
        Assert.Equal(0, r.Flow.NewRank);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Title, r.Flow.Current);
        Assert.Null(r.Flow.Game);
    }

    [Fact]
    public void High_score_entry_saves_and_highlights()
    {
        var r = new Rig();
        r.ToPlay();
        r.G.Run!.World.Score = 500;
        r.PauseNow();
        r.QuitFromPause();
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.EnterInitials, r.Flow.Current);
        r.Letter('J'); r.Letter('M'); r.Letter('A');
        Assert.Equal("JMA", r.Flow.Initials!.Text);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.HighScores, r.Flow.Current);
        Assert.Equal(1, r.Flow.NewRank);
        Assert.Equal("JMA", r.Scores.Entries[0].Initials);
        Assert.Equal(500, r.Scores.Entries[0].Score);
        Assert.Equal("JMA", r.Settings.LastInitials);
        Assert.Equal(1, r.Store.HighScoreSaves);
        Assert.Equal("JMA", r.Store.LastSettings!.LastInitials);
        Assert.Single(r.Store.LastHighScores!.Entries);
    }

    [Fact]
    public void WASD_letters_type_in_initials()
    {
        var r = new Rig();
        r.ToPlay();
        r.G.Run!.World.Score = 500;
        r.PauseNow();
        r.QuitFromPause();
        r.Key(MenuInput.Confirm);
        r.Flow.Handle(new MenuKey(MenuInput.Up, 'W'));
        Assert.Equal("WAA", r.Flow.Initials!.Text);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.EnterInitials, r.Flow.Current);
    }

    [Fact]
    public void Practice_games_never_record()
    {
        var r = new Rig();
        r.ToPlay(practice: true);
        r.G.Run!.World.Score = 900;
        r.Complete();
        Assert.Equal(Screen.MissionComplete, r.Flow.Current);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Title, r.Flow.Current);
        Assert.Empty(r.Scores.Entries);

        r.ToPlay(practice: true);
        r.G.Run!.World.Score = 900;
        r.PauseNow();
        r.QuitFromPause();
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Title, r.Flow.Current);
        Assert.Empty(r.Scores.Entries);
    }

    [Fact]
    public void Quit_confirm_says_whether_the_score_counts()
    {
        var c = new Rig();
        c.ToPause();
        c.Select("quit"); c.Key(MenuInput.Confirm);
        Assert.Equal("YOUR SCORE STILL COUNTS FOR THE HIGH SCORES.", c.Flow.Menu!.Lines[0]);

        var p = new Rig();
        p.ToPlay(practice: true);
        p.PauseNow();
        p.Select("quit"); p.Key(MenuInput.Confirm);
        Assert.Equal(Screen.QuitConfirm, p.Flow.Current);
        Assert.Equal("PRACTICE SCORES ARE NOT RECORDED.", p.Flow.Menu!.Lines[0]);
    }

    [Fact]
    public void Quitting_a_practice_from_its_briefing_leaves_no_restart()
    {
        var r = new Rig();
        r.Flow.StartGame(practice: true);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.QuitConfirm, r.Flow.Current);
        r.Key(MenuInput.Down); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        Assert.Null(r.G.Run);
        r.Letter('R');
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        Assert.False(r.G.RestartMission());
    }

    [Fact]
    public void A_repeated_notice_is_not_added_twice()
    {
        var r = new Rig();
        r.Flow.ShowNotice("COULD NOT SAVE SETTINGS: X");
        r.Flow.ShowNotice("COULD NOT SAVE SETTINGS: X");
        Assert.Equal("COULD NOT SAVE SETTINGS: X", r.Flow.Notice);
        r.Flow.ShowNotice("OTHER");
        Assert.Equal("COULD NOT SAVE SETTINGS: X / OTHER", r.Flow.Notice);
    }

    [Fact]
    public void R_restarts_only_in_practice()
    {
        var r = new Rig();
        r.ToPause();
        var before = r.G.Run!.World;
        r.Letter('R');
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Same(before, r.G.Run.World);

        var p = new Rig();
        p.ToPlay(practice: true);
        p.PauseNow();
        Assert.Equal("PRACTICE: ALPHA", p.Flow.Menu!.Lines[0]);
        Assert.Contains(p.Flow.Menu.Items, i => i.Label == "RESTART MISSION");
        var w = p.G.Run!.World;
        p.Letter('R');
        Assert.Equal(Screen.Play, p.Flow.Current);
        Assert.NotSame(w, p.G.Run!.World);
        Assert.Equal(RunState.Playing, p.G.Run.State);
    }

    [Fact]
    public void R_on_a_practice_result_screen_restarts_but_not_in_a_campaign()
    {
        var p = new Rig();
        p.ToPlay(practice: true);
        p.Settings.SchemeIndex = 1;
        p.Complete();
        Assert.Equal(Screen.MissionComplete, p.Flow.Current);
        var w = p.G.Run!.World;
        p.Letter('r');
        Assert.Equal(Screen.Play, p.Flow.Current);
        Assert.NotSame(w, p.G.Run!.World);
        Assert.Equal(1, p.G.Run.World.SchemeIndex);
        Assert.Equal(GameStage.Playing, p.G.Stage);

        p.PauseNow();
        p.QuitFromPause();
        Assert.Equal(Screen.GameOver, p.Flow.Current);
        w = p.G.Run!.World;
        p.Letter('R');
        Assert.Equal(Screen.Play, p.Flow.Current);
        Assert.NotSame(w, p.G.Run!.World);

        var c = new Rig();
        c.ToPlay();
        c.Complete();
        var cw = c.G.Run!.World;
        c.Letter('R');
        Assert.Equal(Screen.MissionComplete, c.Flow.Current);
        Assert.Same(cw, c.G.Run!.World);
    }

    [Fact]
    public void Settings_change_and_save_on_back()
    {
        var r = new Rig();
        r.ToPause();
        r.Select("settings"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Settings, r.Flow.Current);
        r.Select("text"); r.Key(MenuInput.Right);
        Assert.Equal(TextSize.Large, r.Settings.TextSize);
        Assert.Equal(1.25, r.Settings.TextScale);
        Assert.Equal("LARGE", r.Flow.Menu!.Current.Value);
        r.Select("steering"); r.Key(MenuInput.Right);
        Assert.False(r.Feel.SpeedSensitiveSteering);
        Assert.Equal("OFF", r.Flow.Menu!.Current.Value);
        r.Select("scheme"); r.Key(MenuInput.Right);
        Assert.Equal(1, r.G.Run!.World.SchemeIndex);
        Assert.Equal("SPEED LEVER", r.Flow.Menu!.Current.Value);
        r.Select("shake"); r.Key(MenuInput.Left);
        Assert.False(r.Settings.ScreenShake);
        Assert.Equal(0, r.Store.SettingsSaves);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Equal(1, r.Store.SettingsSaves);
        Assert.Equal(TextSize.Large, r.Store.LastSettings!.TextSize);
    }

    [Fact]
    public void Return_stack_unwinds()
    {
        var r = new Rig();
        r.ToPause();
        r.Select("settings"); r.Key(MenuInput.Confirm);
        r.Select("keys"); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Controls, r.Flow.Current);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Settings, r.Flow.Current);
        Assert.Equal("keys", r.Flow.Menu!.Current.Id);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Equal("settings", r.Flow.Menu!.Current.Id);
    }

    [Fact]
    public void Controls_returns_on_P_help_and_pause()
    {
        var r = new Rig();
        r.Key(MenuInput.Help);
        Assert.Equal(Screen.Controls, r.Flow.Current);
        r.Letter('P');
        Assert.Equal(Screen.Title, r.Flow.Current);
        r.Key(MenuInput.Help); r.Key(MenuInput.Help);
        Assert.Equal(Screen.Title, r.Flow.Current);
        r.Key(MenuInput.Help); r.Key(MenuInput.Pause);
        Assert.Equal(Screen.Title, r.Flow.Current);
    }

    [Fact]
    public void A_failed_save_shows_a_notice()
    {
        var r = new Rig();
        r.Store.FailSaves = true;
        r.Select("settings"); r.Key(MenuInput.Confirm);
        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Title, r.Flow.Current);
        Assert.Contains("COULD NOT SAVE SETTINGS", r.Flow.Notice);
        r.Key(MenuInput.Help);   // the notice has now been shown on a screen, so the next change clears it
        Assert.Null(r.Flow.Notice);
    }

    [Fact]
    public void Notices_join_and_clear_after_being_shown()
    {
        var r = new Rig();
        r.Flow.ShowNotice("ONE");
        r.Flow.ShowNotice("TWO");
        Assert.Equal("ONE / TWO", r.Flow.Notice);
        r.Key(MenuInput.Help);
        Assert.Null(r.Flow.Notice);
    }

    [Fact]
    public void Failed_saves_at_initials_join_both_notices()
    {
        var r = new Rig();
        r.ToPlay();
        r.G.Run!.World.Score = 500;
        r.PauseNow();
        r.QuitFromPause();
        r.Key(MenuInput.Confirm);
        r.Store.FailSaves = true;
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.HighScores, r.Flow.Current);
        Assert.Contains(" / ", r.Flow.Notice);
        Assert.Equal(1, r.Flow.NewRank);
    }

    [Fact]
    public void Changing_scheme_in_play_saves_only_when_it_changed()
    {
        var r = new Rig();
        r.ToPlay();
        r.Flow.HandleGameAction(new GameAction(ActionKind.SelectScheme, 1));
        Assert.Equal(1, r.Settings.SchemeIndex);
        Assert.Equal(1, r.Store.SettingsSaves);
        r.Flow.HandleGameAction(new GameAction(ActionKind.SelectScheme, 1));   // no change
        Assert.Equal(1, r.Store.SettingsSaves);

        var w = new Rig();
        w.ToPlay();
        w.G.Run!.World.Player.Armor = 0;
        w.Tick();
        Assert.Equal(RunState.Respawning, w.G.Run.State);
        w.Flow.HandleGameAction(new GameAction(ActionKind.SelectScheme, 1));   // the run drops it
        Assert.Equal(0, w.Settings.SchemeIndex);
        Assert.Equal(0, w.Store.SettingsSaves);
    }

    [Fact]
    public void Every_screen_leads_back_to_title_or_play()
    {
        var starts = new Dictionary<Screen, Action<Rig>>
        {
            [Screen.Title] = _ => { },
            [Screen.Briefing] = r => r.Flow.StartGame(),
            [Screen.Pause] = r => r.ToPause(),
            [Screen.Settings] = r => { r.Select("settings"); r.Key(MenuInput.Confirm); },
            [Screen.Controls] = r => r.Key(MenuInput.Help),
            [Screen.QuitConfirm] = r => { r.Flow.StartGame(); r.Key(MenuInput.Back); },
            [Screen.MissionComplete] = r => { r.ToPlay(); r.Complete(); },
            [Screen.GameOver] = r => { r.Flow.StartGame(); r.Key(MenuInput.Back); r.Key(MenuInput.Down); r.Key(MenuInput.Confirm); },
            [Screen.Finished] = r => { r.ToPlay(); r.Complete(); r.Key(MenuInput.Confirm); r.Key(MenuInput.Confirm); r.Complete(); r.Key(MenuInput.Confirm); },
            [Screen.EnterInitials] = r =>
            {
                r.ToPlay(); r.G.Run!.World.Score = 500; r.Complete();
                r.Key(MenuInput.Confirm); r.Key(MenuInput.Confirm); r.Complete(); r.Key(MenuInput.Confirm);
                r.Key(MenuInput.Confirm);
            },
            [Screen.HighScores] = r => { r.Select("scores"); r.Key(MenuInput.Confirm); },
        };
        foreach (var (screen, setup) in starts)
        {
            var r = new Rig();
            setup(r);
            Assert.True(r.Flow.Current == screen, $"setup for {screen} reached {r.Flow.Current}");
            for (int i = 0; i < 6 && r.Flow.Current is not (Screen.Title or Screen.Play); i++)
            {
                var was = r.Flow.Current;
                r.Key(MenuInput.Confirm);
                if (r.Flow.Current == was) r.Key(MenuInput.Back);
            }
            Assert.True(r.Flow.Current is Screen.Title or Screen.Play, $"{screen} ended on {r.Flow.Current}");
        }
    }

    [Fact]
    public void Focus_lost_only_matters_in_play()
    {
        var r = new Rig();
        r.Flow.FocusLost();
        Assert.Equal(Screen.Title, r.Flow.Current);
        r.Flow.StartGame();
        r.Flow.FocusLost();
        Assert.Equal(Screen.Briefing, r.Flow.Current);

        r.Key(MenuInput.Confirm);
        r.Flow.FocusLost();
        Assert.Equal(Screen.Play, r.Flow.Current);
        Assert.Equal(RunState.Paused, r.G.Run!.State);
        r.Tick();
        Assert.Equal(Screen.Pause, r.Flow.Current);
    }

    [Fact]
    public void Focus_lost_through_a_respawn_pauses_when_it_ends()
    {
        var r = new Rig();
        r.ToPlay();
        r.G.Run!.World.Player.Armor = 0;
        r.Tick();
        Assert.Equal(RunState.Respawning, r.G.Run.State);
        for (int i = 0; i < 40 && r.Flow.Current == Screen.Play; i++)
        {
            r.Flow.FocusLost();
            r.Flow.Update(ControlFrame.Empty, 0.25);
        }
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Equal(RunState.Paused, r.G.Run.State);
    }

    [Fact]
    public void Version_goes_up_on_visible_changes()
    {
        var r = new Rig();
        int v = r.Flow.Version;
        r.Key(MenuInput.Down);
        Assert.True(r.Flow.Version > v);
    }
}
