using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NightPursuit.App.Rendering;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;

namespace NightPursuit.App.Diagnostics;

/// <summary>
/// Command-line diagnostics run instead of the game window:
/// <c>--snapshot out.png [--seconds S] [--scene name]</c>, plus <c>--stress</c> and <c>--perf</c> (added later).
/// </summary>
public static class DiagnosticsRunner
{
    const double Dt = 1.0 / 120;
    const int Width = 1280, Height = 720;

    /// <summary>Runs the requested diagnostic and returns the process exit code (0 ok, 1 failed, 2 bad arguments).</summary>
    public static int Run(string[] args)
    {
        _logPath = LogPathFor(args);
        try
        {
            if (Has(args, "--snapshot")) return Snapshot(args);
            foreach (var name in new[] { "--stress", "--perf" })
                if (Has(args, name))
                {
                    Error($"{name}: not implemented yet");
                    return 2;
                }
            return 2;
        }
        catch (MissionFormatException ex)
        {
            Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Error($"Diagnostics failed: {ex}");
            return 1;
        }
    }

    static string _logPath = "diagnostics.log";

    /// <summary>Reports an error on stderr and in the log file (a WinExe has no visible console).</summary>
    static void Error(string message)
    {
        Console.Error.WriteLine(message);
        try { File.AppendAllText(_logPath, message + Environment.NewLine); }
        catch (Exception) { /* nothing more we can do */ }
    }

    static string LogPathFor(string[] args)
    {
        string? output = Arg(args, "--snapshot");
        return output is null || output.StartsWith("--") ? "diagnostics.log" : output + ".log";
    }

    static int Snapshot(string[] args)
    {
        string? path = Arg(args, "--snapshot");
        if (path is null || path.StartsWith("--"))
        {
            Error("--snapshot needs an output file name, e.g. --snapshot snapshots/frame.png");
            return 2;
        }
        string scene = Arg(args, "--scene") ?? "default";
        if (scene != "default")
        {
            Error($"Unknown scene '{scene}'. Known scenes: default");
            return 2;
        }
        double seconds = 12;
        string? secondsText = Arg(args, "--seconds");
        if (secondsText is not null
            && !(double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                 && double.IsFinite(seconds) && seconds >= 0 && seconds <= 3600))
        {
            Error($"--seconds needs a number of seconds from 0 to 3600, not '{secondsText}'");
            return 2;
        }

        var run = NewRun();
        for (double t = 0; t < seconds; t += Dt) run.Update(Autopilot.Next(run.World), Dt);

        var view = new GameView { Run = run, Width = Width, Height = Height };
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
        return 0;
    }

    static MissionRun NewRun()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "missions", "mission-01.json");
        return new MissionRun(MissionLoader.LoadFile(path), new FeelSettings(), seed: 1);
    }

    static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
