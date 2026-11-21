using NAudio.Dsp;
using NAudio.Effects;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NightPursuit.Core.Audio;

namespace NightPursuit.Core.Tests.Audio;

/// <summary>
/// Pins down how NAudio.Core 3.3.0 really behaves, so the later audio tasks can rely on facts instead of memory
/// of the 2.x API.
/// </summary>
public class NAudioChecksTests
{
    private const int Rate = AudioFormat.SampleRate;

    /// <summary>A provider that returns the same value for ever.</summary>
    private sealed class Constant(float value, WaveFormat format) : ISampleProvider
    {
        public WaveFormat WaveFormat => format;
        public int Read(Span<float> buffer) { buffer.Fill(value); return buffer.Length; }
    }

    /// <summary>Plays a float array once, then reports the end.</summary>
    private sealed class BufferSource(float[] data, WaveFormat format) : ISampleProvider
    {
        private int _pos;
        public WaveFormat WaveFormat => format;
        public int Read(Span<float> buffer)
        {
            int n = Math.Min(buffer.Length, data.Length - _pos);
            data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
    }

    private static float[] ReadAll(ISampleProvider p, int count)
    {
        var buf = new float[count];
        Assert.Equal(count, p.Read(buf));
        return buf;
    }

    [Fact]
    public void SignalGenerator_sine_at_441_Hz_crosses_zero_882_times_a_second()
    {
        var gen = new SignalGenerator(Rate, 1) { Type = SignalGeneratorType.Sin, Frequency = 441, Gain = 1 };
        var s = ReadAll(gen, Rate);
        int crossings = 0;
        for (int i = 1; i < s.Length; i++)
            if ((s[i - 1] < 0) != (s[i] < 0)) crossings++;
        Assert.InRange(crossings, 880, 884);
    }

