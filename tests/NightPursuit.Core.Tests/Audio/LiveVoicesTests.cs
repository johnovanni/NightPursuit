using NAudio.Dsp;
using NAudio.Wave;
using NightPursuit.Core.Audio;

namespace NightPursuit.Core.Tests.Audio;

/// <summary>Behaviour of the live voices: engine, off-road rumble, skid, siren and missile whine.</summary>
public class LiveVoicesTests
{
    private const int Rate = AudioFormat.SampleRate;

    /// <summary>Reads stereo frames in 1,024-sample pieces and returns the interleaved samples.</summary>
    private static float[] Read(ISampleProvider p, double seconds)
    {
        var buffer = new float[(int)(seconds * Rate) * 2];
        int done = 0;
        while (done < buffer.Length)
        {
            int n = Math.Min(1024, buffer.Length - done);
            Assert.Equal(n, p.Read(buffer.AsSpan(done, n)));
            done += n;
        }
        return buffer;
    }

    private static float[] Channel(float[] b, int ch, double fromSeconds = 0)
    {
        int from = (int)(fromSeconds * Rate);
        var c = new float[b.Length / 2 - from];
        for (int i = 0; i < c.Length; i++) c[i] = b[2 * (from + i) + ch];
        return c;
    }

    /// <summary>Plays a state, lets it settle, and returns the left channel of the next stretch.</summary>
    private static float[] Play(LiveState s, double settle = 0.3, double seconds = 1.0)
    {
        var v = new LiveVoices();
        v.Set(s);
        return Channel(Read(v, settle + seconds), 0, settle);
    }

    /// <summary>
    /// Zero crossings per second. With <paramref name="hysteresis"/> a crossing only counts once the signal has
    /// gone a quarter of its peak past zero, so ripple around zero (the engine's two detuned saws) is not counted.
    /// </summary>
    private static double CrossingsPerSecond(float[] x, bool hysteresis = false)
    {
        float h = hysteresis ? 0.25f * Peak(x) : 0f;
        int n = 0;
        int side = 0; // -1 below, +1 above
        foreach (float s in x)
        {
            int now = s > h ? 1 : s < -h ? -1 : 0;
            if (now == 0) continue;
            if (side != 0 && now != side) n++;
            side = now;
        }
        return n / (x.Length / (double)Rate);
    }

    private static double Rms(float[] x) => Math.Sqrt(x.Sum(v => (double)v * v) / x.Length);

    private static float MaxStep(float[] x)
    {
        float max = 0;
        for (int i = 1; i < x.Length; i++) max = Math.Max(max, Math.Abs(x[i] - x[i - 1]));
        return max;
    }

    private static float Peak(float[] x) => x.Max(Math.Abs);

    private static double Db(double linear) => 20 * Math.Log10(linear);

    private static readonly LiveState Everything = new()
    {
        EngineOn = true, Speed01 = 1, Throttle01 = 1, Punctured = true, OffRoad01 = 1, Skid01 = 1,
        Siren01 = 1, SirenPan = 1, Whine01 = 1, WhineCloseness01 = 1,
    };

