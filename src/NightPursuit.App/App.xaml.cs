using System.IO;
using System.Windows;
using System.Windows.Threading;
using NightPursuit.App.Audio;
using NightPursuit.App.Diagnostics;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;

namespace NightPursuit.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostics run without any window and exit with their own code.
        if (e.Args.Any(a => a is "--snapshot" or "--stress" or "--perf" or "--sound-check"))
        {
            Shutdown(DiagnosticsRunner.Run(e.Args));
            return;
        }

        DispatcherUnhandledException += OnUnhandled;

        try
        {
            // One store for the whole session: it remembers which unreadable files it must never overwrite.
            // Both files are loaded before anything can save.
            var store = new FileGameStore(AppPaths.DataDirectory);
            var (settings, settingsProblem) = store.LoadSettings();
            var (scores, scoresProblem) = store.LoadHighScores();
            var feel = new FeelSettings { SpeedSensitiveSteering = settings.LighterSteering };

            // START GAME is always the real campaign; --mission name adds a practice game of that one mission.
            var campaign = new List<MissionDefinition>();
            foreach (string id in Campaign.MissionIds)
            {
                if (LoadMission(id) is not { } def) return;
                campaign.Add(def);
            }
            MissionDefinition? practice = null;
            if (MissionName(e.Args) is { } name && (practice = LoadMission(name)) is null) return;

            var flow = new ScreenFlow(
                isPractice => isPractice && practice is not null
                    ? new Game(new[] { practice }, feel, settings, practice: true)
                    : new Game(campaign, feel, settings),
                feel, settings, scores, store);
            if (practice is not null) flow.StartGame(practice: true);
            // After StartGame, so a load problem shows on the first screen (the title, or the practice briefing) instead of being cleared by it.
            foreach (string? problem in new[] { settingsProblem, scoresProblem })
                if (problem is not null) flow.ShowNotice(problem);

            // Sound never stops the game starting: if it cannot be built, the game runs silently with a notice.
            SoundSystem? sound = null;
            try
            {
                sound = new SoundSystem(Path.Combine(AppContext.BaseDirectory, "content"),
                                        practice is null ? campaign : campaign.Append(practice), openDevice: true);
                foreach (string problem in sound.Problems) flow.ShowNotice(problem);
            }
            catch (Exception ex)
            {
                string reason = ex.Message.Split('\n')[0].Trim().TrimEnd('.').ToUpperInvariant();
                flow.ShowNotice("SOUND IS OFF: " + (reason.Length == 0 ? "COULD NOT START" : reason.Length > 60 ? reason[..60] : reason));
            }

            new MainWindow(flow, sound).Show();
        }
        catch (MissionFormatException ex)
        {
            Fail("Night Pursuit: mission file problem", ex.Message);
        }
        catch (Exception ex)
        {
            Fail("Night Pursuit: could not start", ex.ToString());
        }
    }

    /// <summary>Loads content/missions/<paramref name="name"/>.json, or shows why not and returns null (the app is then shutting down).</summary>
    MissionDefinition? LoadMission(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "missions", name + ".json");
        if (!File.Exists(path))
        {
            Fail("Night Pursuit: mission not found", $"There is no mission called '{name}'.{Environment.NewLine}Looked for: {path}");
            return null;
        }
        return MissionLoader.LoadFile(path);
    }

    /// <summary><c>--mission name</c> picks a practice mission in content/missions (letters, digits and '-' only); null without it.</summary>
    static string? MissionName(string[] args)
    {
        int i = Array.IndexOf(args, "--mission");
        if (i < 0) return null;
        string name = i + 1 < args.Length ? args[i + 1] : "";
        if (name.Length == 0 || !name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            throw new MissionFormatException($"--mission needs a mission name of letters, digits and '-' only, e.g. mission-01 or proving-ground, not '{name}'");
        return name;
    }

    bool _failing;

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_failing) return;   // the message box pumps the dispatcher; a repeating error must not stack more boxes
        _failing = true;
        (MainWindow as NightPursuit.App.MainWindow)?.StopLoop();
        Fail("Night Pursuit: something went wrong", e.Exception.ToString());
    }

    /// <summary>Shows a readable message instead of the default crash dialog, then exits with a failure code.</summary>
    void Fail(string title, string message)
    {
        _failing = true;
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
