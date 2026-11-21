using NAudio.Wave;
using NightPursuit.Core.Audio;

namespace NightPursuit.Core.Tests.Audio;

public class SmallPiecesTests
{
    [Fact]
    public void Smoothed_reaches_its_target_in_time_without_overshoot()
    {
        var s = new Smoothed(0.01) { Target = 1.0 };
        int steps = (int)(0.01 * AudioFormat.SampleRate); // 441
        double prev = 0;
        for (int i = 0; i < steps; i++)
        {
            double v = s.Next();
            Assert.InRange(v, prev, 1.0);
            prev = v;
        }
        Assert.Equal(1.0, prev, 9);
        Assert.Equal(1.0, s.Next(), 9);

        // A new target part-way down is also reached in time, and not undershot.
        s.Target = 0.2;
        for (int i = 0; i < steps; i++)
        {
            double v = s.Next();
            Assert.InRange(v, 0.2, prev);
            prev = v;
        }
        Assert.Equal(0.2, prev, 9);
    }

    [Fact]
    public void SeededNoise_with_the_same_seed_repeats()
    {
        var a = new float[1000];
        var b = new float[1000];
        var c = new float[1000];
        new SeededNoise(5).Read(a);
        new SeededNoise(5).Read(b);
        new SeededNoise(6).Read(c);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.All(a, v => Assert.InRange(v, -1f, 1f));
        Assert.Contains(a, v => v < -0.5f);
        Assert.Contains(a, v => v > 0.5f);
    }

    private sealed class Fixed(float[] data) : ISampleProvider
    {
        public WaveFormat WaveFormat => AudioFormat.Mono;
        public int Read(Span<float> buffer) { data.AsSpan(0, buffer.Length).CopyTo(buffer); return buffer.Length; }
    }

    [Fact]
    public void HardClamp_never_passes_its_ceiling()
    {
        float[] data = [0.2f, -0.2f, 3f, -3f, 0.891f, 1e6f, -1e6f];
        var clamp = new HardClamp(new Fixed(data));
        var buf = new float[data.Length];
        Assert.Equal(data.Length, clamp.Read(buf));
        Assert.All(buf, v => Assert.InRange(v, -0.891f, 0.891f));
        Assert.Equal(0.2f, buf[0]);
        Assert.Equal(-0.2f, buf[1]);
        Assert.Equal(0.891f, buf[2]);
        Assert.Equal(-0.891f, buf[3]);
    }

    private static string TempPath(string name) => Path.Combine(Path.GetTempPath(), $"np-audio-{Guid.NewGuid():N}-{name}");

    [Fact]
    public void Wav_round_trip_keeps_samples_to_16_bit_precision()
    {
        var path = TempPath("mono.wav");
        var stereoPath = TempPath("stereo.wav");
        try
        {
            var samples = new float[2000];
            for (int i = 0; i < samples.Length; i++) samples[i] = 0.8f * MathF.Sin(i * 0.05f);
            SoundFiles.WriteMono16(path, samples);
            var back = SoundFiles.LoadMono(path);
            Assert.Equal(samples.Length, back.Length);
            for (int i = 0; i < samples.Length; i++) Assert.Equal(samples[i], back[i], 1e-4f);

            // Stereo is averaged to mono: left 0.5, right -0.1 gives 0.2.
            var stereo = new float[200];
            for (int i = 0; i < stereo.Length; i += 2) { stereo[i] = 0.5f; stereo[i + 1] = -0.1f; }
            SoundFiles.WriteStereo16(stereoPath, stereo);
            var mono = SoundFiles.LoadMono(stereoPath);
            Assert.Equal(100, mono.Length);
            Assert.All(mono, v => Assert.Equal(0.2f, v, 1e-4f));
        }
        finally { File.Delete(path); File.Delete(stereoPath); }
    }

    [Fact]
    public void Loading_an_8_bit_or_48_kHz_wav_throws_a_plain_message()
    {
        var eight = TempPath("8bit.wav");
        var fortyEight = TempPath("48k.wav");
        try
        {
            using (var w = new WaveFileWriter(eight, new WaveFormat(44100, 8, 1))) w.Write(new byte[100], 0, 100);
            using (var w = new WaveFileWriter(fortyEight, new WaveFormat(48000, 16, 1))) w.Write(new byte[200], 0, 200);
            var e1 = Assert.Throws<InvalidDataException>(() => SoundFiles.LoadMono(eight));
            var e2 = Assert.Throws<InvalidDataException>(() => SoundFiles.LoadMono(fortyEight));
            Assert.Contains("16-bit", e1.Message);
            Assert.Contains("44,100", e2.Message);
        }
        finally { File.Delete(eight); File.Delete(fortyEight); }
    }
}
