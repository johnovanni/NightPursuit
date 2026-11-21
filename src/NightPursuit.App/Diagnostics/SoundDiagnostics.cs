using System.Globalization;
using System.IO;
using System.Text;
using NightPursuit.App.Audio;
using NightPursuit.Core.Audio;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Diagnostics;

/// <summary>
/// <c>--sound-check out.wav [--mission mission-01] [--seconds 60]</c>: drives a practice mission on the autopilot, mixes the
/// sound offline into a WAV file (no sound device is ever opened) and logs every cue and music change.
/// Also the sound timing for <c>--perf</c>.
/// </summary>
static class SoundDiagnostics
{
    const double Step = 1.0 / 60;
    const int FramesPerStep = 735;          // 44,100 / 60 stereo frames
    const float Ceiling = AudioFormat.Ceiling;   // -1 dBFS, the mixer's hard clamp
    /// <summary>The longest check: the whole mix is kept in memory (about 2.5 GB for an hour), so it is capped at ten minutes.</summary>
    const double MaxSeconds = 600;

    /// <summary>A practice game of the mission, on the Briefing screen, sound defaults on.</summary>
    static (ScreenFlow Flow, MissionDefinition Def) NewFlow(string mission)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "missions", mission + ".json");
        var def = MissionLoader.LoadFile(path);
        var settings = new GameSettings();
        var feel = new FeelSettings { SpeedSensitiveSteering = settings.LighterSteering };
        var flow = new ScreenFlow(
            isPractice => new Game(new[] { def }, feel, settings, practice: true),
            feel, settings, new HighScoreTable(), new MemoryGameStore());
        flow.StartGame(practice: true);
        return (flow, def);
    }

    /// <summary>Confirms the briefing as a player would (a menu Confirm) and dismisses the intro help.</summary>
    static void BeginPlay(ScreenFlow flow, SoundSystem sound)
    {
        var confirm = new MenuKey(MenuInput.Confirm);
        flow.Handle(confirm);
        sound.NoteMenuKey(confirm);
        flow.IntroHelpSeen();
    }

    static void Drive(ScreenFlow flow)
    {
        if (flow.Current == Screen.Play && flow.Game?.Run is { } run)
        {
            // The autopilot only drives; the guns are held down as well so the shooting sounds are exercised.
            var drive = Autopilot.Next(run.World);
            flow.Update(new ControlFrame { Gas = drive.Gas, PadConnected = true, PadSteer = drive.PadSteer, Fire = true }, Step);
        }
    }

    public static int SoundCheck(string[] args)
    {
        string? wavPath = DiagnosticsRunner.Arg(args, "--sound-check");
        if (wavPath is null || wavPath.StartsWith("--"))
        {
            DiagnosticsRunner.Error("--sound-check needs an output file name, e.g. --sound-check sound/check.wav");
            return 2;
        }
        string mission = DiagnosticsRunner.Arg(args, "--mission") ?? "mission-01";
        if (mission.Length == 0 || !mission.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
        {
            DiagnosticsRunner.Error($"--mission needs a mission name of letters, digits and '-' only, e.g. mission-01, not '{mission}'");
            return 2;
        }
        double seconds = 60;
        string? secondsText = DiagnosticsRunner.Arg(args, "--seconds");
        if (secondsText is not null
            && !(double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                 && double.IsFinite(seconds) && seconds > 0 && seconds <= MaxSeconds))
        {
            DiagnosticsRunner.Error($"--seconds needs a number of seconds above 0 and up to {MaxSeconds:0} (the whole mix is kept in memory), not '{secondsText}'");
            return 2;
        }

        string contentDir = Path.Combine(AppContext.BaseDirectory, "content");
        var (flow, def) = NewFlow(mission);
        using var sound = new SoundSystem(contentDir, new[] { def }, openDevice: false);
        if (sound.HasOutput) throw new InvalidOperationException("the sound check must not open a sound device");

        var log = new StringBuilder();
        var counts = new SortedDictionary<string, int>();
        var inv = CultureInfo.InvariantCulture;
        MusicCue? lastMusic = null;
        bool engine = false, siren = false, whine = false, offRoad = false, skid = false;
        double? bossAt = null;

        int steps = (int)Math.Round(seconds / Step);
        var samples = new float[steps * FramesPerStep * 2];
        var buffer = new float[FramesPerStep * 2];
        float peak = 0;
        int over = 0;
        double mixMs = 0;

        BeginPlay(flow, sound);
        for (int i = 0; i < steps; i++)
        {
            double t = i * Step;
            if (i > 0) Drive(flow);
            sound.Update(flow, Step);
            if (sound.LastFrame is { } f)
            {
                foreach (var c in f.Cues)
                {
                    log.AppendLine(string.Create(inv, $"t={t:0.00} {c.Cue} vol={c.Volume:0.00} pan={c.Pan:0.00}"));
                    string key = c.Cue.ToString();
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    if (c.Cue == SoundCue.BossAppears) bossAt ??= t;
                }
                Edge(log, inv, t, "Engine", f.Live.EngineOn, ref engine, counts);
                Edge(log, inv, t, "Siren", f.Live.Siren01 > 0, ref siren, counts);
                Edge(log, inv, t, "MissileWhine", f.Live.Whine01 > 0, ref whine, counts);
                Edge(log, inv, t, "OffRoadRumble", f.Live.OffRoad01 > 0, ref offRoad, counts);
                Edge(log, inv, t, "Skid", f.Live.Skid01 > 0, ref skid, counts);
                if (lastMusic is not { } m || !m.SameMusic(f.Music))
                {
                    log.AppendLine(string.Create(inv, $"t={t:0.00} Music slot={f.Music.Slot} file={f.Music.Track?.File ?? "(silence)"}"));
                    counts["Music change"] = counts.GetValueOrDefault("Music change") + 1;
                }
                lastMusic = f.Music;
            }

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            sound.Mixer.Read(buffer);
            mixMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            buffer.CopyTo(samples, i * buffer.Length);
            foreach (float s in buffer)
            {
                float a = Math.Abs(s);
                if (a > peak) peak = a;
                if (a > Ceiling + 1e-6f || float.IsNaN(s)) over++;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(wavPath))!);
        SoundFiles.WriteStereo16(wavPath, samples);

        var missing = Enum.GetValues<SoundCue>()
            .Where(c => !File.Exists(Path.Combine(contentDir, "sounds", SoundCues.FileName(c))))
            .Select(SoundCues.FileName).ToList();
        double audioMs = steps * Step * 1000;
        double peakDb = peak > 0 ? 20 * Math.Log10(peak) : double.NegativeInfinity;
        bool pass = over == 0 && missing.Count == 0 && mixMs < audioMs;

        var summary = new StringBuilder();
        summary.AppendLine(string.Create(inv, $"sound-check: {mission}, {seconds:0.##} s on the autopilot, ended on screen {flow.Current}; no sound device opened"));
        summary.AppendLine(string.Create(inv, $"peak {peakDb:0.00} dBFS ({peak:0.000}); samples over the {Ceiling} ceiling: {over}   ({(over == 0 ? "PASS" : "FAIL")})"));
        summary.AppendLine("cue counts: " + (counts.Count == 0 ? "(none)" : string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"))));
        summary.AppendLine(bossAt is { } b ? string.Create(inv, $"boss reached: BossAppears cue at t={b:0.00}") : "boss not reached in this run");
        summary.AppendLine(string.Create(inv, $"mixing time {mixMs:0.0} ms for {audioMs:0} ms of audio ({mixMs / audioMs * 100:0.0}%)   ({(mixMs < audioMs ? "PASS" : "FAIL")})"));
        summary.AppendLine(missing.Count == 0 ? "every cue has its sound file   (PASS)" : "missing sound files: " + string.Join(", ", missing) + "   (FAIL)");
        foreach (string problem in sound.Problems) summary.AppendLine("sound notice: " + problem);

        DiagnosticsRunner.Progress(log.ToString().TrimEnd());
        DiagnosticsRunner.Progress(summary.ToString().TrimEnd());
        Console.Out.Write(summary.ToString());
        return pass ? 0 : 1;
    }

    static void Edge(StringBuilder log, IFormatProvider inv, double t, string name, bool now, ref bool was, SortedDictionary<string, int> counts)
    {
        if (now == was) return;
        was = now;
        log.AppendLine(string.Create(inv, $"t={t:0.00} {name} {(now ? "on" : "off")}"));
        if (now) counts[name + " starts"] = counts.GetValueOrDefault(name + " starts") + 1;
    }

    /// <summary>
    /// Times <see cref="SoundSystem.Update"/> (the director and the live sounds, no device) over a practice mission on the
    /// autopilot: 12 s of play first, then <paramref name="warmUp"/> warm-up and <paramref name="frames"/> timed frames.
    /// </summary>
    public static List<double> TimeUpdate(int warmUp, int frames)
    {
        var (flow, def) = NewFlow("mission-01");
        using var sound = new SoundSystem(Path.Combine(AppContext.BaseDirectory, "content"), new[] { def }, openDevice: false);
        BeginPlay(flow, sound);
        for (double t = 0; t < 12; t += Step) { Drive(flow); sound.Update(flow, Step); }
        var times = new List<double>(frames);
        var buffer = new float[FramesPerStep * 2];
        for (int i = -warmUp; i < frames; i++)
        {
            Drive(flow);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            sound.Update(flow, Step);
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            sound.Mixer.Read(buffer);   // keeps the cues' voices ending as they would in the game (not timed)
            if (i >= 0) times.Add(ms);
        }
        return times;
    }
}
