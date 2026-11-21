using NAudio.Dsp;
using NAudio.Effects;
using NAudio.Wave;
using NightPursuit.Core.Audio;

namespace SynthSounds;

/// <summary>Which band a <see cref="Dsp.Sweep"/> filter passes.</summary>
internal enum FilterKind { LowPass, HighPass, BandPass }

/// <summary>
/// Small building blocks for the recipes. Every sound is rendered offline into a mono float array, so the
/// helpers take and return arrays. NAudio does the real work (oscillators, filters, envelopes, effects);
/// the helpers only join those blocks together.
/// </summary>
internal static class Dsp
{
    public const int Rate = AudioFormat.SampleRate;

    public static int Count(double seconds) => (int)Math.Round(seconds * Rate);

    public static double Db(double linear) => 20 * Math.Log10(Math.Max(linear, 1e-9));

    /// <summary>An exponential glide from <paramref name="from"/> to <paramref name="to"/> over <paramref name="over"/> seconds, then held.</summary>
    public static double Glide(double t, double from, double to, double over) =>
        from * Math.Pow(to / from, Math.Clamp(t / over, 0, 1));

    // ---- sources ----

    /// <summary>An oscillator whose pitch (Hz) is given at each moment; the phase never jumps.</summary>
    public static float[] Osc(OscillatorWave wave, double seconds, Func<double, double> hz)
    {
        var osc = new GlideOscillator { Wave = wave };
        var result = new float[Count(seconds)];
        var one = new float[1];
        for (int i = 0; i < result.Length; i++)
        {
            osc.Frequency = hz(i / (double)Rate);
            osc.Read(one);
            result[i] = one[0];
        }
        return result;
    }

    public static float[] Osc(OscillatorWave wave, double seconds, double hz) => Osc(wave, seconds, _ => hz);

    public static float[] Noise(int seed, double seconds)
    {
        var result = new float[Count(seconds)];
        new SeededNoise(seed).Read(result);
        return result;
    }

    // ---- envelopes ----

    /// <summary>A plain exponential decay, exp(-t / tau), for percussive hits.</summary>
    public static float[] Decay(double seconds, double tau)
    {
        var result = new float[Count(seconds)];
        for (int i = 0; i < result.Length; i++) result[i] = (float)Math.Exp(-i / (double)Rate / tau);
        return result;
    }

    /// <summary>An attack, decay, sustain, release shape from NAudio's EnvelopeGenerator; the gate closes at <paramref name="gateSeconds"/>.</summary>
    public static float[] Adsr(double seconds, double attack, double decay, double sustain, double release, double gateSeconds)
    {
        var env = new EnvelopeGenerator
        {
            AttackRate = (float)(attack * Rate),
            DecayRate = (float)(decay * Rate),
            ReleaseRate = (float)(release * Rate),
            SustainLevel = (float)sustain,
        };
        env.Gate(true);
        var result = new float[Count(seconds)];
        int gateOff = Count(gateSeconds);
        for (int i = 0; i < result.Length; i++)
        {
            if (i == gateOff) env.Gate(false);
            result[i] = env.Process();
        }
        return result;
    }

    // ---- arithmetic ----

    /// <summary>Multiplies two signals sample by sample (the shorter one sets the length).</summary>
    public static float[] Mul(float[] a, float[] b)
    {
        var result = new float[Math.Min(a.Length, b.Length)];
        for (int i = 0; i < result.Length; i++) result[i] = a[i] * b[i];
        return result;
    }

    public static float[] Gain(float[] a, double g)
    {
        var result = new float[a.Length];
        for (int i = 0; i < a.Length; i++) result[i] = (float)(a[i] * g);
        return result;
    }

    /// <summary>Adds signals together; the result is as long as the longest.</summary>
    public static float[] Mix(params float[][] parts)
    {
        var result = new float[parts.Max(p => p.Length)];
        foreach (var p in parts)
            for (int i = 0; i < p.Length; i++) result[i] += p[i];
        return result;
    }

    /// <summary>Delays a signal by <paramref name="atSeconds"/> of silence.</summary>
    public static float[] Place(float[] a, double atSeconds)
    {
        var result = new float[Count(atSeconds) + a.Length];
        a.CopyTo(result, Count(atSeconds));
        return result;
    }

    /// <summary>Cuts or pads a signal with silence to exactly this length.</summary>
    public static float[] Fit(float[] a, double seconds)
    {
        var result = new float[Count(seconds)];
        Array.Copy(a, result, Math.Min(a.Length, result.Length));
        return result;
    }

    /// <summary>Stays at full level until <paramref name="holdSeconds"/>, then falls to nothing: the cut-off of a gated reverb.</summary>
    public static float[] GateOff(float[] a, double holdSeconds, double cutSeconds)
    {
        var result = new float[a.Length];
        int hold = Count(holdSeconds), cut = Math.Max(1, Count(cutSeconds));
        for (int i = 0; i < a.Length; i++)
            result[i] = i < hold ? a[i] : i >= hold + cut ? 0 : a[i] * (1f - (i - hold) / (float)cut);
        return result;
    }

    // ---- filters ----

    public static float[] Lp(float[] x, double hz, double q = 0.707) =>
        Run(x, BiQuadFilter.LowPassFilter(Rate, (float)hz, (float)q));

