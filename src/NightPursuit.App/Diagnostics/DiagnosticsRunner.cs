using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NightPursuit.App.Rendering;
using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

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
        if (scene != "default" && scene != "close-cars")
        {
            Error($"Unknown scene '{scene}'. Known scenes: default, close-cars");
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
        var view = new GameView { Run = run, Width = Width, Height = Height };
        for (double t = 0; t < seconds; t += Dt)
        {
            run.Update(Autopilot.Next(run.World), Dt);
            view.Effects.Absorb(run.World);
            view.Effects.Update(Dt);
        }
        if (scene == "close-cars") PlaceCloseCars(run.World, view.Effects);

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

    /// <summary>Replaces whatever is on the road with one of each thing the renderers draw, at known distances from the player.</summary>
    static void PlaceCloseCars(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();

        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 22, p.Lateral + 4, p.Speed);
        runner.Armor = 3;   // half of 6, so its health bar shows
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, p.Distance + 70, p.Lateral - 3, p.Speed);
        var spinner = AgentCar.Create(w.NextId(), AgentKind.Pursuer, p.Distance + 140, p.Lateral, p.Speed);
        spinner.State = VehicleState.Spinning;
        spinner.SpinAngle = 0.7;
        var behind = AgentCar.Create(w.NextId(), AgentKind.Pursuer, p.Distance - 25, p.Lateral, p.Speed);
        w.Agents.AddRange(new[] { runner, blocker, spinner, behind });

        w.Hazards.Add(new Wreck { Distance = p.Distance + 50, Lateral = p.Lateral + 1, Width = 1.9, Length = 4.4, Burn = 0.6 });
        w.Hazards.Add(new OilSlick { Distance = p.Distance + 40, Lateral = p.Lateral - 2.5 });

        effects.Spawn(p.Distance + 30, p.Lateral - 1);
        effects.Update(0.3);

        w.Tracers.Add(NewTracer(p, 9, 0.7, 3));    // fresh: also shows the muzzle flash
        w.Tracers.Add(NewTracer(p, 60, -0.7, 150));
    }

    static Tracer NewTracer(PlayerCar p, double ahead, double side, double travel)
    {
        var pose = new RoadPose(p.Distance + ahead, p.Lateral + side, 0);
        return new Tracer { Pose = pose, PrevDistance = pose.Distance - 2.5, PrevLateral = pose.Lateral, Travel = travel, OwnerId = p.Id };
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
