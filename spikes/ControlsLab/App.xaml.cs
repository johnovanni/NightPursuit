using System.Windows;

namespace ControlsLab;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostic: "--snapshot out.png" simulates a few seconds and renders one frame to a PNG,
        // so the look can be checked without screen captures.
        if (Array.IndexOf(e.Args, "--snapshot") >= 0 || Array.IndexOf(e.Args, "--stress") >= 0
            || Array.IndexOf(e.Args, "--trace") >= 0 || Array.IndexOf(e.Args, "--kill-trace") >= 0 || Array.IndexOf(e.Args, "--kill-film") >= 0 || Array.IndexOf(e.Args, "--steer-test") >= 0)
        {
            Snapshot.Run(e.Args);
            Shutdown(Environment.ExitCode);
            return;
        }

        new MainWindow().Show();
    }
}