    public static float[] Hp(float[] x, double hz, double q = 0.707) =>
        Run(x, BiQuadFilter.HighPassFilter(Rate, (float)hz, (float)q));

    public static float[] Bp(float[] x, double hz, double q = 1.5) =>
        Run(x, BiQuadFilter.BandPassFilterConstantPeakGain(Rate, (float)hz, (float)q));

    private static float[] Run(float[] x, BiQuadFilter filter)
    {
        var result = new float[x.Length];
        for (int i = 0; i < x.Length; i++) result[i] = filter.Transform(x[i]);
        return result;
    }

    /// <summary>A filter whose cutoff glides from <paramref name="fromHz"/> to <paramref name="toHz"/> over <paramref name="overSeconds"/>.</summary>
    public static float[] Sweep(float[] x, FilterKind kind, double fromHz, double toHz, double overSeconds, double q = 0.9)
    {
        var filter = kind switch
        {
            FilterKind.LowPass => BiQuadFilter.LowPassFilter(Rate, (float)fromHz, (float)q),
            FilterKind.HighPass => BiQuadFilter.HighPassFilter(Rate, (float)fromHz, (float)q),
            _ => BiQuadFilter.BandPassFilterConstantPeakGain(Rate, (float)fromHz, (float)q),
        };
        var result = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            if (i % 16 == 0)
            {
                float hz = (float)Glide(i / (double)Rate, fromHz, toHz, overSeconds);
                switch (kind)
                {
                    case FilterKind.LowPass: filter.UpdateLowPassFilter(Rate, hz, (float)q); break;
                    case FilterKind.HighPass: filter.UpdateHighPassFilter(Rate, hz, (float)q); break;
                    default: filter.UpdateBandPassFilter(Rate, hz, (float)q); break;
                }
            }
            result[i] = filter.Transform(x[i]);
        }
        return result;
    }

    // ---- NAudio effects ----

    /// <summary>Runs a signal through an NAudio effect, padding with silence so a tail can ring out to <paramref name="totalSeconds"/>.</summary>
    public static float[] Through(float[] x, IAudioEffect effect, double totalSeconds)
    {
        var source = new ArraySource(Fit(x, totalSeconds));
        var provider = new EffectSampleProvider(source, effect);
        var result = new float[source.Length];
        int read = 0;
        while (read < result.Length)
        {
            int n = provider.Read(result.AsSpan(read));
            if (n <= 0) break;
            read += n;
        }
        return result;
    }

    /// <summary>A short room reverb, cut off abruptly after <paramref name="gateSeconds"/>: the 80s gated drum sound.</summary>
    public static float[] GatedReverb(float[] x, double totalSeconds, double gateSeconds, double room = 0.55, double mix = 0.4) =>
        GateOff(Through(x, new ReverbEffect { RoomSize = (float)room, Damping = 0.4f, Mix = (float)mix }, totalSeconds),
            gateSeconds, 0.03);

    public static float[] Hall(float[] x, double totalSeconds, double decaySeconds, double mix) =>
        Through(x, new FdnReverbEffect { DecaySeconds = (float)decaySeconds, Damping = 0.5f, PreDelayMs = 10, Mix = (float)mix }, totalSeconds);

    public static float[] Chorus(float[] x, double mix = 0.5, double depthMs = 3, double rateHz = 0.8) =>
        Through(x, new ChorusEffect { BaseDelayMs = 18, DepthMs = (float)depthMs, RateHz = (float)rateHz, Feedback = 0.1f, Mix = (float)mix }, x.Length / (double)Rate);

    public static float[] Echo(float[] x, double totalSeconds, double delayMs, double feedback, double mix) =>
        Through(x, new DelayEffect { DelayMs = (float)delayMs, Feedback = (float)feedback, Damping = 0.4f, Mix = (float)mix }, totalSeconds);

    /// <summary>Grit: drives the signal into a soft clip.</summary>
    public static float[] Saturate(float[] x, double driveDb) =>
        Through(x, new SaturationEffect { DriveDb = (float)driveDb }, x.Length / (double)Rate);

    // ---- finishing ----

    /// <summary>
    /// Fades the first and last 8 ms with a steep curve (so the first and last 2 ms sit far below -40 dBFS),
    /// then scales the loudest sample to -3 dBFS.
    /// </summary>
    public static float[] Finish(float[] x)
    {
        var y = (float[])x.Clone();
        int fade = Math.Min(Count(0.008), y.Length / 2);
        for (int i = 0; i < fade; i++)
        {
            double k = (i + 1) / (double)fade;
            float g = (float)(k * k * k * k);
            y[i] *= g;
            y[y.Length - 1 - i] *= g;
        }
        float peak = y.Max(Math.Abs);
        float scale = peak > 0 ? (float)Math.Pow(10, -3 / 20.0) / peak : 1;
        for (int i = 0; i < y.Length; i++) y[i] *= scale;
        return y;
    }

    private sealed class ArraySource(float[] data) : ISampleProvider
    {
        private int _pos;
        public int Length => data.Length;
        public WaveFormat WaveFormat => AudioFormat.Mono;
        public int Read(Span<float> buffer)
        {
            int n = Math.Min(buffer.Length, data.Length - _pos);
            data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
    }
}
