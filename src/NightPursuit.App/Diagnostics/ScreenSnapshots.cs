using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NightPursuit.App.Rendering;
using NightPursuit.App.Screens;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Diagnostics;

/// <summary>
/// <c>--snapshot out.png --screen name[,name] [--mission m] [--size WxH] [--text-size normal|large|largest]</c>: drives a fresh
/// screen flow through its public API to each named screen and renders the game view with the screen host over it, as the
/// window shows them. The flow saves to a <see cref="MemoryGameStore"/> (never the real data folder) and starts with a sample
/// table of 7 high scores. Without <c>--mission</c> the game is the campaign; with it, a practice game of that mission.
/// With several screens, out.png becomes out-&lt;screen&gt;.png, as for <c>--scene</c>.
/// </summary>
public static class ScreenSnapshots
{
    const double Dt = 1.0 / 120;
    const double DriveSeconds = 12;   // as the --scene default: far enough along that the frozen road behind a screen is real road

    static readonly string[] ScreenNames = { "title", "settings", "pause", "quit", "briefing", "briefing-all", "controls", "complete", "gameover", "ended", "finished", "initials", "highscores" };
    const string BriefingAll = "briefing-all";
    static readonly string[] TextSizeNames = { "normal", "large", "largest" };

    // Made up for the pictures: initials, score, mission reached, finished all six.
    static readonly HighScoreEntry[] SampleScores =
    {
        new("ACE", 52400, 6, true), new("RJK", 41850, 5, false), new("MOX", 33120, 4, false), new("DEV", 26700, 4, false),
        new("LUZ", 18900, 3, false), new("TOM", 9450, 2, false), new("KAI", 4200, 1, false),
    };