    // Observed (NAudio.Core 3.3.0): SignalGenerator keeps its own phase accumulator, so changing Frequency
    // mid-stream bends the pitch without a jump. The live voices can use SignalGenerator directly and
    // no GlideOscillator is needed.
    [Theory]
    [InlineData(SignalGeneratorType.Sin)]
    [InlineData(SignalGeneratorType.Triangle)]
    public void SignalGenerator_stays_phase_continuous_when_frequency_changes(SignalGeneratorType type)
    {
        var gen = new SignalGenerator(Rate, 1) { Type = type, Frequency = 100, Gain = 1 };
        // 1103 samples is not a whole number of cycles at 100 or 200 Hz, so a phase reset would show as a jump.
        var first = ReadAll(gen, 1103);
        gen.Frequency = 200;
        var second = ReadAll(gen, 1000);
        var all = first.Concat(second).ToArray();
        float largestStep = 0;
        for (int i = 1; i < all.Length; i++) largestStep = Math.Max(largestStep, Math.Abs(all[i] - all[i - 1]));
        // The steepest a 200 Hz sine goes is 2*pi*200/44100 = 0.0285 per sample; a triangle's slope is 4*200/44100 = 0.0181.
        Assert.True(largestStep < 0.03f, $"largest step was {largestStep}");

        // The change really took effect: the second part has about twice the zero crossings per sample.
        static int Crossings(float[] s) { int c = 0; for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0) != (s[i] < 0)) c++; return c; }
        Assert.True(Crossings(second) > 1.7 * Crossings(first) * 1000 / 1103);
    }

    [Fact]
    public void LimiterEffect_at_minus_1_dB_with_hard_clamp_never_passes_0_891()
    {
        var rng = new Random(1);
        var noise = new float[Rate * 2];
        for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 8 - 4);
        var src = new BufferSource(noise, AudioFormat.Stereo);
        var limiter = new LimiterEffect { CeilingDb = -1, LookaheadMs = 2, ReleaseMs = 100 };
        var chain = new HardClamp(new EffectSampleProvider(src, limiter));
        var buf = new float[noise.Length];
        int n = chain.Read(buf);
        Assert.True(n > 0);
        float peak = 0;
        for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        Assert.True(peak <= 0.891f, $"peak was {peak}");
    }

    [Fact]
    public void LimiterEffect_leaves_quiet_audio_unchanged()
    {
        const int frames = 4000;
        var input = new float[frames * 2];
        for (int i = 0; i < frames; i++)
            input[2 * i] = input[2 * i + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * i / Rate);
        var limiter = new LimiterEffect { CeilingDb = -1, LookaheadMs = 2, ReleaseMs = 100 };
        var provider = new EffectSampleProvider(new BufferSource(input, AudioFormat.Stereo), limiter);
        var output = new float[input.Length];
        provider.Read(output);
        int delay = limiter.LatencySamples;
        Assert.True(delay > 0);
        for (int i = 0; i < frames - delay; i++)
        {
            Assert.Equal(input[2 * i], output[2 * (i + delay)], 1e-3f);
            Assert.Equal(input[2 * i + 1], output[2 * (i + delay) + 1], 1e-3f);
        }
    }

    [Fact]
    public void LimiterEffect_latency_is_under_3_ms()
    {
        var limiter = new LimiterEffect { CeilingDb = -1, LookaheadMs = 2, ReleaseMs = 100 };
        _ = new EffectSampleProvider(new Constant(0, AudioFormat.Stereo), limiter); // configures the effect
        double ms = limiter.LatencySamples * 1000.0 / Rate;
        Assert.InRange(ms, 0.01, 3.0);
    }

    [Fact]
    public void BiQuadFilter_low_pass_at_200_Hz_cuts_a_5_kHz_sine_by_at_least_20_dB()
    {
        var filter = BiQuadFilter.LowPassFilter(Rate, 200, 0.7071f);
        const int n = 8820;
        double inSq = 0, outSq = 0;
        for (int i = 0; i < n; i++)
        {
            float x = MathF.Sin(2 * MathF.PI * 5000 * i / Rate);
            float y = filter.Transform(x);
            if (i >= 2000) { inSq += x * x; outSq += y * y; }
        }
        double db = 10 * Math.Log10(outSq / inSq);
        Assert.True(db <= -20, $"cut was {db} dB");
    }

    [Fact]
    public void EnvelopeGenerator_rises_holds_and_releases()
    {
        var env = new EnvelopeGenerator
        {
            AttackRate = 0.01f * Rate,
            DecayRate = 0.01f * Rate,
            ReleaseRate = 0.01f * Rate,
            SustainLevel = 0.5f,
        };
        Assert.Equal(EnvelopeGenerator.EnvelopeState.Idle, env.State);
        env.Gate(true);
        float peak = 0;
        for (int i = 0; i < 0.01 * Rate * 2; i++) peak = Math.Max(peak, env.Process());
        Assert.True(peak >= 0.99f, $"attack peak was {peak}");
        for (int i = 0; i < 4410; i++) env.Process(); // let it decay and hold
        Assert.Equal(EnvelopeGenerator.EnvelopeState.Sustain, env.State);
        Assert.Equal(0.5f, env.Process(), 0.01f);
        env.Gate(false);
        float last = 1;
        for (int i = 0; i < 0.01 * Rate * 3; i++) last = env.Process();
        Assert.Equal(EnvelopeGenerator.EnvelopeState.Idle, env.State);
        Assert.Equal(0f, last, 0.01f);
    }

    [Fact]
    public void MixingSampleProvider_with_ReadFully_fills_the_buffer_when_empty()
    {
        var mixer = new MixingSampleProvider(AudioFormat.Stereo) { ReadFully = true };
        var buf = new float[512];
        Array.Fill(buf, 7f);
        Assert.Equal(512, mixer.Read(buf));
        Assert.All(buf, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void PanningSampleProvider_full_left_is_silent_on_the_right()
    {
        var mono = new SignalGenerator(Rate, 1) { Type = SignalGeneratorType.Sin, Frequency = 440, Gain = 1 };
        var pan = new PanningSampleProvider(mono) { Pan = -1f };
        var buf = ReadAll(pan, 2000);
        float leftPeak = 0, rightPeak = 0;
        for (int i = 0; i < buf.Length; i += 2)
        {
            leftPeak = Math.Max(leftPeak, Math.Abs(buf[i]));
            rightPeak = Math.Max(rightPeak, Math.Abs(buf[i + 1]));
        }
        Assert.True(leftPeak > 0.5f);
        Assert.True(rightPeak < 1e-6f);
    }

    // The package docs say: "Gain changes are smoothed so automating the level never clicks."
    // This pins the behaviour: a sudden gain change is spread over many samples, not applied in one step.
    [Fact]
    public void GainEffect_ramps_a_gain_change_instead_of_jumping()
    {
        var gain = new GainEffect { LinearGain = 1f };
        var provider = new EffectSampleProvider(new Constant(1f, AudioFormat.Mono), gain);
        ReadAll(provider, 2000);
        gain.LinearGain = 0.1f;
        var after = ReadAll(provider, 20000);
        float largestStep = 0;
        float prev = 1f;
        foreach (var v in after) { largestStep = Math.Max(largestStep, Math.Abs(v - prev)); prev = v; }
        Assert.True(largestStep < 0.1f, $"largest step was {largestStep}");
        Assert.Equal(0.1f, after[^1], 0.01f);
    }
}
