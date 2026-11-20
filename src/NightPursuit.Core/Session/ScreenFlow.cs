using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;

namespace NightPursuit.Core.Session;

public enum Screen { Title, Briefing, Play, Pause, Settings, Controls, QuitConfirm, MissionComplete, GameOver, Finished, EnterInitials, HighScores }

/// <summary>Which screen is showing and what each key does there. The game is stepped only while the screen is Play.</summary>
public sealed class ScreenFlow
{
    static readonly string[] TextSizeNames = { "NORMAL", "LARGE", "LARGEST" };

    readonly Func<bool, Game> _newGame;
    readonly IGameStore _store;
    readonly Stack<(Screen Screen, int Selected)> _returns = new();
    bool _pausedForHelp;
    bool _noticeShown;

    public ScreenFlow(Func<bool, Game> newGame, FeelSettings feel, GameSettings settings, HighScoreTable scores, IGameStore store)
    {
        _newGame = newGame;
        Feel = feel;
        Settings = settings;
        Scores = scores;
        _store = store;
        Feel.SpeedSensitiveSteering = Settings.LighterSteering;
        Go(Screen.Title);
    }

    public Screen Current { get; private set; }
    public Game? Game { get; private set; }
    public MenuModel? Menu { get; private set; }
    public InitialsEntry? Initials { get; private set; }
    /// <summary>The 1-based rank of the entry just added, or 0.</summary>
    public int NewRank { get; private set; }
    public string? Notice { get; private set; }
    /// <summary>Goes up on every visible change.</summary>
    public int Version { get; private set; }
    public bool ExitRequested { get; private set; }
    public bool ShowIntroHelp { get; private set; }
    public GameSettings Settings { get; }
    public HighScoreTable Scores { get; }
    public FeelSettings Feel { get; }

    public void IntroHelpSeen()
    {
        if (!ShowIntroHelp) return;
        ShowIntroHelp = false;
        Version++;
    }

    /// <summary>Shows a notice on the current screen (null clears it). Several notices join with " / ".</summary>
    public void ShowNotice(string? notice)
    {
        Notice = notice is null ? null : Notice is null ? notice : Notice + " / " + notice;
        _noticeShown = true;
        Version++;
    }

    // A notice raised while handling a key that changes screen: it is first shown on the next screen.
    void Post(string? problem)
    {
        if (problem is null) return;
        Notice = Notice is null ? problem : Notice + " / " + problem;
        _noticeShown = false;
    }

    public void StartGame(bool practice = false)
    {
        Game = _newGame(practice);
        Initials = null;
        NewRank = 0;
        ShowIntroHelp = false;
        _returns.Clear();
        _pausedForHelp = false;
        Go(Screen.Briefing);
    }

    // ---- screens ----

    void Go(Screen screen, int selected = 0)
    {
        Current = screen;
        Menu = screen switch
        {
            Screen.Title => TitleMenu(selected),
            Screen.Pause => PauseMenu(selected),
            Screen.Settings => SettingsMenu(selected),
            Screen.QuitConfirm => new MenuModel("QUIT THIS GAME?", new[] { "YOUR SCORE STILL COUNTS FOR THE HIGH SCORES." },
                new[] { new MenuEntry("no", "NO, KEEP PLAYING"), new MenuEntry("yes", "YES, QUIT") }, selected),
            _ => null,
        };
        if (Notice is not null)
        {
            if (_noticeShown) { Notice = null; _noticeShown = false; }
            else _noticeShown = true;
        }
        Version++;
    }

    static MenuModel TitleMenu(int selected) => new("NIGHT PURSUIT", new[] { "SIX MISSIONS. THREE SPARE CARS. ONE LONG NIGHT." },
        new[]
        {
            new MenuEntry("start", "START GAME"), new MenuEntry("scores", "HIGH SCORES"), new MenuEntry("settings", "SETTINGS"),
            new MenuEntry("controls", "CONTROLS"), new MenuEntry("quit", "QUIT"),
        }, selected);

    MenuModel PauseMenu(int selected)
    {
        var g = Game!;
        string line = g.Practice
            ? "PRACTICE: " + g.Mission.Title.ToUpperInvariant()
            : $"MISSION {g.MissionIndex + 1} OF {g.Missions.Count}: {g.Mission.Title.ToUpperInvariant()}";
        var items = new List<MenuEntry> { new("resume", "RESUME") };
        if (g.Practice) items.Add(new("restart", "RESTART MISSION"));
        items.Add(new("settings", "SETTINGS"));
        items.Add(new("controls", "CONTROLS"));
        items.Add(new("quit", "QUIT TO TITLE"));
        return new MenuModel("PAUSED", new[] { line }, items, selected);
    }

