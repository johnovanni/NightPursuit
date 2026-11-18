using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ControlsLab;

/// <summary>
/// Diagnostic: ControlsLab.exe --snapshot out.png [--seconds 12] [--scheme 0-3] [--help-overlay]
/// Drives the car on autopilot for a while, then renders one frame to a PNG.
/// </summary>
static class Snapshot
{
    /// <summary>
    /// --stress: renders a frame every 10 m round the whole track with cars, slicks, tracers and
    /// explosions at many distances, and reports any exception. Exit code 1 on failure.
    /// </summary>
    static void Stress(string[] args)
    {
        var world = new World(new Tuning());
        var view = new GameView { World = world, Width = 960, Height = 540 };
        view.Measure(new Size(960, 540));
        view.Arrange(new Rect(0, 0, 960, 540));
        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
        double[] distances = { -200, -60, -20, 3, 12, 30, 80, 200, 400, 700, 1000, 1300, 1490 };
        int frames = 0, failures = 0;
        var report = new System.Text.StringBuilder();

        for (double z = 0; z < world.Track.Length; z += 10)
        {
            var p = world.Player;
            p.Z = z;
            p.X = Math.Sin(z / 300) * 6;
            p.Heading = Math.Sin(z / 170) * 0.35;   // nose up to 20° either way
            world.View.Build(world.Track, z);
            world.Enemies.Clear(); world.Slicks.Clear(); world.Bullets.Clear(); world.Explosions.Clear();
            int k = 0;
            foreach (double d in distances)
            {
                double x = (k++ % 5 - 2) * 4.5;
                world.Enemies.Add(new EnemyCar
                {
                    Kind = (EnemyKind)(k % 3), Z = z + d, X = x, Health = 2, MaxHealth = 6,
                    State = k % 4 == 0 ? EnemyState.Spinning : EnemyState.Driving, SpinAngle = k,
                });
                world.Slicks.Add(new OilSlick { Z = z + d, X = -x });
                world.Bullets.Add(new Bullet { Z = z + d, X = x / 2, Heading = (k % 3 - 1) * 0.2 });
                world.Explosions.Add(new Explosion { Z = z + d, X = x, Age = 0.4, Seed = k });
            }
            try
            {
                view.InvalidateVisual();
                view.UpdateLayout();
                bitmap.Clear();
                bitmap.Render(view);
            }
            catch (Exception ex)
            {
                failures++;
                if (failures <= 10) report.AppendLine($"z={z}: {ex.GetType().Name}: {ex.Message}");
            }
            frames++;
        }

        report.Insert(0, $"stress: {frames} frames, {failures} failures{Environment.NewLine}");
        File.WriteAllText(Arg(args, "--stress") ?? "stress.txt", report.ToString());
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// --trace out.csv: drives with keyboard-style input through several scenarios and logs every
    /// simulation step, then summarises where the nose angle or the screen shake oscillates.
    /// </summary>
    static void Trace(string[] args)
    {
        string path = Arg(args, "--trace") ?? "trace.csv";
        var csv = new System.Text.StringBuilder("scenario,t,speed,steer,headingDeg,x,lateralVel,offRoad,shake,curvature\n");
        var summary = new System.Text.StringBuilder();
        const double dt = 1 / 120.0;

        foreach (var scenario in new[] { "taps", "hold-into-rail", "no-steer-on-bends", "weave" })
        {
            var world = new World(new Tuning());
            world.Enemies.Clear();
            var rng = new Random(9);
            int dir = 0;
            double nextChange = 0, prevRate = 0;
            int flips = 0, oscillatingSteps = 0, shakeSteps = 0, offRoadSteps = 0;
            double prevHeading = 0;
            var flipTimes = new Queue<double>();
            var oscillationStarts = new List<double>();
            bool inOscillation = false;

            for (double t = 0; t < 60; t += dt)
            {
                var p = world.Player;
                switch (scenario)
                {
                    case "taps":
                        if (t >= nextChange) { dir = rng.Next(3) - 1; nextChange = t + 0.05 + rng.NextDouble() * 0.5; }
                        break;
                    case "hold-into-rail":
                        dir = (t % 12) < 4 ? 1 : (t % 12) < 8 ? 0 : -1;
                        break;
                    case "no-steer-on-bends":
                        dir = 0;
                        break;
                    case "weave":
                        // A player keeping to the centre lane with key presses: steer back when off-centre.
                        if (t >= nextChange)
                        {
                            dir = p.X > 1.5 ? -1 : p.X < -1.5 ? 1 : 0;
                            nextChange = t + 0.12;
                        }
                        break;
                }
                world.Enemies.Clear();   // no traffic: isolate steering
                world.Update(new InputFrame { Gas = true, SteerDir = dir }, dt);

                double rate = (p.Heading - prevHeading) / dt;
                prevHeading = p.Heading;
                if (Math.Sign(rate) != Math.Sign(prevRate) && Math.Abs(rate) > 0.002 && Math.Abs(prevRate) > 0.002)
                {
                    flips++;
                    flipTimes.Enqueue(t);
                }
                if (Math.Abs(rate) > 0.002) prevRate = rate;
                while (flipTimes.Count > 0 && flipTimes.Peek() < t - 0.5) flipTimes.Dequeue();
                bool osc = flipTimes.Count >= 5;   // 5+ reversals of the nose within half a second
                if (osc) oscillatingSteps++;
                if (osc && !inOscillation) oscillationStarts.Add(t);
                inOscillation = osc;
                if (p.Shake > 0) shakeSteps++;
                if (p.OffRoad) offRoadSteps++;

                csv.Append(FormattableString.Invariant(
                    $"{scenario},{t:0.000},{p.Speed:0.00},{p.Steer:0.000},{p.Heading * 180 / Math.PI:0.0000},{p.X:0.000},{p.LateralVel:0.000},{(p.OffRoad ? 1 : 0)},{p.Shake:0.000},{world.Track.CurvatureAt(p.Z):0.000000}\n"));
            }
            summary.AppendLine(FormattableString.Invariant(
                $"{scenario}: nose reversals {flips}, oscillating {oscillatingSteps * dt:0.0}s (starts at {string.Join(", ", oscillationStarts.Take(8).Select(s => s.ToString("0.0")))}), shaking {shakeSteps * dt:0.0}s, off road {offRoadSteps * dt:0.0}s"));
        }
        File.WriteAllText(path, csv.ToString());
        File.WriteAllText(Path.ChangeExtension(path, ".summary.txt"), summary.ToString());
    }

    /// <summary>
    /// --kill-trace out.csv: drives in normal traffic like a keyboard player, aiming the nose at the nearest
    /// car ahead and firing, for 3 minutes. Logs every step and summarises what happens in the 3 s after
    /// each gun kill (shake and its cause, off-road time, nose reversals) against the rest of the drive.
    /// </summary>
    static void KillTrace(string[] args)
    {
        string path = Arg(args, "--kill-trace") ?? "kill-trace.csv";
        var csv = new System.Text.StringBuilder("t,speed,steer,headingDeg,x,lateralVel,pushVel,shake,shakeWhy,offRoad,kills,targetDz,targetX,aimX\n");
        const double dt = 1 / 120.0;
        var world = new World(new Tuning());
        var log = new List<(double T, double Heading, double Rate, double Shake, string Why, bool Off, double LatVel, double X)>();
        var killTimes = new List<double>();
        int dir = 0, kills = 0;
        double nextDecision = 0, prevHeading = 0;
        for (double t = 0; t < 180; t += dt)
        {
            var p = world.Player;
            EnemyCar? target = null;
            foreach (var e in world.Enemies)
                if (e.State != EnemyState.Wrecked && e.Z - p.Z > 6 && e.Z - p.Z < 150 && (target is null || e.Z < target.Z)) target = e;
            double aimX = double.NaN;
            bool fire = false;
            if (t >= nextDecision)
            {
                nextDecision = t + 0.1;   // a human's reaction time between key decisions
                if (target is not null)
                {
                    aimX = world.AimPoint(target.Z - p.Z).X;
                    double miss = target.X - aimX;
                    dir = miss > 0.5 ? 1 : miss < -0.5 ? -1 : 0;
                }
                else dir = p.X > 1.5 ? -1 : p.X < -1.5 ? 1 : 0;
            }
            if (target is not null)
            {
                aimX = world.AimPoint(target.Z - p.Z).X;
                fire = Math.Abs(target.X - aimX) < 1.5;
            }
            world.Update(new InputFrame { Gas = true, SteerDir = dir, Fire = fire }, dt);
            if (world.GunKills > kills) { kills = world.GunKills; killTimes.Add(t); }
            double rate = (p.Heading - prevHeading) / dt;
            prevHeading = p.Heading;
            log.Add((t, p.Heading, rate, p.Shake, p.Shake > 0 ? p.ShakeWhy : "", p.OffRoad, p.LateralVel, p.X));
            csv.Append(FormattableString.Invariant(
                $"{t:0.000},{p.Speed:0.00},{p.Steer:0.000},{p.Heading * 180 / Math.PI:0.000},{p.X:0.000},{p.LateralVel:0.000},{p.PushVel:0.000},{p.Shake:0.000},{(p.Shake > 0 ? p.ShakeWhy : "")},{(p.OffRoad ? 1 : 0)},{kills},{(target is null ? double.NaN : target.Z - p.Z):0.0},{(target is null ? double.NaN : target.X):0.00},{aimX:0.00}\n"));
        }

        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"gun kills: {killTimes.Count}");
        bool After(double t) => killTimes.Any(k => t >= k && t < k + 3);
        foreach (bool after in new[] { true, false })
        {
            var rows = log.Where(r => After(r.T) == after).ToList();
            double seconds = rows.Count * dt;
            int reversals = 0; double prev = 0;
            int latFlips = 0; double prevLat = 0;
            foreach (var r in rows)
            {
                if (Math.Abs(r.Rate) > 0.002) { if (prev != 0 && Math.Sign(r.Rate) != Math.Sign(prev)) reversals++; prev = r.Rate; }
                if (Math.Abs(r.LatVel) > 0.3) { if (prevLat != 0 && Math.Sign(r.LatVel) != Math.Sign(prevLat)) latFlips++; prevLat = r.LatVel; }
            }
            var why = rows.Where(r => r.Shake > 0).GroupBy(r => r.Why).Select(g => $"{g.Key} {g.Count() * dt:0.00}s");
            summary.AppendLine(FormattableString.Invariant(
                $"{(after ? "3 s after kills" : "rest of drive")}: {seconds:0.0}s, shaking {rows.Count(r => r.Shake > 0) * dt:0.00}s ({string.Join(", ", why)}), off road {rows.Count(r => r.Off) * dt:0.00}s, nose reversals {reversals / seconds:0.00}/s, sideways reversals {latFlips / seconds:0.00}/s, max nose {rows.Max(r => Math.Abs(r.Heading)) * 180 / Math.PI:0.0}°"));
        }
        foreach (double k in killTimes.Take(12))
        {
            var w = log.Where(r => r.T >= k && r.T < k + 3).ToList();
            var why = w.Where(r => r.Shake > 0).GroupBy(r => r.Why).Select(g => $"{g.Key} {g.Count() * dt:0.00}s");
            summary.AppendLine(FormattableString.Invariant(
                $"  kill at {k:0.0}s: shaking {w.Count(r => r.Shake > 0) * dt:0.00}s ({string.Join(", ", why)}), off road {w.Count(r => r.Off) * dt:0.00}s, x {w.Min(r => r.X):0.0}..{w.Max(r => r.X):0.0}"));
        }
        File.WriteAllText(path, csv.ToString());
        File.WriteAllText(Path.ChangeExtension(path, ".summary.txt"), summary.ToString());
    }

