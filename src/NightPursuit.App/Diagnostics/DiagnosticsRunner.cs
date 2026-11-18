using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NightPursuit.App.Rendering;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
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

    static readonly string[] Scenes = { "default", "close-cars", "offroad", "uphill", "wreck", "arsenal", "strike", "enemies", "boss", "boss-mirror", "missile-warning", "weapons-hud", "boss-lock", "help" };

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
        string[] scenes = (Arg(args, "--scene") ?? "default").Split(',');
        foreach (string scene in scenes)
            if (Array.IndexOf(Scenes, scene) < 0)
            {
                Error($"Unknown scene '{scene}'. Known scenes: {string.Join(", ", Scenes)}");
                return 2;
            }
        string mission = Arg(args, "--mission") ?? "mission-01";
        if (mission.Length == 0 || !mission.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
        {
            Error($"--mission needs a mission name of letters, digits and '-' only, e.g. mission-01, not '{mission}'");
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

        foreach (string scene in scenes)
        {
            // With several scenes, out.png becomes out-<scene>.png; each scene starts from its own fresh run.
            string file = scenes.Length > 1
                ? Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-" + scene + Path.GetExtension(path))
                : path;
            RenderScene(scene, mission, seconds, width, height, file);
        }
        return 0;
    }

    static void RenderScene(string scene, string mission, double seconds, int width, int height, string path)
    {
        var run = NewRun(mission);
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
        else if (scene == "arsenal") { PlaceOnOpenRoad(run.World); PlaceArsenal(run.World, view.Effects); }
        else if (scene == "strike") { PlaceOnOpenRoad(run.World); PlaceStrike(run.World, view.Effects); }
        else if (scene == "enemies") { PlaceOnOpenRoad(run.World); PlaceEnemies(run.World, view.Effects); }
        else if (scene == "weapons-hud") { PlaceOnOpenRoad(run.World); PlaceWeaponsHud(run.World, view.Effects); }
        else if (scene == "boss") { PlaceOnOpenRoad(run.World); PlaceBoss(run, view.Effects, mission); }
        else if (scene == "boss-mirror") { PlaceOnOpenRoad(run.World); PlaceBossMirror(run, view.Effects, mission); }
        else if (scene == "missile-warning") { PlaceOnOpenRoad(run.World); PlaceMissileWarning(run, view.Effects, mission); }
        else if (scene == "boss-lock") { PlaceOnOpenRoad(run.World); PlaceBossLock(run, view.Effects, mission); }
        else if (scene == "help") view.ShowHelp = true;   // the F1 help over the default scene

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
    }

    // ---- --stress -------------------------------------------------------------------------------------------

    static readonly string[] StressMissions = { "mission-01", "proving-ground" };
    static readonly (int W, int H)[] StressSizes = { (960, 540), (320, 200), (2560, 600) };
    static readonly double[] StressDistances = { -200, -60, -20, 3, 12, 30, 80, 200, 400, 700, 1000, 1300, 1490 };
    static readonly RunState[] StressStates =
        { RunState.Playing, RunState.Playing, RunState.Paused, RunState.Respawning, RunState.Completed, RunState.GameOver };

    /// <summary>
    /// Renders every 10 m of mission 1's road and the proving ground's, each at three window sizes (and 0 x 0), with a
    /// crowd of every enemy kind, a boss every third frame, slicks, wrecks, smoke, caltrops, crates, missiles, tracers and
    /// explosions, cycling the screen-fixed states (run-state overlays, help, messages, hit flash, locked guns, off-road,
    /// strike sweep, banner, puncture and grip loss) by frame number. Any exception counts as a failure.
    /// </summary>
    static int Stress(string[] args)
    {
        string path = Arg(args, "--stress") ?? "stress.txt";
        var report = new System.Text.StringBuilder();
        var failureMessages = new List<string>();
        int totalFrames = 0, totalFailures = 0, totalBossBar = 0;

        foreach (string mission in StressMissions)
        {
            var boss = LoadBoss(mission);
            foreach (var (width, height) in StressSizes)
            {
                var run = NewRun(mission);
                var view = new GameView { Run = run, Width = width, Height = height };
                view.Measure(new Size(width, height));
                view.Arrange(new Rect(0, 0, width, height));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                int frames = 0, failures = 0, bossBar = 0;
                var sizeClock = System.Diagnostics.Stopwatch.StartNew();

                for (double z = 0; z < run.World.Track.Length; z += 10, frames++)
                {
                    if (frames % 50 == 0) Progress($"stress {mission} {width}x{height}: frame {frames}, {sizeClock.Elapsed.TotalSeconds:0.0} s");
                    try
                    {
                        PlaceStressFrame(run, view, boss, z, frames);
                        int before = view.RenderCount;
                        view.InvalidateVisual();
                        view.UpdateLayout();
                        bitmap.Clear();
                        bitmap.Render(view);
                        if (view.RenderCount == before) throw new InvalidOperationException("the view was not redrawn");
                        if (BossBar.Showing(run) is not null) bossBar++;
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        if (failureMessages.Count < 10) failureMessages.Add($"{mission} {width}x{height} z={z}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                report.AppendLine($"{mission} {width}x{height}: {frames} frames, {failures} failures, boss bar on {bossBar}, {sizeClock.Elapsed.TotalSeconds:0.0} s");
                totalFrames += frames;
                totalBossBar += bossBar;
                totalFailures += failures;
            }

            // A minimised window: the view has no size at all, so drawing must simply do nothing.
            {
                int failures = 0;
                try
                {
                    var view = new GameView { Run = NewRun(mission), Width = 0, Height = 0 };
                    view.Measure(new Size(0, 0));
                    view.Arrange(new Rect(0, 0, 0, 0));
                    view.InvalidateVisual();
                    view.UpdateLayout();
                    new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32).Render(view);   // a bitmap cannot be 0 x 0
                }
                catch (Exception ex)
                {
                    failures++;
                    failureMessages.Add($"{mission} 0x0: {ex.GetType().Name}: {ex.Message}");
                }
                report.AppendLine($"{mission} 0x0: 1 frame, {failures} failures");
                totalFrames++;
                totalFailures += failures;
            }
        }

        report.Insert(0, $"stress: {totalFrames} frames, {totalFailures} failures, boss bar on {totalBossBar}{Environment.NewLine}");
        if (failureMessages.Count > 0)
        {
            report.AppendLine("first failures:");
            foreach (var m in failureMessages) report.AppendLine("  " + m);
        }
        WriteReport(path, report.ToString());
        return totalFailures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Sets up one stress frame at road position <paramref name="z"/>; <paramref name="frame"/> picks which enemy kinds,
    /// boss state and screen-fixed variant to show.
    /// </summary>
    static void PlaceStressFrame(MissionRun run, GameView view, BossDefinition bossDef, double z, int frame)
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
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        w.RecentMessages.Clear();
        view.Effects.Clear();
        // A fresh loadout each frame: 0, 1 or 2 missiles (the lock and "NO MISSILES"), a strike ready, a rear weapon picked.
        w.Loadout = new Loadout(new LoadoutDefinition(Missiles: frame % 3, Strikes: 1));
        for (int i = 0; i < frame % 3; i++) w.Loadout.CycleRear();

        var cars = new List<AgentCar>();
        for (int k = 0; k < StressDistances.Length; k++)
        {
            double d = z + StressDistances[k];
            double x = (k % 5 - 2) * 4.5;
            var car = AgentCar.Create(w.NextId(), (AgentKind)((k + frame) % 6), d, x, 30);   // every kind but the boss (F22)
            car.Armor = car.Stats.Armor / 3;
            if (k % 4 == 0) { car.State = VehicleState.Spinning; car.SpinAngle = k; car.SnapPrev(); }
            if (k % 5 == 3) car.HitFlash = 0.1;
            cars.Add(car);
            w.Hazards.Add(new OilSlick { Distance = d, Lateral = -x, Hostile = k % 2 == 1 });
            w.Hazards.Add(new Wreck { Distance = d + 7, PrevDistance = d + 7, Lateral = x / 2, Width = 1.9, Length = 4.4, Burn = k * 0.7, Seed = 100 + k });
            var pose = new RoadPose(d, x / 2, (k % 3 - 1) * 0.2);
            w.Tracers.Add(new Tracer { Pose = pose, PrevDistance = d - 2.5, PrevLateral = pose.Lateral, PrevHeading = pose.Heading, Travel = k * 25, OwnerId = k % 2 == 0 ? p.Id : car.Id, Hostile = k % 2 == 1 });
            view.Effects.Spawn(d, x);
        }

        // A punctured car and a Gunner aiming at you.
        var punctured = AgentCar.Create(w.NextId(), (AgentKind)(frame % 6), z + 26, p.Lateral + 4, 30);
        punctured.Punctured = true;
        punctured.PunctureSeconds = frame % 10 * 0.1;
        var gunner = AgentCar.Create(w.NextId(), AgentKind.Gunner, z + 45, p.Lateral, 30);
        gunner.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Aim, Timer = 0.3, Target = gunner.Lateral };
        cars.Add(punctured);
        cars.Add(gunner);

        // Every third frame, the boss 60 m ahead (with its bar): each phase in turn, with and without its shield, spun out or
        // not, engaged or easing in. On the other frames the director is back in the approach, so the no-boss layout is drawn too.
        if (frame % 3 == 0)
        {
            int n = frame / 3;
            var boss = BossCar.Create(w.NextId(), bossDef, z + 60, p.Lateral - 3, 30);
            boss.PhaseIndex = n % bossDef.Phases.Count;
            boss.Armor = boss.MaxHealth * Math.Min(1, boss.Phase.EndsBelow + 0.1);
            boss.Shield = n % 2 == 0 ? 0.8 : 0;
            if (n % 4 == 1) { boss.State = VehicleState.Spinning; boss.StateTimer = 1; boss.SpinAngle = n; }
            boss.Engaged = n % 3 != 2;
            boss.SnapPrev();
            cars.Add(boss);
            run.Director.AdoptBossForDiagnostics(boss);
        }
        else run.Director.ReleaseBossForDiagnostics();   // no boss: the approach HUD, without the boss bar
        w.Agents.AddRange(cars);

        // The arsenal: 2 smoke clouds, 3 caltrops (alternately hostile), 3 crates, 2 player missiles and 1 enemy missile.
        for (int i = 0; i < 2; i++)
        {
            double d = z + (i == 0 ? -30 : 50 + 20 * (frame % 4));
            w.Hazards.Add(new SmokeCloud { Distance = d, PrevDistance = d, Lateral = p.Lateral + (i == 0 ? -2 : 3), Age = frame % 10 * 0.45 });
        }
        for (int i = 0; i < 3; i++)
            w.Hazards.Add(new Caltrops { Distance = z + 35 + 40 * i, Lateral = Track.LaneCentres[i], Hostile = (i + frame) % 2 == 0 });
        for (int i = 0; i < 3; i++)
        {
            double d = z + 25 + 30 * i;
            w.Hazards.Add(new SupplyCrate((SupplyItem)((i + frame) % 6), 1 + i % 2) { Distance = d, PrevDistance = d, Lateral = Track.LaneCentres[(i + frame) % 3] });
        }
        for (int i = 0; i < 2; i++)
        {
            var pose = new RoadPose(z + 8 + 30 * i, p.Lateral + (i == 0 ? -1 : 1), (i == 0 ? -1 : 1) * 0.15);
            w.Missiles.Add(new Missile
            {
                Pose = pose, PrevDistance = pose.Distance - 2, PrevLateral = pose.Lateral, PrevHeading = pose.Heading,
                Speed = p.Speed + Missile.LaunchBoost, Travel = 100 * i, TargetId = i == 0 ? gunner.Id : null,
            });
        }
        {
            bool behind = frame % 4 == 3;
            double gap = behind ? -70 : 40 + frame % 5 * 25;
            double lateral = p.Lateral + (frame % 3 - 1) * 4;
            w.EnemyMissiles.Add(new EnemyMissile
            {
                Id = w.NextId(), Distance = z + gap, PrevDistance = z + gap, Lateral = lateral, PrevLateral = lateral,
                TargetLateral = p.Lateral, LaunchTime = w.Time, Dir = behind ? -1 : 1, Locked = frame % 2 == 0,
            });
        }

        // The strike sweep on odd frames, part-way through (with a beam from an earlier kill).
        if (frame % 2 == 1)
        {
            w.Loadout.Strike.TryBegin();
            w.Loadout.Strike.AdvanceSweep(SatelliteStrike.SweepSeconds * (frame % 9) / 10);
            view.Effects.SpawnBeam(z + 110, p.Lateral + 6);
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
        // A banner on frames divisible by 5 (urgent with a detail line, or plain); otherwise none. Time stands still in
        // this test, so the banner is drawn at its first instant (still fully faded out), but every draw call runs.
        if (frame % 5 == 0) w.Announce("STRESS BANNER", frame % 10 == 0 ? "WITH A DETAIL LINE" : "", 3, urgent: frame % 10 == 0);
        else w.Announce("", "", 0, urgent: true);   // ends at once: no banner
        p.HitFlash = frame % 4 == 0 ? 0.3 : 0;
        p.GripLoss = frame % 2 == 0 ? 0.6 : 0;      // enemy oil and enemy caltrops, alternately
        p.PunctureLeft = frame % 2 == 1 ? 3.2 : 0;
        var guns = w.Loadout.Guns;
        if (frame % 7 < 2) { for (int i = 0; i < 5; i++) guns.TryFire(true, 1.0); }   // heats past 1: locked
        else guns.TryFire(false, 3.0);                                                // lock timer expires
        if (frame % 9 == 0) { p.Lateral = -9.2; p.OffRoad = true; }
        else if (frame % 9 == 1) { p.Lateral = 9.2; p.OffRoad = true; }
        else p.OffRoad = false;
        p.SnapPrev();
    }

    // ---- --perf ---------------------------------------------------------------------------------------------

    const int PerfFrames = 600, PerfWarmUp = 30, PerfPlaceEvery = 60;
    const double PerfBudgetMs = 8;

    /// <summary>
    /// Times GameView's OnRender (the cost of recording its draw calls) over 600 frames of the close-cars scene plus
    /// 25 more cars of every kind, the boss, smoke, crates, missiles and a strike sweep (<see cref="PlacePerfScene"/>),
    /// at 1280 x 720, while the simulation moves a little between frames. The cost of rasterising into the bitmap is
    /// reported too, for information only.
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
        var bossDef = LoadBoss("mission-01");
        int strikeFrames = 0, bossFrames = 0;
        double startDistance = 0;
        for (int frame = -PerfWarmUp; frame < PerfFrames; frame++)
        {
            if (frame % 100 == 0) Progress($"perf: frame {frame}");
            // Placed again every second, so the 1.5 s strike sweep is always under way (it never lands and clears the road).
            if (frame % PerfPlaceEvery == 0 || frame == -PerfWarmUp) PlacePerfScene(run, view, bossDef);
            if (frame == 0) startDistance = run.World.Player.Distance;
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
                if (run.World.Loadout.Strike.Targeting) strikeFrames++;
                if (BossBar.Showing(run) is not null) bossFrames++;
            }
        }

        var (mean, p95) = Stats(onRender);
        var (totalMean, totalP95) = Stats(total);
        bool pass = mean < PerfBudgetMs;
        var report = new System.Text.StringBuilder();
        report.AppendLine($"perf: {onRender.Count} frames at {Width}x{Height} (after {PerfWarmUp} warm-up frames), close-cars scene + 25 cars of all six kinds, the boss, 3 smoke clouds, 4 crates, 2 missiles and a strike sweep");
        report.AppendLine($"strike sweeping in {strikeFrames} frames, boss bar showing in {bossFrames}; the autopilot drove {run.World.Player.Distance - startDistance:0} m, ended in state {run.State}");
        report.AppendLine($"OnRender: mean {mean:0.000} ms, 95th percentile {p95:0.000} ms, max {onRender.Max():0.000} ms   (budget: mean < {PerfBudgetMs} ms: {(pass ? "PASS" : "FAIL")})");
        report.AppendLine($"OnRender + rasterising into a bitmap (information only): mean {totalMean:0.000} ms, 95th percentile {totalP95:0.000} ms");
        WriteReport(path, report.ToString());
        return pass ? 0 : 1;
    }

    /// <summary>
    /// The close-cars scene plus 25 more cars of all six kinds spread 20 to 900 m ahead, mission 1's boss 90 m ahead
    /// (with its bar), 3 smoke clouds, 4 crates, 2 player missiles in flight and a strike sweep just begun.
    /// </summary>
    static void PlacePerfScene(MissionRun run, GameView view, BossDefinition bossDef)
    {
        var w = run.World;
        var p = w.Player;
        PlaceCloseCars(w, view.Effects);
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        for (int i = 0; i < 25; i++)
        {
            double ahead = 20 + i * (880.0 / 24);
            var car = AgentCar.Create(w.NextId(), (AgentKind)(i % 6), p.Distance + ahead, (i % 5 - 2) * 4.5, p.Speed);
            if (i % 4 == 0) car.Armor = car.Stats.Armor / 2;
            w.Agents.Add(car);
        }

        var boss = BossCar.Create(w.NextId(), bossDef, p.Distance + 90, 5, p.Speed);
        boss.Engaged = true;
        boss.Armor = 0.7 * boss.MaxHealth;
        w.Agents.Add(boss);
        run.Director.AdoptBossForDiagnostics(boss);

        double[] smoke = { -30, 35, 120 };
        foreach (double d in smoke)
            w.Hazards.Add(new SmokeCloud { Distance = p.Distance + d, PrevDistance = p.Distance + d, Lateral = p.Lateral + 2, Age = 1.0, Speed = d < 0 ? p.Speed / 2 : 0 });
        for (int i = 0; i < 4; i++)
        {
            double d = p.Distance + 28 + 19 * i;
            w.Hazards.Add(new SupplyCrate((SupplyItem)(i + 1), 1) { Distance = d, PrevDistance = d, Lateral = Track.LaneCentres[i % 3] });
        }
        for (int i = 0; i < 2; i++)
        {
            var pose = new RoadPose(p.Distance + 6 + 10 * i, p.Lateral + (i == 0 ? -1 : 1), 0);
            w.Missiles.Add(new Missile
            {
                Pose = pose, PrevDistance = pose.Distance, PrevLateral = pose.Lateral, PrevHeading = pose.Heading,
                Speed = p.Speed + Missile.LaunchBoost, TargetId = i == 0 ? boss.Id : null,
            });
        }

        // A fresh loadout with the strike sweep just begun; the scene is placed again before the 1.5 s sweep ends.
        w.Loadout = new Loadout(new LoadoutDefinition(Missiles: 2, Strikes: 1));
        w.BeginStrike();   // as the V key does: the sweep, its message and its banner
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

    /// <summary>
    /// Moves the player (centred, straight) to the stretch of track where the next 400 m of road is least hidden by
    /// its own hills, so a scene's objects can all be seen. The first such stretch wins.
    /// </summary>
    static void PlaceOnOpenRoad(World w)
    {
        var track = w.Track;
        int bestHidden = int.MaxValue;
        double best = 0;
        for (double s = 0; s < track.Length - 450; s += Track.SegmentLength)
        {
            double eye = track.HeightAt(s) + Projector.CamHeight, top = double.MinValue;
            int hidden = 0;
            for (double a = 10; a <= 400; a += Track.SegmentLength)
            {
                double angle = (track.HeightAt(s + a) - eye) / a;
                if (angle < top - 1e-4) hidden++;
                top = Math.Max(top, angle);
            }
            if (hidden < bestHidden) { bestHidden = hidden; best = s; }
        }
        var p = w.Player;
        p.Distance = best;
        p.Lateral = 0;
        p.Heading = 0;
        p.OffRoad = false;
        p.SnapPrev();
        w.View.Build(track, best);
        Progress($"open road: player placed at {best:0} m ({bestHidden} hidden road segments in the next 400 m)");
    }

    /// <summary>
    /// One of everything in the arsenal: own smoke 30 m behind (the mirror) and another 60 m ahead, hostile caltrops 45 m
    /// ahead in the player's lane, crates (Missile 35 m, Armor 22 m on the other side, so the 60 m smoke does not hide its label), a player missile 12 m ahead locked on a Runner 140 m
    /// ahead, an enemy missile 70 m ahead in the next lane and a hostile tracer 14 m ahead (beside the dart, clear of any smoke, so both can be checked by eye).
    /// </summary>
    static void PlaceArsenal(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        effects.Clear();
        w.Loadout.Restock(SupplyItem.Missile, 1);

        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 140, p.Lateral, p.Speed);
        w.Agents.Add(runner);

        w.Hazards.Add(new SmokeCloud { Distance = p.Distance - 30, PrevDistance = p.Distance - 30, Lateral = p.Lateral, Age = 2.0 });
        w.Hazards.Add(new SmokeCloud { Distance = p.Distance + 60, PrevDistance = p.Distance + 60, Lateral = p.Lateral, Age = 2.0 });
        w.Hazards.Add(new Caltrops { Distance = p.Distance + 45, Lateral = p.Lateral, Hostile = true });
        w.Hazards.Add(new SupplyCrate(SupplyItem.Missile, 1) { Distance = p.Distance + 35, PrevDistance = p.Distance + 35, Lateral = p.Lateral + 5 });
        w.Hazards.Add(new SupplyCrate(SupplyItem.Armor, 1) { Distance = p.Distance + 22, PrevDistance = p.Distance + 22, Lateral = p.Lateral - 5 });

        var pose = new RoadPose(p.Distance + 12, p.Lateral, 0);
        w.Missiles.Add(new Missile
        {
            Pose = pose, PrevDistance = pose.Distance, PrevLateral = pose.Lateral, PrevHeading = pose.Heading,
            Speed = p.Speed + Missile.LaunchBoost, Travel = 40, TargetId = runner.Id,
        });
        w.EnemyMissiles.Add(new EnemyMissile
        {
            Id = w.NextId(), Distance = p.Distance + 70, PrevDistance = p.Distance + 70, Lateral = p.Lateral + 5, PrevLateral = p.Lateral + 5,
            TargetLateral = p.Lateral + 5, LaunchTime = w.Time, Dir = 1,
        });
        var hostile = new RoadPose(p.Distance + 14, p.Lateral + 1.8, 0);
        w.Tracers.Add(new Tracer
        {
            Pose = hostile, PrevDistance = hostile.Distance - 2.5, PrevLateral = hostile.Lateral, PrevHeading = hostile.Heading,
            Travel = 40, OwnerId = runner.Id, Hostile = true,
        });
    }

    /// <summary>
    /// One of each new enemy kind: a Gunner aiming 30 m ahead in the player's lane, a Dropper arming 90 m ahead with a Blocker
    /// beside it (to tell the two apart), an Armored 55 m ahead in the next lane, a punctured Runner 120 m ahead, and an
    /// Armored 30 m behind (for the mirror and the radar).
    /// </summary>
    static void PlaceEnemies(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        effects.Clear();
        SpendMissiles(w);

        var gunner = AgentCar.Create(w.NextId(), AgentKind.Gunner, p.Distance + 30, p.Lateral, p.Speed);
        gunner.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Aim, Timer = 0.3, Target = gunner.Lateral };
        var dropper = AgentCar.Create(w.NextId(), AgentKind.Dropper, p.Distance + 90, p.Lateral - 3.5, p.Speed);
        dropper.DriverState = new DropperDriver.State { Arming = 0.3, Target = dropper.Lateral };
        var armored = AgentCar.Create(w.NextId(), AgentKind.Armored, p.Distance + 55, p.Lateral + 4, p.Speed);
        var punctured = AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 120, p.Lateral + 3.5, p.Speed);
        punctured.Punctured = true;
        punctured.PunctureSeconds = 0.17;   // near the top of its wobble
        var behind = AgentCar.Create(w.NextId(), AgentKind.Armored, p.Distance - 30, p.Lateral + 3, p.Speed);
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, p.Distance + 90, p.Lateral - 6.3, p.Speed);
        w.Agents.AddRange(new[] { gunner, dropper, armored, punctured, behind, blocker });
    }

    /// <summary>
    /// Every weapon-panel readout at once: Caltrops selected, smoke empty, oil recharging at 2 of 5 and cooling down (WAIT),
    /// 2 missiles with a lock on a Runner 120 m ahead, a strike targeting, guns warm, and the player punctured and without grip.
    /// </summary>
    static void PlaceWeaponsHud(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        effects.Clear();
        var lo = w.Loadout;

        w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 120, p.Lateral, p.Speed));
        lo.Restock(SupplyItem.Missile, MissileLauncher.MaxCharges);
        lo.Missiles.Update(MissileLauncher.UseCooldown + 1);
        lo.Restock(SupplyItem.Strike, 1);
        lo.Strike.TryBegin();
        lo.Strike.AdvanceSweep(SatelliteStrike.SweepSeconds * 0.4);
        lo.CycleRear();
        lo.CycleRear();   // Oil -> Smoke -> Caltrops
        lo.Caltrops.Restock(1);

        // Smoke empty; oil used down to 2 of 5, the last use still cooling (WAIT) and 3 s into its 8 s recharge.
        for (int i = 0; i < SmokeLauncher.MaxCharges + 4; i++) { lo.Smoke.TryUse(); lo.Smoke.Update(10); }
        for (int i = 0; i < OilDropper.MaxCharges - 2; i++) { lo.Oil.TryDrop(); lo.Oil.Update(OilDropper.DropCooldown + 0.01); }
        lo.Oil.Update(3);
        lo.Oil.TryDrop();
        lo.Oil.Restock(1);
        lo.Oil.Update(0.2);
        p.PunctureLeft = 3.2;
        p.GripLoss = 0.6;
        effects.Clear();
    }

    /// <summary>Uses up the missiles, so no LOCK bracket covers the car being looked at.</summary>
    static void SpendMissiles(World w)
    {
        for (int i = 0; i < MissileLauncher.MaxCharges; i++)
        {
            w.Loadout.Missiles.TryUse();
            w.Loadout.Missiles.Update(MissileLauncher.UseCooldown + 1);
        }
    }

    static BossDefinition LoadBoss(string mission) =>
        MissionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "content", "missions", mission + ".json")).Boss
        ?? throw new InvalidOperationException($"{mission} has no boss");

    /// <summary>Clears the road and puts <paramref name="boss"/> in the world and the director's boss stage, so the boss bar and the boss warnings show.</summary>
    static void AddBoss(MissionRun run, BossCar boss, EffectList effects)
    {
        var w = run.World;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        effects.Clear();
        SpendMissiles(w);
        boss.Engaged = true;
        boss.SnapPrev();
        w.Agents.Add(boss);
        run.Director.AdoptBossForDiagnostics(boss);
    }

    /// <summary>The mission's boss 45 m ahead at 65% health in phase 1, spun out (open armour) with 0.5 s of shield left.</summary>
    static void PlaceBoss(MissionRun run, EffectList effects, string mission)
    {
        var p = run.World.Player;
        var boss = BossCar.Create(run.World.NextId(), LoadBoss(mission), p.Distance + 45, p.Lateral, p.Speed);
        boss.Armor = 0.65 * boss.MaxHealth;
        boss.PhaseIndex = 0;
        boss.State = VehicleState.Spinning;
        boss.StateTimer = 1;
        boss.SpinAngle = 0.5;
        boss.Shield = 0.5;
        AddBoss(run, boss, effects);
    }

    /// <summary>
    /// The boss 30 m behind on your right in its Pursuer phase (it shows in the mirror at 1.3 times), your own smoke cloud behind you,
    /// and a punctured Runner 22 m ahead throwing tyre sparks.
    /// </summary>
    static void PlaceBossMirror(MissionRun run, EffectList effects, string mission)
    {
        var w = run.World;
        var p = w.Player;
        var boss = BossCar.Create(w.NextId(), LoadBoss(mission), p.Distance - 30, p.Lateral + 3.5, p.Speed);
        boss.Armor = 0.4 * boss.MaxHealth;
        boss.PhaseIndex = 1;
        AddBoss(run, boss, effects);
        var punctured = AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 22, p.Lateral - 3.5, p.Speed);
        punctured.Punctured = true;
        punctured.PunctureSeconds = 0.17;
        w.Agents.Add(punctured);
        w.Hazards.Add(new SmokeCloud { Distance = p.Distance - 14, PrevDistance = p.Distance - 14, Lateral = p.Lateral - 1, Age = 1.0 });
    }

    /// <summary>The boss 40 m ahead in your lane with 2 missiles ready, so its BOSS tag and the missile LOCK label show together.</summary>
    static void PlaceBossLock(MissionRun run, EffectList effects, string mission)
    {
        var w = run.World;
        var p = w.Player;
        var boss = BossCar.Create(w.NextId(), LoadBoss(mission), p.Distance + 40, p.Lateral, p.Speed);
        boss.Armor = 0.8 * boss.MaxHealth;
        AddBoss(run, boss, effects);   // spends the missiles
        w.Loadout.Restock(SupplyItem.Missile, MissileLauncher.MaxCharges);
        w.Loadout.Missiles.Update(MissileLauncher.UseCooldown + 1);
    }

    /// <summary>
    /// Everything the top of the screen can show at once (the layout check): the boss bar, four warnings (a missile 1.6 s out in the next lane,
    /// a blocker, a hostile oil slick 90 m ahead and a Gunner 30 m behind closing at 6 m/s), four messages and a phase banner.
    /// The boss is 100 m ahead in a phase that fires missiles.
    /// </summary>
    static void PlaceMissileWarning(MissionRun run, EffectList effects, string mission)
    {
        var w = run.World;
        w.Announce("PHASE 2 OF 3: PURSUER", "LET IT PASS, THEN SHOOT  -  FIRES MISSILES", 3, urgent: true);
        w.Advance(ControlFrame.Empty, 0.3);   // the banner has faded in by the time the picture is taken
        var p = w.Player;
        var real = LoadBoss(mission);
        var def = new BossDefinition(real.Name, real.Health, new List<BossPhaseDefinition>
            { new(AgentKind.Blocker, 0.6), new(AgentKind.Pursuer, 0.25, FiresMissiles: true), new(AgentKind.Runner, 0) });
        var boss = BossCar.Create(w.NextId(), def, p.Distance + 100, p.Lateral + 3.5, p.Speed);
        boss.Armor = 0.55 * boss.MaxHealth;
        boss.PhaseIndex = 1;
        AddBoss(run, boss, effects);

        double gap = 1.6 * EnemyMissile.ClosingSpeed + EnemyMissile.ContactGap;   // 1.6 s from impact
        w.EnemyMissiles.Add(new EnemyMissile
        {
            Id = w.NextId(), Distance = p.Distance + gap, PrevDistance = p.Distance + gap, Lateral = p.Lateral + 3.5, PrevLateral = p.Lateral + 3.5,
            TargetLateral = p.Lateral + 3.5, LaunchTime = w.Time, Dir = 1,
        });
        w.Hazards.Add(new OilSlick { Distance = p.Distance + 90, Lateral = p.Lateral, Hostile = true });
        var blocker = AgentCar.Create(w.NextId(), AgentKind.Blocker, p.Distance + 60, p.Lateral, p.Speed);
        var gunner = AgentCar.Create(w.NextId(), AgentKind.Gunner, p.Distance - 30, p.Lateral - 3.5, p.Speed + 6);
        w.Agents.AddRange(new[] { blocker, gunner });

        w.Publish(new Message(w.Time, "MISSILE HIT THE BOSS", Tone.Good));
        w.Publish(new Message(w.Time, "BOSS PHASE 2 OF 3: PURSUER", Tone.Info));
        w.Publish(new Message(w.Time, "CRATE: MISSILE +1", Tone.Good));
        w.Publish(new Message(w.Time, "GUNNER SPOTTED BEHIND", Tone.Bad));
    }

    /// <summary>Three Runners 60, 180 and 380 m ahead, the strike half-way through its sweep, and one beam from a prior kill.</summary>
    static void PlaceStrike(World w, EffectList effects)
    {
        var p = w.Player;
        w.Agents.Clear();
        w.Hazards.Clear();
        w.Tracers.Clear();
        w.Missiles.Clear();
        w.EnemyMissiles.Clear();
        effects.Clear();
        double[] ahead = { 60, 180, 380 }, side = { 4, -4, 0 };
        for (int i = 0; i < ahead.Length; i++)
            w.Agents.Add(AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + ahead[i], p.Lateral + side[i], p.Speed));
        w.Loadout.Restock(SupplyItem.Strike, 1);
        w.Loadout.Strike.TryBegin();
        w.Loadout.Strike.AdvanceSweep(SatelliteStrike.SweepSeconds / 2);
        w.Announce("SATELLITE STRIKE", "TARGETING 3 HOSTILES", 1.5);
        effects.Spawn(p.Distance + 110, p.Lateral + 6);
        effects.SpawnBeam(p.Distance + 110, p.Lateral + 6);
        effects.Update(0.2);
    }

    static MissionRun NewRun(string mission = "mission-01")
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "missions", mission + ".json");
        return new MissionRun(MissionLoader.LoadFile(path), new FeelSettings(), seed: 1);
    }

    static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
