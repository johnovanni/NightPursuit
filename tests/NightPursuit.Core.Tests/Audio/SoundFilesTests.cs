using System.Text.RegularExpressions;
using NAudio.Wave;
using NightPursuit.Core.Audio;

namespace NightPursuit.Core.Tests.Audio;

/// <summary>Checks the generated effect WAVs in <c>content/sounds</c>.</summary>
public class SoundFilesTests
{
    private static string SoundsDir => Path.Combine(AppContext.BaseDirectory, "content", "sounds");
    private static string PathOf(SoundCue c) => Path.Combine(SoundsDir, SoundCues.FileName(c));

    public static IEnumerable<object[]> Cues() => Enum.GetValues<SoundCue>().Select(c => new object[] { c });

    private static double Db(double linear) => 20 * Math.Log10(Math.Max(linear, 1e-9));

    [Fact]
    public void Every_cue_has_its_wav_file()
    {
        var missing = Enum.GetValues<SoundCue>().Where(c => !File.Exists(PathOf(c))).ToList();
        Assert.True(missing.Count == 0, "missing: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void Every_sound_file_is_44_1_kHz_16_bit_mono_and_reads(SoundCue cue)
    {
        using (var reader = new WaveFileReader(PathOf(cue)))
        {
            Assert.Equal(44100, reader.WaveFormat.SampleRate);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            Assert.Equal(1, reader.WaveFormat.Channels);
        }
        Assert.NotEmpty(SoundFiles.LoadMono(PathOf(cue)));
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void No_sound_peaks_above_minus_1_dBFS(SoundCue cue)
    {
        var peak = SoundFiles.LoadMono(PathOf(cue)).Max(Math.Abs);
        Assert.True(Db(peak) <= -1, $"{cue} peaks at {Db(peak):F2} dBFS");
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void No_sound_is_longer_than_its_limit(SoundCue cue)
    {
        double seconds = SoundFiles.LoadMono(PathOf(cue)).Length / 44100.0;
        Assert.True(seconds <= SoundCues.MaxSeconds(cue), $"{cue} is {seconds:F2} s");
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void Every_sound_starts_and_ends_near_silence(SoundCue cue)
    {
        var s = SoundFiles.LoadMono(PathOf(cue));
        int n = (int)(0.002 * 44100);
        var head = s.Take(n).Max(Math.Abs);
        var tail = s.Skip(s.Length - n).Max(Math.Abs);
        Assert.True(Db(head) < -40, $"{cue} starts at {Db(head):F1} dBFS");
        Assert.True(Db(tail) < -40, $"{cue} ends at {Db(tail):F1} dBFS");
    }

    [Fact]
    public void File_names_are_kebab_case()
    {
        var kebab = new Regex(@"^[a-z]+(-[a-z]+)*\.wav$");
        foreach (var c in Enum.GetValues<SoundCue>())
            Assert.Matches(kebab, SoundCues.FileName(c));
        foreach (var f in Directory.GetFiles(SoundsDir, "*.wav"))
            Assert.Matches(kebab, Path.GetFileName(f));
        Assert.Equal("gun-player.wav", SoundCues.FileName(SoundCue.GunPlayer));
        Assert.Equal("enemy-missile-shot-down.wav", SoundCues.FileName(SoundCue.EnemyMissileShotDown));
    }

    // ---- swapped-in effects: any normal WAV is converted to 44,100 Hz mono ----

    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "np-bank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void WriteConstant24(string path, int rate, int channels, double seconds, double level)
    {
        using var w = new WaveFileWriter(path, new WaveFormat(rate, 24, channels));
        int frames = (int)(seconds * rate);
        int v = (int)(level * 8388607);
        var bytes = new byte[frames * channels * 3];
        for (int i = 0; i < frames * channels; i++)
        {
            bytes[i * 3] = (byte)v;
            bytes[i * 3 + 1] = (byte)(v >> 8);
            bytes[i * 3 + 2] = (byte)(v >> 16);
        }
        w.Write(bytes, 0, bytes.Length);
    }

    [Fact]
    public void A_48_kHz_24_bit_stereo_wav_loads_as_44_1_kHz_mono_of_the_right_length()
    {
        var dir = NewDir();
        try
        {
            WriteConstant24(Path.Combine(dir, SoundCues.FileName(SoundCue.Pickup)), 48000, 2, 0.5, 0.4);
            var bank = SoundBank.Load(dir, out var problems);
            Assert.DoesNotContain(problems, p => p.Contains("pickup.wav"));
            var samples = bank.Samples(SoundCue.Pickup)!;
            Assert.InRange(samples.Length, 22050 - 200, 22050 + 200);   // 0.5 s at 44,100 Hz
            Assert.Equal(0.4f, samples[samples.Length / 2], 0.01f);     // stereo averaged, level kept

            var mixer = new AudioMixer(bank) { SoundOn = true, EffectsVolume = 10 };
            mixer.Play(new CuePlay(SoundCue.Pickup));
            Assert.Equal(1, mixer.ActiveVoices);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_float_mono_wav_at_22_kHz_loads_too_and_a_44_1_kHz_one_is_not_resampled()
    {
        var dir = NewDir();
        try
        {
            using (var w = new WaveFileWriter(Path.Combine(dir, SoundCues.FileName(SoundCue.MenuMove)), WaveFormat.CreateIeeeFloatWaveFormat(22050, 1)))
                w.WriteSamples(Enumerable.Repeat(0.3f, 11025).ToArray(), 0, 11025);   // 0.5 s
            SoundFiles.WriteMono16(Path.Combine(dir, SoundCues.FileName(SoundCue.MenuBack)), new float[4410]);
            var bank = SoundBank.Load(dir, out _);
            Assert.InRange(bank.Samples(SoundCue.MenuMove)!.Length, 22050 - 200, 22050 + 200);
            Assert.Equal(4410, bank.Samples(SoundCue.MenuBack)!.Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_bad_effect_file_is_reported_in_upper_case_naming_the_file_once()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, SoundCues.FileName(SoundCue.Explosion)), "not a wav");
            using (var w = new WaveFileWriter(Path.Combine(dir, SoundCues.FileName(SoundCue.Warning)), new WaveFormat(44100, 16, 6)))
                w.Write(new byte[120], 0, 120);
            SoundBank.Load(dir, out var problems);
            foreach (var name in new[] { "explosion.wav", "warning.wav" })
            {
                var p = Assert.Single(problems, x => x.Contains(name));
                Assert.Equal(1, p.Split(name).Length - 1);
                Assert.StartsWith("SOUND FILE " + name + ": ", p);
                Assert.Equal(p["SOUND FILE ".Length..].Replace(name, "").ToUpperInvariant(), p["SOUND FILE ".Length..].Replace(name, ""));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadMono_stays_strict_about_the_shipped_format()
    {
        var path = Path.Combine(Path.GetTempPath(), $"np-strict-{Guid.NewGuid():N}.wav");
        try
        {
            WriteConstant24(path, 48000, 2, 0.1, 0.2);
            Assert.Throws<InvalidDataException>(() => SoundFiles.LoadMono(path));
        }
        finally { File.Delete(path); }
    }
}