    /// <summary>A keyboard-style test driver: aims the nose at the nearest car ahead and fires.</summary>
    sealed class KillDriver
    {
        int _dir;
        double _nextDecision;
        public void Step(World world, double t, double dt)
        {
            var p = world.Player;
            EnemyCar? target = null;
            foreach (var e in world.Enemies)
                if (e.State != EnemyState.Wrecked && e.Z - p.Z > 6 && e.Z - p.Z < 150 && (target is null || e.Z < target.Z)) target = e;
            if (t >= _nextDecision)
            {
                _nextDecision = t + 0.1;
                if (target is not null)
                {
                    double miss = target.X - world.AimPoint(target.Z - p.Z).X;
                    _dir = miss > 0.5 ? 1 : miss < -0.5 ? -1 : 0;
                }
                else _dir = p.X > 1.5 ? -1 : p.X < -1.5 ? 1 : 0;
            }
            bool fire = target is not null && Math.Abs(target.X - world.AimPoint(target.Z - p.Z).X) < 1.5;
            world.Update(new InputFrame { Gas = true, SteerDir = _dir, Fire = fire }, dt);
        }
    }

    /// <summary>
    /// --kill-film out.png [--kill N] [--step 0.05]: drives with the kill driver until the Nth gun kill,
    /// then renders 16 frames after it into a 4×4 contact sheet (frame time printed on each).
    /// </summary>
    static void KillFilm(string[] args)
    {
        string path = Arg(args, "--kill-film") ?? "kill-film.png";
        int wanted = int.TryParse(Arg(args, "--kill"), out var k) ? k : 1;
        double frameStep = double.TryParse(Arg(args, "--step"), System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out var fs) ? fs : 0.05;
        const double dt = 1 / 120.0;
        var world = new World(new Tuning());
        world.ShowHelp = false;
        var driver = new KillDriver();
        double t = 0;
        while (world.GunKills < wanted && t < 600) { driver.Step(world, t, dt); t += dt; }

        const int fw = 640, fh = 360, cols = 4, rows = 4;
        var view = new GameView { World = world, Width = fw, Height = fh };
        view.Measure(new Size(fw, fh));
        view.Arrange(new Rect(0, 0, fw, fh));
        var frame = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(Brushes.DimGray, null, new Rect(0, 0, fw * cols + 6 * cols, fh * rows + 6 * rows));
            double killT = t;
            for (int i = 0; i < cols * rows; i++)
            {
                view.InvalidateVisual();
                view.UpdateLayout();
                frame.Clear();
                frame.Render(view);
                var copy = new WriteableBitmap(frame);
                copy.Freeze();
                var at = new Rect(i % cols * (fw + 6), i / cols * (fh + 6), fw, fh);
                dc.DrawImage(copy, at);
                var label = new FormattedText(FormattableString.Invariant($"+{t - killT:0.00}s  x {world.Player.X:0.00}  nose {world.Player.Heading * 180 / Math.PI:0.0}°  shake {world.Player.Shake:0.00}"),
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 14, Brushes.White, 1);
                dc.DrawText(label, new Point(at.X + 6, at.Y + fh - 20));
                for (double end = t + frameStep; t < end - 1e-9; t += dt) driver.Step(world, t, dt);
            }
        }
        var bitmap = new RenderTargetBitmap(cols * (fw + 6), rows * (fh + 6), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(sheet);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// --steer-test out.txt: compares steering settings on (1) a one-lane change at full speed, released
    /// when 3 m across, measuring overshoot past the lane centre (5 m) and settling time; (2) the kill driver
    /// in traffic for 3 minutes, measuring how much the view swings (nose reversals, mean nose turn rate)
    /// and how many cars it destroys; (3) the lane-keeping weave, measuring time off the road.
    /// </summary>
    static void SteerTest(string[] args)
    {
        const double dt = 1 / 120.0;
        var variants = new (string Name, Action<Tuning> Set)[]
        {
            ("old (assist only when released, 3/s, turn 52)", t => { t.AssistWhileSteering = false; t.Straighten = 3; t.TurnRateDeg = 52; }),
            ("new default (assist always, 8/s, turn 90)", t => { }),
        };
        var report = new System.Text.StringBuilder();
        foreach (var (name, set) in variants)
        {
            // (1) lane change on the straightest 400 m of the track, at 80 and 150 mph.
            var lane = new System.Text.StringBuilder();
            foreach (double mph in new[] { 80.0, 150.0 })
            {
                var tune = new Tuning(); set(tune);
                var w = new World(tune) { ShowHelp = false };
                w.Player.Z = StraightestStart(w.Track);
                w.Player.Speed = mph / Units.MphPerMs;
                w.Player.Throttle = 1;
                double peak = 0, released = -1, settled = -1;
                for (double t = 0; t < 4; t += dt)
                {
                    w.Enemies.Clear();
                    w.Player.Speed = mph / Units.MphPerMs;   // hold the test speed exactly
                    w.Update(new InputFrame { SteerDir = released < 0 ? 1 : 0 }, dt);
                    if (released < 0 && w.Player.X >= 3) released = t;
                    peak = Math.Max(peak, w.Player.X);
                    if (released >= 0 && settled < 0 && Math.Abs(w.Player.LateralVel) < 0.5) settled = t - released;
                }
                lane.Append(FormattableString.Invariant($"  lane change at {mph:0} mph: key held {released:0.00}s, peak {peak:0.0} m (lane centre 5), slide stops {settled:0.00}s after release\n"));
            }

            // (2) kill driver in traffic.
            var tune2 = new Tuning(); set(tune2);
            var w2 = new World(tune2) { ShowHelp = false };
            var driver = new KillDriver();
            int reversals = 0; double prevRate = 0, prevH = 0, sumRate = 0; int n = 0;
            for (double t2 = 0; t2 < 180; t2 += dt)
            {
                driver.Step(w2, t2, dt);
                double rate = (w2.Player.Heading - prevH) / dt; prevH = w2.Player.Heading;
                if (Math.Abs(rate) > 0.002) { if (prevRate != 0 && Math.Sign(rate) != Math.Sign(prevRate)) reversals++; prevRate = rate; }
                if (t2 > 10) { sumRate += Math.Abs(rate); n++; }
            }

            // (3) lane-keeping weave on the open road.
            var tune3 = new Tuning(); set(tune3);
            var w3 = new World(tune3) { ShowHelp = false };
            int d3 = 0; double next = 0, off = 0, maxH = 0;
            for (double t3 = 0; t3 < 60; t3 += dt)
            {
                var p = w3.Player;
                if (t3 >= next) { d3 = p.X > 1.5 ? -1 : p.X < -1.5 ? 1 : 0; next = t3 + 0.12; }
                w3.Enemies.Clear();
                w3.Update(new InputFrame { Gas = true, SteerDir = d3 }, dt);
                if (p.OffRoad) off += dt;
                maxH = Math.Max(maxH, Math.Abs(p.Heading));
            }

            report.AppendLine(FormattableString.Invariant(
                $"{name}\n{lane}  kill driver: {w2.GunKills} kills, nose reversals {reversals / 180.0:0.00}/s, mean nose turn {sumRate / n * 180 / Math.PI:0.0}°/s\n  weave: off road {off:0.0}s, max nose {maxH * 180 / Math.PI:0.0}°"));
        }
        File.WriteAllText(Arg(args, "--steer-test") ?? "steer-test.txt", report.ToString());

        static double StraightestStart(Track track)
        {
            double best = 0, bestK = double.MaxValue;
            for (double z = 0; z < track.Length - 400; z += 20)
            {
                double k = 0;
                for (double d = 0; d < 400; d += 10) k = Math.Max(k, Math.Abs(track.CurvatureAt(z + d)));
                if (k < bestK) { bestK = k; best = z; }
            }
            return best;
        }
    }

    public static void Run(string[] args)
    {
        if (Array.IndexOf(args, "--stress") >= 0) { Stress(args); return; }
        if (Array.IndexOf(args, "--trace") >= 0) { Trace(args); return; }
        if (Array.IndexOf(args, "--kill-trace") >= 0) { KillTrace(args); return; }
        if (Array.IndexOf(args, "--kill-film") >= 0) { KillFilm(args); return; }
        if (Array.IndexOf(args, "--steer-test") >= 0) { SteerTest(args); return; }
        string path = Arg(args, "--snapshot") ?? "snapshot.png";
        double seconds = double.TryParse(Arg(args, "--seconds"), out var s) ? s : 12;
        int scheme = int.TryParse(Arg(args, "--scheme"), out var sc) ? sc : 0;

        var world = new World(new Tuning());
        world.Handle(new GameAction(ActionKind.SelectScheme, scheme));
        if (scheme == 1 || scheme == 3) world.Handle(new GameAction(ActionKind.LeverPreset, 8));

        const double dt = 1 / 120.0;
        bool oilDropped = false;
        for (double t = 0; t < seconds; t += dt)
        {
            var p = world.Player;
            var tune = world.Tuning;
            // Autopilot: cancel the road's turning and steer the nose gently back toward the centre lane.
            double rate = tune.TurnRateAt(p.Speed);
            double wantHeading = Math.Clamp(-0.04 * p.X, -0.08, 0.08);
            double needed = world.Track.CurvatureAt(p.Z) * p.Speed + (wantHeading - p.Heading) * 4;
            var input = new InputFrame
            {
                Gas = scheme is 0 or 2,
                Fire = t > seconds - 1.2,
                PadConnected = true,
                PadSteer = rate > 0.01 ? Math.Clamp(needed / rate, -1, 1) : 0,
            };
            if (scheme == 2 && p.Speed > 0.9 * world.Scheme.GearTopSpeed.GetValueOrDefault(999))
                world.Handle(new GameAction(ActionKind.ShiftUp));
            if (!oilDropped && t > seconds * 0.6) { world.Handle(new GameAction(ActionKind.DropOil)); oilDropped = true; }
            world.Update(input, dt);
        }
        world.ShowHelp = Array.IndexOf(args, "--help-overlay") >= 0;
        if (Array.IndexOf(args, "--offroad") >= 0)
        {
            world.Player.X = -9;
            world.Update(new InputFrame { Gas = true }, 1 / 120.0);
        }
        if (Array.IndexOf(args, "--close-cars") >= 0)
        {
            var p = world.Player;
            world.Enemies.Clear();
            world.Enemies.Add(new EnemyCar { Kind = EnemyKind.Runner, Z = p.Z + 22, X = p.X + 4, Health = 3, MaxHealth = 6 });
            world.Enemies.Add(new EnemyCar { Kind = EnemyKind.Blocker, Z = p.Z + 70, X = p.X - 3, Health = 14, MaxHealth = 14 });
            world.Enemies.Add(new EnemyCar { Kind = EnemyKind.Pursuer, Z = p.Z + 140, X = p.X, Health = 10, MaxHealth = 10,
                                             State = EnemyState.Spinning, SpinAngle = 0.7 });
            world.Enemies.Add(new EnemyCar { Kind = EnemyKind.Pursuer, Z = p.Z - 25, X = p.X - 3, Health = 10, MaxHealth = 10 });
            world.Slicks.Add(new OilSlick { Z = p.Z + 40, X = p.X + 1 });
        }

        const int w = 1280, h = 720;
        var view = new GameView { World = world, Width = w, Height = h };
        view.Measure(new Size(w, h));
        view.Arrange(new Rect(0, 0, w, h));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
