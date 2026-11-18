using System.IO;
using System.Windows;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;

namespace NightPursuit.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Task 13 hook: route "--snapshot", "--stress" and "--perf" arguments to the diagnostics
        // runner here (run it, then Shutdown(exitCode) and return) before any window is created.

        MissionRun run;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "content", "missions", "mission-01.json");
            run = new MissionRun(MissionLoader.LoadFile(path), new FeelSettings(), seed: 1);
        }
        catch (MissionFormatException ex)
        {
            MessageBox.Show(ex.Message, "Night Pursuit: mission file problem", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        new MainWindow(run).Show();
    }
}