    MenuModel SettingsMenu(int selected) => new("SETTINGS", Array.Empty<string>(), new[]
    {
        new MenuEntry("scheme", "CONTROLS", Settings.SchemeIndex == 1 ? "SPEED LEVER" : "RAMP PEDAL + CRUISE"),
        new MenuEntry("steering", "LIGHTER STEERING AT SPEED", Settings.LighterSteering ? "ON" : "OFF"),
        new MenuEntry("shake", "SCREEN SHAKE", Settings.ScreenShake ? "ON" : "OFF"),
        new MenuEntry("text", "TEXT SIZE", TextSizeNames[(int)Settings.TextSize]),
        new MenuEntry("keys", "KEY BINDINGS"),
        new MenuEntry("back", "BACK"),
    }, selected);

    /// <summary>Opens a screen that returns here (Settings, Controls, QuitConfirm, HighScores).</summary>
    void Open(Screen screen)
    {
        _returns.Push((Current, Menu?.Selected ?? 0));
        Go(screen);
    }

    void Return()
    {
        if (_returns.Count == 0) { Go(Game is null ? Screen.Title : Screen.Briefing); return; }
        var (screen, selected) = _returns.Pop();
        if (screen == Screen.Play && _pausedForHelp)
        {
            _pausedForHelp = false;
            Game?.Run?.Handle(new GameAction(ActionKind.Pause));
        }
        Go(screen, selected);
    }

    void ToTitle()
    {
        Game = null;
        Initials = null;
        NewRank = 0;
        _returns.Clear();
        Go(Screen.Title);
    }

    // ---- keys ----

    public void Handle(MenuKey key)
    {
        switch (Current)
        {
            case Screen.Play: return;
            case Screen.Title: OnTitle(key); break;
            case Screen.Briefing: OnBriefing(key); break;
            case Screen.Pause: OnPause(key); break;
            case Screen.Settings: OnSettings(key); break;
            case Screen.Controls:
                if (key.Input is MenuInput.Confirm or MenuInput.Back or MenuInput.Help or MenuInput.Pause || IsLetter(key, 'P')) Return();
                break;
            case Screen.QuitConfirm: OnQuitConfirm(key); break;
            case Screen.MissionComplete: OnMissionComplete(key); break;
            case Screen.GameOver:
            case Screen.Finished: OnEnd(key); break;
            case Screen.EnterInitials: OnInitials(key); break;
            case Screen.HighScores:
                if (key.Input is MenuInput.Confirm or MenuInput.Back)
                {
                    if (_returns.Count > 0) Return(); else ToTitle();
                }
                break;
        }
        Version++;
    }

    static bool IsLetter(MenuKey key, char c) => key.Input == MenuInput.Letter && char.ToUpperInvariant(key.Letter) == c;

    bool MoveMenu(MenuKey key)
    {
        if (Menu is null) return false;
        if (key.Input == MenuInput.Up) { Menu.Move(-1); return true; }
        if (key.Input == MenuInput.Down) { Menu.Move(1); return true; }
        return false;
    }

    void OnTitle(MenuKey key)
    {
        if (MoveMenu(key)) return;
        if (key.Input == MenuInput.Help) { Open(Screen.Controls); return; }
        if (key.Input != MenuInput.Confirm) return;
        switch (Menu!.Current.Id)
        {
            case "start": StartGame(); break;
            case "scores": Open(Screen.HighScores); break;
            case "settings": Open(Screen.Settings); break;
            case "controls": Open(Screen.Controls); break;
            case "quit": ExitRequested = true; break;
        }
    }

    void OnBriefing(MenuKey key)
    {
        switch (key.Input)
        {
            case MenuInput.Confirm:
                var g = Game!;
                g.StartMission();
                ShowIntroHelp = g.MissionIndex == 0;
                Go(Screen.Play);
                break;
            case MenuInput.Back: Open(Screen.QuitConfirm); break;
            case MenuInput.Help: Open(Screen.Controls); break;
        }
    }

    void ResumeFromPause()
    {
        Game?.Run?.Handle(new GameAction(ActionKind.Pause));
        _returns.Clear();
        Go(Screen.Play);
    }

    void Restart()
    {
        Game!.RestartMission();
        _returns.Clear();
        Go(Screen.Play);
    }

    void OnPause(MenuKey key)
    {
        if (MoveMenu(key)) return;
        if (key.Input is MenuInput.Back or MenuInput.Pause || IsLetter(key, 'P')) { ResumeFromPause(); return; }
        if (key.Input == MenuInput.Help) { Open(Screen.Controls); return; }
        if (IsLetter(key, 'R')) { if (Game!.Practice) Restart(); return; }
        if (key.Input != MenuInput.Confirm) return;
        switch (Menu!.Current.Id)
        {
            case "resume": ResumeFromPause(); break;
            case "restart": Restart(); break;
            case "settings": Open(Screen.Settings); break;
            case "controls": Open(Screen.Controls); break;
            case "quit": Open(Screen.QuitConfirm); break;
        }
    }

