using System.Globalization;
using NightPursuit.Core.Audio;
using SynthSounds;

// Usage: dotnet run --project tools/SynthSounds -- <content/sounds directory>
// Writes one WAV per SoundCue plus report.txt. Exits 1 if a peak is above -1 dBFS or a file is too long.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SynthSounds <content/sounds directory>");
    return 2;
}

string dir = args[0];
Directory.CreateDirectory(dir);

var keepPath = Path.Combine(dir, "keep.txt");
var keep = File.Exists(keepPath)
    ? File.ReadAllLines(keepPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase)
    : [];

var report = new List<string>();
bool failed = false;
foreach (var cue in Enum.GetValues<SoundCue>())
{
    string name = SoundCues.FileName(cue);
    string path = Path.Combine(dir, name);
    bool kept = keep.Contains(name) && File.Exists(path);
    // A kept file is the owner's own sound and may be any normal WAV, as the game allows: only measure it, never rewrite it.
    float[] samples = kept ? SoundFiles.LoadConvertedMono(path) : Recipes.Render(cue);
    if (!kept) SoundFiles.WriteMono16(path, samples);

    double seconds = samples.Length / (double)AudioFormat.SampleRate;
    double peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
    double rms = samples.Length == 0 ? 0 : Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);
    double peakDb = Dsp.Db(peak), rmsDb = Dsp.Db(rms);
    string problem = "";
    if (peakDb > -1) { problem += " PEAK TOO HIGH"; failed = true; }
    if (seconds > SoundCues.MaxSeconds(cue)) { problem += " TOO LONG"; failed = true; }
    report.Add(string.Create(CultureInfo.InvariantCulture,
        $"{name,-32} {seconds,5:F2} s  peak {peakDb,6:F1} dBFS  rms {rmsDb,6:F1} dBFS{(kept ? "  (kept)" : "")}{problem}"));
}

File.WriteAllLines(Path.Combine(dir, "report.txt"), report);
foreach (var line in report) Console.WriteLine(line);
return failed ? 1 : 0;
