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
                if (!File.Exists(path)) { list.Add($"{name}: file not found"); continue; }
                var samples = SoundFiles.LoadMono(path);
                if (samples.Length == 0) { list.Add($"{name}: the file is empty"); continue; }
                bank._samples[(int)cue] = samples;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException
                                          or UnauthorizedAccessException or ArgumentException)
            {
                list.Add($"{name}: {e.Message}");
            }
        }
        problems = list;
        return bank;
    }

    /// <summary>The cue's mono samples (44,100 Hz), or null if it could not be loaded. Do not change the array.</summary>
    public float[]? Samples(SoundCue c) => _samples[(int)c];
}
