using System.IO;
using System.Windows;
using System.Windows.Threading;
using NightPursuit.App.Diagnostics;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;

namespace NightPursuit.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostics run without any window and exit with their own code.
        if (e.Args.Any(a => a is "--snapshot" or "--stress" or "--perf"))
        {
            Shutdown(DiagnosticsRunner.Run(e.Args));
            return;
        }

        DispatcherUnhandledException += OnUnhandled;

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "content", "missions", "mission-01.json");
            var run = new MissionRun(MissionLoader.LoadFile(path), new FeelSettings(), seed: 1);
            new MainWindow(run).Show();
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

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Fail("Night Pursuit: something went wrong", e.Exception.ToString());
    }

    /// <summary>Shows a readable message instead of the default crash dialog, then exits with a failure code.</summary>
    void Fail(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
