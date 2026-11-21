namespace NightPursuit.Core.Audio;

/// <summary>Every effect sound, loaded once into memory. A cue whose file is missing or bad stays silent.</summary>
public sealed class SoundBank
{
    private readonly float[]?[] _samples = new float[]?[Enum.GetValues<SoundCue>().Length];

    private SoundBank() { }

    /// <summary>
    /// Loads every cue's WAV from <paramref name="soundsDir"/>. Each file that is missing or unusable adds a
    /// plain-words line to <paramref name="problems"/> and leaves that cue silent. Never throws for file problems.
    /// </summary>
    public static SoundBank Load(string soundsDir, out IReadOnlyList<string> problems)
    {
        var bank = new SoundBank();
        var list = new List<string>();
        foreach (var cue in Enum.GetValues<SoundCue>())
        {
            var name = SoundCues.FileName(cue);
            var path = Path.Combine(soundsDir, name);
            try
            {
                if (!File.Exists(path)) { list.Add(Problem(name, "file not found")); continue; }
                var samples = SoundFiles.LoadConvertedMono(path);   // any normal WAV: converted to 44,100 Hz mono
                if (samples.Length == 0) { list.Add(Problem(name, "the file is empty")); continue; }
                bank._samples[(int)cue] = samples;
            }
            // Any problem with one file leaves that cue silent; only the fatal errors are let through.
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException
                                                or ThreadAbortException or AppDomainUnloadedException))
            {
                list.Add(Problem(name, e.Message));
            }
        }
        problems = list;
        return bank;
    }

    // "SOUND FILE gun-player.wav: FILE NOT FOUND": the file name keeps its real spelling so it can be found, the rest is upper case.
    static string Problem(string fileName, string message) => $"SOUND FILE {fileName}: {message.ToUpperInvariant()}";

    /// <summary>The cue's mono samples (44,100 Hz), or null if it could not be loaded. Do not change the array.</summary>
    public float[]? Samples(SoundCue c) => _samples[(int)c];
}
