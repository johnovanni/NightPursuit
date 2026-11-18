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
/// <c>--snapshot out.png [--seconds S] [--scene name] [--size WxH]</c>, <c>--stress report.txt</c> and <c>--perf report.txt</c>.
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
            if (Has(args, "--stress")) return Stress(args);
            if (Has(args, "--perf")) return Perf(args);
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

    static readonly string[] Scenes = { "default", "close-cars", "offroad", "uphill", "wreck" };

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
        string? output = Arg(args, "--snapshot") ?? Arg(args, "--stress") ?? Arg(args, "--perf");
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
        if (Array.IndexOf(Scenes, scene) < 0)
        {
            Error($"Unknown scene '{scene}'. Known scenes: {string.Join(", ", Scenes)}");
            return 2;
        }
        int width = Width, height = Height;
        string? sizeText = Arg(args, "--size");
        if (sizeText is not null)
        {
            string[] parts = sizeText.Split('x');
            if (!(parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
                  && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
                  && width is >= 1 and <= 8192 && height is >= 1 and <= 8192))
            {
                Error($"--size needs WIDTHxHEIGHT from 1x1 to 8192x8192, e.g. --size 320x200, not '{sizeText}'");
                return 2;
            }
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
        var view = new GameView { Run = run, Width = width, Height = height, ShowHelp = false };
        for (double t = 0; t < seconds; t += Dt)
        {
            run.Update(Autopilot.Next(run.World), Dt);
            view.Effects.Absorb(run.World);
            view.Effects.Update(Dt);
        }
        if (scene == "close-cars") PlaceCloseCars(run.World, view.Effects);
        else if (scene == "offroad") PlaceOffRoad(run);
        else if (scene == "uphill") PlaceUphill(run, width);
        else if (scene == "wreck") PlaceWrecks(run.World, view.Effects);

        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
        return 0;
    }

    // ---- --stress -------------------------------------------------------------------------------------------

    static readonly (int W, int H)[] StressSizes = { (960, 540), (320, 200), (2560, 600) };
    static readonly double[] StressDistances = { -200, -60, -20, 3, 12, 30, 80, 200, 400, 700, 1000, 1300, 1490 };
    static readonly RunState[] StressStates =
        { RunState.Playing, RunState.Playing, RunState.Paused, RunState.Respawning, RunState.Completed, RunState.GameOver };

    /// <summary>
    /// Renders every 10 m of mission 1's road at three window sizes (and 0 x 0) with a crowd of cars, slicks, wrecks,
    /// tracers and explosions, cycling the screen-fixed states (run-state overlays, help, messages, hit flash,
    /// locked guns, off-road) by frame number. Any exception counts as a failure.
    /// </summary>
    static int Stress(string[] args)
    {
        string path = Arg(args, "--stress") ?? "stress.txt";
        var report = new System.Text.StringBuilder();
        var failureMessages = new List<string>();
        int totalFrames = 0, totalFailures = 0;

        foreach (var (width, height) in StressSizes)
        {
            var run = NewRun();
            var view = new GameView { Run = run, Width = width, Height = height };
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            int frames = 0, failures = 0;
            var sizeClock = System.Diagnostics.Stopwatch.StartNew();

            for (double z = 0; z < run.World.Track.Length; z += 10, frames++)
            {
                if (frames % 50 == 0) Progress($"stress {width}x{height}: frame {frames}, {sizeClock.Elapsed.TotalSeconds:0.0} s");
                try
                {
                    PlaceStressFrame(run, view, z, frames);
                    int before = view.RenderCount;
                    view.InvalidateVisual();
                    view.UpdateLayout();
                    bitmap.Clear();
                    bitmap.Render(view);
                    if (view.RenderCount == before) throw new InvalidOperationException("the view was not redrawn");
                }
                catch (Exception ex)
                {
                    failures++;
                    if (failureMessages.Count < 10) failureMessages.Add($"{width}x{height} z={z}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            report.AppendLine($"{width}x{height}: {frames} frames, {failures} failures, {sizeClock.Elapsed.TotalSeconds:0.0} s");
            totalFrames += frames;
            totalFailures += failures;
        }

        // A minimised window: the view has no size at all, so drawing must simply do nothing.
        {
            int failures = 0;
            try
            {
                var view = new GameView { Run = NewRun(), Width = 0, Height = 0 };
                view.Measure(new Size(0, 0));
                view.Arrange(new Rect(0, 0, 0, 0));
                view.InvalidateVisual();
                view.UpdateLayout();
                new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32).Render(view);   // a bitmap cannot be 0 x 0
            }
            catch (Exception ex)
            {
                failures++;
                failureMessages.Add($"0x0: {ex.GetType().Name}: {ex.Message}");
            }
            report.AppendLine($"0x0: 1 frame, {failures} failures");
            totalFrames++;
            totalFailures += failures;
        }

        report.Insert(0, $"stress: {totalFrames} frames, {totalFailures} failures{Environment.NewLine}");
        if (failureMessages.Count > 0)
        {
            report.AppendLine("first failures:");
            foreach (var m in failureMessages) report.AppendLine("  " + m);
        }
        WriteReport(path, report.ToString());
        return totalFailures == 0 ? 0 : 1;
    }

    /// <summary>Sets up one stress frame at road position <paramref name="z"/>; <paramref name="frame"/> picks which screen-fixed variant to show.</summary>
    static void PlaceStressFrame(MissionRun run, GameView view, double z, int frame)
    {
        var w = run.World;
        var p = w.Player;
        p.Distance = z;
        p.Lateral = Math.Sin(z / 300) * 6;
        p.Heading = Math.Sin(z / 170) * 0.35;   // nose up to 20 degrees either way
        p.Speed = 40;
        p.State = VehicleState.Driving;
        p.SnapPrev();   // a teleport: no interpolation from the old pose
        w.View.Build(w.Track, z);

        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        w.RecentMessages.Clear();
        view.Effects.Clear();
        for (int k = 0; k < StressDistances.Length; k++)
        {
            double d = z + StressDistances[k];
            double x = (k % 5 - 2) * 4.5;
            var car = AgentCar.Create(w.NextId(), (AgentKind)((k + frame) % 3), d, x, 30);
            car.Armor = car.Stats.Armor / 3;
            if (k % 4 == 0) { car.State = VehicleState.Spinning; car.SpinAngle = k; car.SnapPrev(); }
            if (k % 5 == 3) car.HitFlash = 0.1;
            w.Agents.Add(car);
            w.Hazards.Add(new OilSlick { Distance = d, Lateral = -x });
            w.Hazards.Add(new Wreck { Distance = d + 7, PrevDistance = d + 7, Lateral = x / 2, Width = 1.9, Length = 4.4, Burn = k * 0.7, Seed = 100 + k });
            var pose = new RoadPose(d, x / 2, (k % 3 - 1) * 0.2);
            w.Tracers.Add(new Tracer { Pose = pose, PrevDistance = d - 2.5, PrevLateral = pose.Lateral, PrevHeading = pose.Heading, Travel = k * 25, OwnerId = k % 2 == 0 ? p.Id : car.Id });
            view.Effects.Spawn(d, x);
        }
        view.Effects.Update(0.4);

        // Screen-fixed variants, cycling with the frame number.
        var state = StressStates[frame % StressStates.Length];
        run.ForceStateForDiagnostics(RunState.Playing);
        if (state == RunState.Paused) run.Handle(new GameAction(ActionKind.Pause));
        else if (state != RunState.Playing) run.ForceStateForDiagnostics(state, 1.5);
        view.ShowHelp = (frame / StressStates.Length) % 2 == 0;
        for (int i = 0; i < frame % 5; i++)   // 0 to 4 messages, every tone
            w.Publish(new Message(w.Time - i * 0.5, $"STRESS MESSAGE {i}  +{100 * i}", (Tone)((frame + i) % 3)));
        p.HitFlash = frame % 4 == 0 ? 0.3 : 0;
        var guns = w.Loadout.Guns;
        if (frame % 7 < 2) { for (int i = 0; i < 5; i++) guns.TryFire(true, 1.0); }   // heats past 1: locked
        else guns.TryFire(false, 3.0);                                                // lock timer expires
        if (frame % 9 == 0) { p.Lateral = -9.2; p.OffRoad = true; }
        else if (frame % 9 == 1) { p.Lateral = 9.2; p.OffRoad = true; }
        else p.OffRoad = false;
        p.SnapPrev();
    }

    // ---- --perf ---------------------------------------------------------------------------------------------

    const int PerfFrames = 600, PerfWarmUp = 30;
    const double PerfBudgetMs = 8;

    /// <summary>
    /// Times GameView's OnRender (the cost of recording its draw calls) over 600 frames of the close-cars scene plus
    /// 25 more cars, at 1280 x 720, while the simulation moves a little between frames. The cost of rasterising into
    /// the bitmap is reported too, for information only.
    /// </summary>
    static int Perf(string[] args)
    {
        string path = Arg(args, "--perf") ?? "perf.txt";
        var run = NewRun();
        var view = new GameView { Run = run, Width = Width, Height = Height, ShowHelp = false };
        for (double t = 0; t < 12; t += Dt)
        {
            run.Update(Autopilot.Next(run.World), Dt);
            view.Effects.Absorb(run.World);
            view.Effects.Update(Dt);
        }
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);

        var onRender = new List<double>(PerfFrames);
        var total = new List<double>(PerfFrames);
        for (int frame = -PerfWarmUp; frame < PerfFrames; frame++)
        {
            if (frame % 100 == 0) Progress($"perf: frame {frame}");
            if (frame % 120 == 0 || frame == -PerfWarmUp) PlacePerfScene(run, view);
            // Advance a little (one 60 fps frame) so no two frames are identical.
            run.Update(Autopilot.Next(run.World), 1.0 / 60);
            view.Effects.Absorb(run.World);
            view.Effects.Update(1.0 / 60);

            int before = view.RenderCount;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            view.InvalidateVisual();
            view.UpdateLayout();
            bitmap.Clear();
            bitmap.Render(view);
            double all = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (view.RenderCount == before) throw new InvalidOperationException("the view was not redrawn");   // a skipped redraw would record a stale time
            if (frame >= 0)
            {
                onRender.Add(view.LastRenderMilliseconds);
                total.Add(all);
            }
        }

        var (mean, p95) = Stats(onRender);
        var (totalMean, totalP95) = Stats(total);
        bool pass = mean < PerfBudgetMs;
        var report = new System.Text.StringBuilder();
        report.AppendLine($"perf: {onRender.Count} frames at {Width}x{Height} (after {PerfWarmUp} warm-up frames), close-cars scene + 25 cars");
        report.AppendLine($"OnRender: mean {mean:0.000} ms, 95th percentile {p95:0.000} ms, max {onRender.Max():0.000} ms   (budget: mean < {PerfBudgetMs} ms: {(pass ? "PASS" : "FAIL")})");
        report.AppendLine($"OnRender + rasterising into a bitmap (information only): mean {totalMean:0.000} ms, 95th percentile {totalP95:0.000} ms");
        WriteReport(path, report.ToString());
        return pass ? 0 : 1;
    }

    /// <summary>The close-cars scene plus 25 more cars spread 20 to 900 m ahead.</summary>
    static void PlacePerfScene(MissionRun run, GameView view)
    {
        var w = run.World;
        PlaceCloseCars(w, view.Effects);
        for (int i = 0; i < 25; i++)
        {
            double ahead = 20 + i * (880.0 / 24);
            var car = AgentCar.Create(w.NextId(), (AgentKind)(i % 3), w.Player.Distance + ahead, (i % 5 - 2) * 4.5, w.Player.Speed);
            if (i % 4 == 0) car.Armor = car.Stats.Armor / 2;
            w.Agents.Add(car);
        }
    }

    static (double Mean, double P95) Stats(List<double> values)
    {
        var sorted = values.Order().ToList();
        return (values.Average(), sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1]);
    }

    /// <summary>Appends a progress line to the log file (a long run can then be watched without a console).</summary>
    static void Progress(string message)
    {
        try { File.AppendAllText(_logPath, message + Environment.NewLine); }
        catch (Exception) { /* progress is optional */ }
    }

    static void WriteReport(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
        Console.Out.Write(text);
    }

    /// <summary>
    /// Finds the spot on the track where the road ahead (0 to 1500 m) climbs steepest as seen from the eye (the largest
    /// angle above the camera's horizontal), and puts the player there, centred and straight, with nothing else on the road.
    /// </summary>
    static void PlaceUphill(MissionRun run, int width)
    {
        var w = run.World;
        var track = w.Track;
        double bestAngle = double.MinValue, bestStart = 0, bestAt = 0;
        for (double s = 0; s < track.Length; s += Track.SegmentLength)
        {
            double eye = track.HeightAt(s) + Projector.CamHeight;
            for (double a = 20; a <= 1500; a += Track.SegmentLength)
            {
                double angle = (track.HeightAt(s + a) - eye) / a;
                if (angle > bestAngle) { bestAngle = angle; bestStart = s; bestAt = a; }
            }
        }
        var p = w.Player;
        p.Distance = bestStart;
        p.Lateral = 0;
        p.Heading = 0;
        p.OffRoad = false;
        p.SnapPrev();
        w.View.Build(track, bestStart);
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        double rise = track.HeightAt(bestStart + bestAt) - track.HeightAt(bestStart) - Projector.CamHeight;
        Progress($"uphill: player at {bestStart:0} m; the road {bestAt:0} m ahead is {rise:0.0} m above the eye (angle {Math.Atan(bestAngle) * 180 / Math.PI:0.00} degrees)");
        double roadAbove = rise / bestAt * (width / 2.0) * Projector.Focal;   // pixels above the horizon line (straight-ahead estimate)
        Progress($"uphill: the top of the road is about {roadAbove:0} px above the horizon line; the mountain ridge reaches 8 to 60 px above it");
    }

    /// <summary>Two burning gunfire wrecks on an otherwise empty road: one 18 m ahead just right of the player, one 60 m ahead in another lane.</summary>
    static void PlaceWrecks(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        effects.Clear();
        w.Hazards.Add(new Wreck { Distance = p.Distance + 18, PrevDistance = p.Distance + 18, Lateral = p.Lateral + 2.2, Width = 1.9, Length = 4.4, Burn = 3.0, Seed = 1 });
        w.Hazards.Add(new Wreck { Distance = p.Distance + 60, PrevDistance = p.Distance + 60, Lateral = p.Lateral - 4.5, Width = 1.9, Length = 4.4, Burn = 3.7, Seed = 2 });
    }

    /// <summary>Puts the player left of the road and steps once so the off-road state is set.</summary>
    static void PlaceOffRoad(MissionRun run)
    {
        run.World.Player.Lateral = -9.2;
        run.Update(ControlFrame.Empty, Dt);
    }

    /// <summary>Replaces whatever is on the road with one of each thing the renderers draw, at known distances from the player.</summary>
    static void PlaceCloseCars(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        effects.Clear();

        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 22, p.Lateral + 4, p.Speed);
        runner.Armor = 3;   // half of 6, so its health bar shows
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, p.Distance + 70, p.Lateral - 1.5, p.Speed);
        var spinner = AgentCar.Create(w.NextId(), AgentKind.Pursuer, p.Distance + 140, p.Lateral, p.Speed);
        spinner.State = VehicleState.Spinning;
        spinner.SpinAngle = 0.7;
        spinner.SnapPrev();
        var behind = AgentCar.Create(w.NextId(), AgentKind.Pursuer, p.Distance - 25, p.Lateral + 3, p.Speed);
        w.Agents.AddRange(new[] { runner, blocker, spinner, behind });

        w.Hazards.Add(new Wreck { Distance = p.Distance + 50, PrevDistance = p.Distance + 50, Lateral = p.Lateral + 1, Width = 1.9, Length = 4.4, Burn = 0.6, Seed = 3 });
        w.Hazards.Add(new OilSlick { Distance = p.Distance + 40, Lateral = p.Lateral - 2.5 });

        effects.Spawn(p.Distance + 30, p.Lateral - 1);
        effects.Update(0.3);

        w.Tracers.Add(NewTracer(p, 9, 0.7, 3));    // fresh: also shows the muzzle flash
        w.Tracers.Add(NewTracer(p, 60, -0.7, 150));
        effects.MarkShot(w.Time, leftGun: true);   // the fresh tracer's muzzle flash
    }

    static Tracer NewTracer(PlayerCar p, double ahead, double side, double travel)
    {
        var pose = new RoadPose(p.Distance + ahead, p.Lateral + side, 0);
        return new Tracer { Pose = pose, PrevDistance = pose.Distance - 2.5, PrevLateral = pose.Lateral, PrevHeading = pose.Heading, Travel = travel, OwnerId = p.Id };
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