    public static int Run(string path, string[] args)
    {
        string[] requested = (DiagnosticsRunner.Arg(args, "--screen") ?? "").Split(',');
        foreach (string screen in requested)
            if (Array.IndexOf(ScreenNames, screen) < 0)
            {
                DiagnosticsRunner.Error($"Unknown screen '{screen}'. Known screens: {string.Join(", ", ScreenNames)}");
                return 2;
            }
        // briefing-all is one briefing per campaign mission: briefing-1 to briefing-N (so out.png becomes out-briefing-1.png ...).
        string[] screens = requested.SelectMany(n => n == BriefingAll
            ? Enumerable.Range(1, Campaign.MissionIds.Length).Select(i => "briefing-" + i)
            : new[] { n }).ToArray();

        string? mission = DiagnosticsRunner.Arg(args, "--mission");
        if (mission is not null && (mission.Length == 0 || !mission.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')))
        {
            DiagnosticsRunner.Error($"--mission needs a mission name of letters, digits and '-' only, e.g. mission-01, not '{mission}'");
            return 2;
        }

        int width = 1280, height = 720;
        string? sizeText = DiagnosticsRunner.Arg(args, "--size");
        if (sizeText is not null)
        {
            string[] parts = sizeText.Split('x');
            if (!(parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
                  && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
                  && width is >= 1 and <= 8192 && height is >= 1 and <= 8192))
            {
                DiagnosticsRunner.Error($"--size needs WIDTHxHEIGHT from 1x1 to 8192x8192, e.g. --size 320x200, not '{sizeText}'");
                return 2;
            }
        }

        string textSizeText = DiagnosticsRunner.Arg(args, "--text-size") ?? "normal";
        int textSize = Array.IndexOf(TextSizeNames, textSizeText);
        if (textSize < 0)
        {
            DiagnosticsRunner.Error($"--text-size needs one of {string.Join(", ", TextSizeNames)}, not '{textSizeText}'");
            return 2;
        }

        var campaign = Campaign.MissionIds.Select(LoadMission).ToArray();
        MissionDefinition? practice = mission is null ? null : LoadMission(mission);

        foreach (string screen in screens)
        {
            string file = screens.Length > 1
                ? Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-" + screen + Path.GetExtension(path))
                : path;
            var view = new GameView { ShowHelp = false };
            var flow = NewFlow(campaign, practice, (TextSize)textSize);
            DriveTo(flow, screen, view, practice is not null, screen.StartsWith("briefing-", StringComparison.Ordinal) ? int.Parse(screen[9..], CultureInfo.InvariantCulture) - 1 : -1);
            Render(flow, view, width, height, file);
        }
        return 0;
    }

    static ScreenFlow NewFlow(MissionDefinition[] campaign, MissionDefinition? practice, TextSize textSize)
    {
        var settings = new GameSettings { TextSize = textSize };
        var feel = new FeelSettings { SpeedSensitiveSteering = settings.LighterSteering };
        var scores = new HighScoreTable();
        foreach (var entry in SampleScores) scores.Add(entry);
        return new ScreenFlow(
            isPractice => isPractice && practice is not null
                ? new Game(new[] { practice }, feel, settings, practice: true)
                : new Game(campaign, feel, settings),
            feel, settings, scores, new MemoryGameStore());
    }

    /// <summary>Presses the keys a player would to reach <paramref name="screen"/>, from the title.</summary>
    static void DriveTo(ScreenFlow flow, string screen, GameView view, bool practice, int briefingOf)
    {
        if (briefingOf >= 0)
        {
            Choose(flow, "start");
            Expect(flow, Screen.Briefing, screen);
            ShowMissionBriefing(flow, briefingOf);
            return;
        }
        switch (screen)
        {
            case "title":
                break;
            case "briefing":
                if (practice) flow.StartGame(practice: true);
                else Choose(flow, "start");
                Expect(flow, Screen.Briefing, screen);
                break;
            case "settings":
                Choose(flow, "settings");
                Expect(flow, Screen.Settings, screen);
                break;
            case "pause":
            case "quit":
                Play(flow, view, practice);
                flow.HandleGameAction(new GameAction(ActionKind.Pause));
                flow.Update(ControlFrame.Empty, 0);
                Expect(flow, Screen.Pause, screen);
                if (screen == "quit")
                {
                    Choose(flow, "quit");
                    Expect(flow, Screen.QuitConfirm, screen);
                }
                break;
            case "controls":
                Play(flow, view, practice);
                flow.HandleGameAction(new GameAction(ActionKind.ToggleHelp));   // F1 in play: pauses and opens the controls
                Expect(flow, Screen.Controls, screen);
                break;
            case "complete":
                Play(flow, view, practice);
                ForceStage(flow, GameStage.MissionComplete, practice ? 0 : 3, 31250);
                break;
            case "gameover":
                Play(flow, view, practice);
                ForceStage(flow, GameStage.GameOver, practice ? 0 : 2, 7200);
                break;
            case "ended":
                Play(flow, view, practice);
                flow.HandleGameAction(new GameAction(ActionKind.Pause));
                flow.Update(ControlFrame.Empty, 0);
                Choose(flow, "quit");
                Choose(flow, "yes");   // "YES, QUIT"
                Expect(flow, Screen.GameOver, screen);
                break;
            case "finished":
                Play(flow, view, practice);
                ForceStage(flow, GameStage.Finished, practice ? 0 : 5, 61000);
                break;
            case "initials":
            case "highscores":
                Play(flow, view, practice);
                ForceStage(flow, GameStage.GameOver, practice ? 0 : 2, 30000);   // a score that makes rank 4 of the sample table
                flow.Handle(new MenuKey(MenuInput.Confirm));
                Expect(flow, Screen.EnterInitials, screen);
                flow.Handle(new MenuKey(MenuInput.Letter, 'M'));
                flow.Handle(new MenuKey(MenuInput.Letter, 'J'));
                flow.Handle(new MenuKey(MenuInput.Left));   // MJA, cursor on the J
                if (screen == "highscores")
                {
                    flow.Handle(new MenuKey(MenuInput.Confirm));
                    Expect(flow, Screen.HighScores, screen);
                }
                break;
        }
    }

    /// <summary>
    /// Puts the playing game into a result stage as the player would see it, without playing it: the diagnostic sets the game's own
    /// state (stage, mission index, score, a sample bonus), then lets the flow notice it on its next update.
    /// </summary>
    static void ForceStage(ScreenFlow flow, GameStage stage, int missionIndex, int score)
    {
        var game = flow.Game!;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(Game).GetProperty(nameof(Game.MissionIndex), Any)!.SetValue(game, missionIndex);
        typeof(Game).GetField("_score", Any)!.SetValue(game, score);
        typeof(Game).GetField("_spareCars", Any)!.SetValue(game, 2);
        if (stage == GameStage.MissionComplete)
            typeof(Game).GetProperty(nameof(Game.LastBonus), Any)!.SetValue(game, MissionBonus.For(game.Mission, 62.7, carLost: false));
        typeof(Game).GetProperty(nameof(Game.Stage), Any)!.SetValue(game, stage);
        flow.Update(ControlFrame.Empty, 0);
        Expect(flow, stage switch { GameStage.MissionComplete => Screen.MissionComplete, GameStage.GameOver => Screen.GameOver, _ => Screen.Finished }, stage.ToString());
    }

    /// <summary>
    /// Starts the game, confirms the briefing and drives a while on the autopilot. The campaign starts from the title's
    /// START GAME; a practice game (--mission) is started as the App starts it.
    /// </summary>
    static void Play(ScreenFlow flow, GameView view, bool practice)
    {
        if (practice) flow.StartGame(practice: true);
        else Choose(flow, "start");
        Expect(flow, Screen.Briefing, "briefing");
        flow.Handle(new MenuKey(MenuInput.Confirm));
        Expect(flow, Screen.Play, "play");
        for (double t = 0; t < DriveSeconds; t += Dt)
        {
            var world = flow.Game!.Run!.World;
            flow.Update(Autopilot.Next(world), Dt);
            if (flow.ShowIntroHelp) flow.IntroHelpSeen();   // the autopilot presses gas
            view.Effects.Absorb(flow.Game.Run.World);
            view.Effects.Update(Dt);
        }
        Expect(flow, Screen.Play, "play");
    }

    /// <summary>
    /// Makes the campaign game's briefing the one for mission <paramref name="index"/> (0-based), as a player would see it after
    /// finishing the missions before it, without playing them: the diagnostic sets the game's own state (a sample score and
    /// spare cars from mission 2 on). The last one also shows a sample notice, to check that a notice fits under the rest.
    /// </summary>
    static void ShowMissionBriefing(ScreenFlow flow, int index)
    {
        var game = flow.Game!;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(Game).GetProperty(nameof(Game.MissionIndex), Any)!.SetValue(game, index);
        if (index > 0)
        {
            typeof(Game).GetField("_score", Any)!.SetValue(game, 4200 * index);
            typeof(Game).GetField("_spareCars", Any)!.SetValue(game, 2);
        }
        if (index == game.Missions.Count - 1) flow.ShowNotice("SAMPLE NOTICE: THIS LINE IS ONLY IN THE DIAGNOSTIC PICTURE.");
    }

    /// <summary>Moves the menu down to the item <paramref name="id"/> and confirms it.</summary>
    static void Choose(ScreenFlow flow, string id)
    {
        for (int i = 0; flow.Menu!.Current.Id != id; i++)
        {
            if (i > flow.Menu.Items.Count) throw new InvalidOperationException($"the {flow.Current} menu has no '{id}' item");
            flow.Handle(new MenuKey(MenuInput.Down));
        }
        flow.Handle(new MenuKey(MenuInput.Confirm));
    }

    static void Expect(ScreenFlow flow, Screen expected, string step)
    {
        if (flow.Current != expected)
            throw new InvalidOperationException($"driving to '{step}' ended on the {flow.Current} screen, not {expected}");
    }

    /// <summary>Renders the game view and the screen host in one grid, as the window stacks them.</summary>
    static void Render(ScreenFlow flow, GameView view, int width, int height, string path)
    {
        view.Run = flow.Game?.Run;
        view.ShowHelp = flow.ShowIntroHelp && flow.Current == Screen.Play;
        view.ShakeEnabled = flow.Settings.ScreenShake;
        view.Notice = flow.Current == Screen.Play ? flow.Notice : null;
        view.Covered = flow.Current != Screen.Play;
        var host = new ScreenHost();
        host.Show(flow);
        var grid = new Grid { Width = width, Height = height, Background = Brushes.Black };
        grid.Children.Add(view);
        grid.Children.Add(host);

        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    static MissionDefinition LoadMission(string name) =>
        MissionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "content", "missions", name + ".json"));
}