    [Fact]
    public void Silent_state_adds_nothing()
    {
        var v = new LiveVoices();
        v.Set(LiveState.Silent);
        Assert.All(Read(v, 0.5), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Engine_pitch_rises_with_speed()
    {
        var slow = Play(new LiveState { EngineOn = true, Speed01 = 0, Throttle01 = 0 });
        var fast = Play(new LiveState { EngineOn = true, Speed01 = 1, Throttle01 = 0 });
        Assert.True(CrossingsPerSecond(fast, true) > 2.5 * CrossingsPerSecond(slow, true),
            $"fast {CrossingsPerSecond(fast, true):F0}/s, slow {CrossingsPerSecond(slow, true):F0}/s");
    }

    [Fact]
    public void Throttle_brightens_the_engine()
    {
        double Bright(double throttle)
        {
            var x = Play(new LiveState { EngineOn = true, Speed01 = 0.5, Throttle01 = throttle });
            var high = BiQuadFilter.HighPassFilter(Rate, 600, 0.707f);
            return Rms(x.Select(s => high.Transform(s)).ToArray());
        }
        double open = Bright(1), closed = Bright(0);
        Assert.True(open > 2 * closed, $"throttle 1: {open:E2}, throttle 0: {closed:E2}");
    }

    [Fact]
    public void Switching_the_engine_off_fades_without_a_click()
    {
        var v = new LiveVoices();
        v.Set(new LiveState { EngineOn = true, Speed01 = 0.7, Throttle01 = 1 });
        Read(v, 0.5);
        v.Set(LiveState.Silent);
        var tail = Channel(Read(v, 0.2), 0);
        Assert.True(MaxStep(tail) <= 0.05f, $"largest step {MaxStep(tail)}");
        // a fade, not a cut: still sounding 5 to 15 ms later, and silent once 35 ms have passed
        Assert.True(Peak(tail[(int)(0.005 * Rate)..(int)(0.015 * Rate)]) > 0.001f);
        Assert.All(tail[(int)(0.035 * Rate)..], s => Assert.Equal(0f, s));
    }

    [Fact]
    public void A_jump_in_every_parameter_causes_no_click()
    {
        var v = new LiveVoices();
        var off = LiveState.Silent;
        var half = new LiveState
        {
            EngineOn = true, Speed01 = 0.3, Throttle01 = 0.2, OffRoad01 = 0.4, Skid01 = 0.5,
            Siren01 = 0.6, SirenPan = -1, Whine01 = 0.7, WhineCloseness01 = 0.1,
        };
        var parts = new List<float[]>();
        foreach (var s in new[] { off, Everything, half, off, Everything, off, half, Everything })
        {
            v.Set(s);
            parts.Add(Read(v, 0.17));
        }
        var all = parts.SelectMany(p => p).ToArray();
        // the waveforms themselves have steep parts (the siren's edges, the saw), so the limit is the steepest
        // step of everything playing steadily; a jump with no fade would step by the whole level at once
        var steady = Read(Settled(Everything), 2.0);
        for (int ch = 0; ch < 2; ch++)
        {
            float steadyStep = MaxStep(Channel(steady, ch));
            var x = Channel(all, ch);
            Assert.True(MaxStep(x) <= steadyStep * 1.2f, $"channel {ch}: largest step {MaxStep(x)}, steady {steadyStep}");
            Assert.True(Peak(x) > 0.05f); // it really was playing
        }
    }

    private static LiveVoices Settled(LiveState s)
    {
        var v = new LiveVoices();
        v.Set(s);
        Read(v, 0.3);
        return v;
    }

    [Fact]
    public void Siren_alternates_between_two_tones()
    {
        var v = new LiveVoices();
        v.Set(new LiveState { Siren01 = 1 });
        var x = Channel(Read(v, 1.0), 0);
        double Crossings(double from, double to) => CrossingsPerSecond(x[(int)(from * Rate)..(int)(to * Rate)]);
        Assert.InRange(Crossings(0.08, 0.40), 2 * 650 * 0.95, 2 * 650 * 1.05);
        Assert.InRange(Crossings(0.53, 0.85), 2 * 900 * 0.95, 2 * 900 * 1.05);
    }

    [Fact]
    public void Siren_pan_right_is_louder_on_the_right()
    {
        var v = new LiveVoices();
        v.Set(new LiveState { Siren01 = 1, SirenPan = 1 });
        var right = Read(v, 0.6);
        Assert.True(Rms(Channel(right, 1, 0.1)) > 4 * Rms(Channel(right, 0, 0.1)));
        var w = new LiveVoices();
        w.Set(new LiveState { Siren01 = 1, SirenPan = -1 });
        var left = Read(w, 0.6);
        Assert.True(Rms(Channel(left, 0, 0.1)) > 4 * Rms(Channel(left, 1, 0.1)));
    }

    [Fact]
    public void Engine_peak_stays_near_minus_18_dBFS()
    {
        var x = Play(new LiveState { EngineOn = true, Speed01 = 0.5, Throttle01 = 1 }, 0.3, 8.0);
        Assert.InRange(Db(Peak(x)), -19.5, -16.5);
    }

    [Fact]
    public void Each_voice_sounds_at_its_own_level()
    {
        // full-level peaks: rumble -20, skid -22, siren -20, whine -24 dBFS
        (string Name, LiveState State, double Max)[] cases =
        [
            ("rumble", new LiveState { OffRoad01 = 1 }, -20),
            ("skid", new LiveState { Skid01 = 1 }, -22),
            ("whine", new LiveState { Whine01 = 1, WhineCloseness01 = 0.5 }, -24),
            ("siren", new LiveState { Siren01 = 1, SirenPan = 0 }, -20),
        ];
        foreach (var (name, state, max) in cases)
        {
            var x = Play(state, 0.3, 8.0);
            Assert.InRange(Db(Peak(x)), max - 1.5, max + 0.5);
        }
    }

    [Fact]
    public void Whine_pitch_rises_with_closeness()
    {
        var far = Play(new LiveState { Whine01 = 1, WhineCloseness01 = 0 });
        var near = Play(new LiveState { Whine01 = 1, WhineCloseness01 = 1 });
        Assert.InRange(CrossingsPerSecond(far), 2 * 900 * 0.97, 2 * 900 * 1.03);
        Assert.InRange(CrossingsPerSecond(near), 2 * 2200 * 0.97, 2 * 2200 * 1.03);
    }

    [Fact]
    public void Punctured_engine_wobbles_in_pitch()
    {
        // crossing counts per 0.1 s window vary more than for a healthy engine
        double Spread(bool punctured)
        {
            var x = Play(new LiveState { EngineOn = true, Speed01 = 1, Throttle01 = 0, Punctured = punctured });
            var counts = Enumerable.Range(0, 10)
                .Select(i => CrossingsPerSecond(x[(i * Rate / 10)..((i + 1) * Rate / 10)])).ToArray();
            return counts.Max() - counts.Min();
        }
        Assert.True(Spread(true) > Spread(false) + 10);
    }

    [Fact]
    public void Mixer_carries_the_live_voices_on_the_effects_bus()
    {
        var bank = SoundBank.Load(Path.Combine(Path.GetTempPath(), "np-no-sounds-" + Guid.NewGuid().ToString("N")), out _);
        var mixer = new AudioMixer(bank) { SoundOn = true, EffectsVolume = 10 };
        Read(mixer, 0.1);
        Assert.All(Read(mixer, 0.1), s => Assert.Equal(0f, s));
        mixer.Live.Set(new LiveState { EngineOn = true, Speed01 = 0.5, Throttle01 = 1 });
        Assert.True(Peak(Channel(Read(mixer, 0.5), 0)) > 0.05f);
        mixer.EffectsVolume = 0;
        Read(mixer, 0.1);
        Assert.All(Read(mixer, 0.1), s => Assert.Equal(0f, s));
    }
}
