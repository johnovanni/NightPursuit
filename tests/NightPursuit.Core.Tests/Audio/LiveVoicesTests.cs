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

    private static double CrossingsPerSecond(float[] x)
    {
        int n = 0;
        for (int i = 1; i < x.Length; i++)
            if ((x[i - 1] < 0) != (x[i] < 0)) n++;
        return n / (x.Length / (double)Rate);
    }

    /// <summary>The size of one frequency in a Hann-windowed one-bin DFT.</summary>
    private static double Magnitude(float[] x, double freq)
    {
        int n = x.Length;
        double re = 0, im = 0, step = 2 * Math.PI * freq / Rate;
        for (int i = 0; i < n; i++)
        {
            double w = x[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
            re += w * Math.Cos(step * i);
            im += w * Math.Sin(step * i);
        }
        return Math.Sqrt(re * re + im * im);
    }

    /// <summary>The strongest frequency between 25 and 200 Hz, scanned in 0.5 Hz steps.</summary>
    private static double Strongest(float[] x)
    {
        double best = 0, bestF = 0;
        for (double f = 25; f <= 200; f += 0.5)
        {
            double mag = Magnitude(x, f);
            if (mag > best) { best = mag; bestF = f; }
        }
        return bestF;
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
        // "the engine pitch" is the saws' fundamental, 38 Hz at speed 0 and 110 Hz at top speed (the sub is an octave below).
        // A short window keeps clear of the two saws' slow beat, which can cancel the fundamental in a 1 s window.
        double slow = Strongest(Play(new LiveState { EngineOn = true, Speed01 = 0, Throttle01 = 0.5 }, 0.1, 0.3));
        double fast = Strongest(Play(new LiveState { EngineOn = true, Speed01 = 1, Throttle01 = 0.5 }, 0.1, 0.3));
        Assert.InRange(slow, 34, 42);
        Assert.InRange(fast, 104, 116);
        Assert.True(fast > 2.5 * slow, $"fast {fast} Hz, slow {slow} Hz");
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
    public static TheoryData<string> Voices => ["engine", "whine", "rumble", "skid"];

    private static LiveState Only(string voice) => voice switch
    {
        "engine" => new LiveState { EngineOn = true, Speed01 = 1, Throttle01 = 1 },
        "whine" => new LiveState { Whine01 = 1, WhineCloseness01 = 1 },
        "rumble" => new LiveState { OffRoad01 = 1 },
        "skid" => new LiveState { Skid01 = 1 },
        _ => new LiveState { Siren01 = 1, SirenPan = 0 },
    };

    private static float[] JumpOnAndOff(LiveState on)
    {
        var v = new LiveVoices();
        var parts = new List<float[]>();
        foreach (var s in new[] { LiveState.Silent, on, LiveState.Silent, on, on, LiveState.Silent })
        {
            v.Set(s);
            parts.Add(Read(v, 0.13));
        }
        return Channel(parts.SelectMany(p => p).ToArray(), 0);
    }

    [Theory]
    [MemberData(nameof(Voices))]
    public void One_voice_jumping_on_and_off_causes_no_click(string voice)
    {
        var x = JumpOnAndOff(Only(voice));
        Assert.True(Peak(x) > 0.05f, "it really was playing");
        // under 0.05, and no steeper than the voice is on its own: an unfaded jump steps by up to its whole level
        float steady = MaxStep(Channel(Read(Settled(Only(voice)), 8.0), 0));
        float limit = Math.Min(0.05f, steady * 1.2f);
        Assert.True(MaxStep(x) <= limit, $"{voice}: largest step {MaxStep(x)}, limit {limit}");
    }

    [Fact]
    public void Siren_jumping_on_and_off_steps_no_more_than_it_does_steadily()
    {
        // the siren's own square edges are steep (more than 0.05 a sample), so its limit is its steady-state step
        float steady = MaxStep(Channel(Read(Settled(Only("siren")), 2.0), 0));
        var x = JumpOnAndOff(Only("siren"));
        Assert.True(MaxStep(x) <= steady * 1.2f, $"largest step {MaxStep(x)}, steady {steady}");
    }

    [Fact]
    public void Bad_numbers_in_the_state_cannot_poison_the_voices()
    {
        var v = new LiveVoices();
        double[] bad = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        foreach (double b in bad)
        {
            v.Set(new LiveState
            {
                EngineOn = true, Speed01 = b, Throttle01 = b, OffRoad01 = b, Skid01 = b,
                Siren01 = b, SirenPan = b, Whine01 = b, WhineCloseness01 = b,
            });
            Assert.All(Read(v, 0.2), s => Assert.True(float.IsFinite(s), "output must stay finite"));
        }
        v.Set(Everything);
        var after = Read(v, 0.5);
        Assert.All(after, s => Assert.True(float.IsFinite(s)));
        Assert.True(Peak(Channel(after, 1, 0.2)) > 0.05f, "normal sound resumes");
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
        // a 6 Hz wobble puts sidebands 6 Hz either side of the 110 Hz fundamental; a healthy engine has none
        double Sidebands(bool punctured)
        {
            var x = Play(new LiveState { EngineOn = true, Speed01 = 1, Throttle01 = 0, Punctured = punctured });
            return (Magnitude(x, 104) + Magnitude(x, 116)) / 2 / Magnitude(x, 110);
        }
        double wobbling = Sidebands(true), healthy = Sidebands(false);
        Assert.True(wobbling > 0.2 && healthy < 0.05, $"punctured {wobbling:F3}, healthy {healthy:F3}");
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
