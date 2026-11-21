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
}