    void OnSettings(MenuKey key)
    {
        if (MoveMenu(key)) return;
        if (key.Input == MenuInput.Back) { LeaveSettings(); return; }
        int delta = key.Input switch { MenuInput.Left => -1, MenuInput.Right => 1, MenuInput.Confirm => 1, _ => 0 };
        if (delta == 0) return;
        var m = Menu!;
        switch (m.Current.Id)
        {
            case "scheme": ChangeScheme(1 - Settings.SchemeIndex); break;
            case "steering":
                Settings.LighterSteering = !Settings.LighterSteering;
                Feel.SpeedSensitiveSteering = Settings.LighterSteering;
                break;
            case "shake": Settings.ScreenShake = !Settings.ScreenShake; break;
            case "text": Settings.TextSize = (TextSize)(((int)Settings.TextSize + delta + 3) % 3); break;
            case "keys": if (key.Input == MenuInput.Confirm) Open(Screen.Controls); return;
            case "back": if (key.Input == MenuInput.Confirm) LeaveSettings(); return;
            default: return;
        }
        Menu = SettingsMenu(m.Selected);
    }

    void ChangeScheme(int index)
    {
        Settings.SchemeIndex = index;
        // MissionRun.Handle passes a scheme change on only while Playing, and from Pause the run is Paused.
        Game?.Run?.World.Handle(new GameAction(ActionKind.SelectScheme, index));
    }

    void LeaveSettings()
    {
        Post(_store.SaveSettings(Settings.Clone()).Problem);
        Return();
    }

    void OnQuitConfirm(MenuKey key)
    {
        if (MoveMenu(key)) return;
        if (key.Input == MenuInput.Back) { Return(); return; }
        if (key.Input != MenuInput.Confirm) return;
        if (Menu!.Current.Id == "yes")
        {
            Game!.Quit();
            _returns.Clear();
            Go(Screen.GameOver);
        }
        else Return();
    }

    void OnMissionComplete(MenuKey key)
    {
        var g = Game!;
        if (IsLetter(key, 'R') && g.Practice) { Restart(); return; }
        if (key.Input != MenuInput.Confirm) return;
        if (g.Practice) { ToTitle(); return; }
        g.Continue();
        Go(g.Stage == GameStage.Finished ? Screen.Finished : Screen.Briefing);
    }

    void OnEnd(MenuKey key)
    {
        var g = Game!;
        if (IsLetter(key, 'R') && g.Practice && Current == Screen.GameOver) { Restart(); return; }
        if (key.Input != MenuInput.Confirm) return;
        if (g.Practice) { ToTitle(); return; }
        if (Scores.Qualifies(g.Score))
        {
            Initials = new InitialsEntry(Settings.LastInitials);
            Go(Screen.EnterInitials);
        }
        else
        {
            NewRank = 0;
            Go(Screen.HighScores);
        }
    }

    void OnInitials(MenuKey key)
    {
        var e = Initials!;
        if (key.Letter != '\0') { e.Type(key.Letter); return; }
        switch (key.Input)
        {
            case MenuInput.Up: e.Up(); break;
            case MenuInput.Down: e.Down(); break;
            case MenuInput.Left: e.Left(); break;
            case MenuInput.Right: e.Right(); break;
            case MenuInput.Delete: e.Delete(); break;
            case MenuInput.Confirm:
                var g = Game!;
                NewRank = Scores.Add(new HighScoreEntry(e.Text, g.Score, g.MissionReached, g.FinishedAll));
                Settings.LastInitials = e.Text;
                Post(_store.SaveSettings(Settings.Clone()).Problem);
                Post(_store.SaveHighScores(Scores).Problem);
                Go(Screen.HighScores);
                break;
        }
    }

    // ---- play ----

    public void HandleGameAction(GameAction a)
    {
        if (Current != Screen.Play || Game?.Run is not { } run) return;
        switch (a.Kind)
        {
            case ActionKind.ToggleHelp:
                _pausedForHelp = run.State == RunState.Playing;
                if (_pausedForHelp) run.Handle(new GameAction(ActionKind.Pause));
                Open(Screen.Controls);
                break;
            case ActionKind.SelectScheme:
                int before = run.World.SchemeIndex;
                Game.Handle(a);
                int after = run.World.SchemeIndex;
                if (after != before)
                {
                    Settings.SchemeIndex = after;
                    Post(_store.SaveSettings(Settings.Clone()).Problem);
                    _noticeShown = true;   // no screen change: it is on screen now
                    Version++;
                }
                break;
            default:
                Game.Handle(a);
                break;
        }
    }

    public void Update(ControlFrame held, double seconds)
    {
        if (Current != Screen.Play || Game is not { } g) return;
        g.Update(held, seconds);
        if (g.Stage == GameStage.MissionComplete) Go(Screen.MissionComplete);
        else if (g.Stage == GameStage.GameOver) Go(Screen.GameOver);
        else if (g.Stage == GameStage.Finished) Go(Screen.Finished);
        else if (g.Run?.State == RunState.Paused) Go(Screen.Pause);
    }

    public void FocusLost()
    {
        if (Current != Screen.Play || Game?.Run is not { } run) return;
        if (run.State == RunState.Playing) run.Handle(new GameAction(ActionKind.Pause));
    }
}
