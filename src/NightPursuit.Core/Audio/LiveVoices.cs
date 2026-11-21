using NAudio.Dsp;
using NAudio.Wave;

namespace NightPursuit.Core.Audio;

/// <summary>
/// The continuous sounds: engine, off-road rumble, tyre skid, police siren and missile whine, as one stereo
/// 44,100 Hz provider for the effects bus. The game thread calls <see cref="Set"/>; the audio thread calls
/// <see cref="Read"/>, which always fills the whole buffer. Each voice's level moves over 30 ms, so a voice
/// reaching 0 fades out and nothing clicks. Filter cutoffs are retuned every 32 samples, keeping filter history.
/// </summary>
public sealed class LiveVoices : ISampleProvider
{
    private const int Chunk = 32;
    private const double FadeSeconds = 0.03;
    private const float FilterQ = 0.7071f;

    private LiveState _state = LiveState.Silent;
    private readonly float[] _mono = new float[Chunk];
    private readonly EngineVoice _engine = new();
    private readonly RumbleVoice _rumble = new();
    private readonly SkidVoice _skid = new();
    private readonly SirenVoice _siren = new();
    private readonly WhineVoice _whine = new();

    public WaveFormat WaveFormat => AudioFormat.Stereo;

    /// <summary>Hands over the new state from the game thread. The voices move to it over 30 ms.</summary>
    public void Set(LiveState s) => Volatile.Write(ref _state, s);

    public int Read(Span<float> buffer)
    {
        var s = Volatile.Read(ref _state);
        _engine.Apply(s);
        _rumble.Apply(s);
        _skid.Apply(s);
        _siren.Apply(s);
        _whine.Apply(s);

        int frames = buffer.Length / 2;
        for (int f = 0; f < frames; f += Chunk)
        {
            int n = Math.Min(Chunk, frames - f);
            var outSpan = buffer.Slice(2 * f, 2 * n);
            outSpan.Clear();
            var mono = _mono.AsSpan(0, n);
            if (_engine.Render(mono)) AddMono(outSpan, mono, 1f, 1f);
            if (_rumble.Render(mono)) AddMono(outSpan, mono, 1f, 1f);
            if (_skid.Render(mono)) AddMono(outSpan, mono, 1f, 1f);
            if (_whine.Render(mono)) AddMono(outSpan, mono, 1f, 1f);
            if (_siren.Render(mono)) AddMono(outSpan, mono, _siren.Left, _siren.Right);
        }
        if (buffer.Length % 2 == 1) buffer[^1] = 0;
        return buffer.Length;
    }

    private static void AddMono(Span<float> stereo, ReadOnlySpan<float> mono, float left, float right)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            stereo[2 * i] += mono[i] * left;
            stereo[2 * i + 1] += mono[i] * right;
        }
    }

    /// <summary>Moves a smoothed value on by <paramref name="n"/> samples and returns where it is.</summary>
    private static double Step(Smoothed s, int n)
    {
        double v = 0;
        for (int i = 0; i < n; i++) v = s.Next();
        return v;
    }

    private static double Unit(double v) => Math.Clamp(v, 0.0, 1.0);

    /// <summary>A voice's level: moves over 30 ms, and says when it is fully off so the voice can be skipped.</summary>
    private sealed class Fader
    {
        private readonly Smoothed _level = new(FadeSeconds);
        private double _last;

        public double Target { set => _level.Target = value; }

        public bool Idle => _level.Target == 0 && _last == 0;

        /// <summary>Multiplies each sample by the level times <paramref name="scale"/>.</summary>
        public void Apply(Span<float> x, double scale)
        {
            for (int i = 0; i < x.Length; i++)
            {
                _last = _level.Next();
                x[i] *= (float)(_last * scale);
            }
        }
    }

    /// <summary>Two saws detuned by 6 cents plus a sine an octave down, through a throttle-controlled low-pass.</summary>
    private sealed class EngineVoice
    {
        private const double Detune = 0.0034717; // 2^(6/1200) - 1
        private const double Peak = 0.0565;      // puts the engine peak at about -18 dBFS (measured over 10 s)

        private readonly GlideOscillator _sawA = new() { Wave = OscillatorWave.Saw };
        private readonly GlideOscillator _sawB = new() { Wave = OscillatorWave.Saw };
        private readonly GlideOscillator _sub = new() { Wave = OscillatorWave.Sine };
        private readonly BiQuadFilter _lowPass = BiQuadFilter.LowPassFilter(AudioFormat.SampleRate, 300, FilterQ);
        private readonly Fader _fader = new();
        private readonly Smoothed _speed = new(FadeSeconds);
        private readonly Smoothed _throttle = new(FadeSeconds);
        private readonly Smoothed _wobbleDepth = new(FadeSeconds);
        private readonly float[] _b = new float[Chunk];
        private readonly float[] _c = new float[Chunk];
        private double _wobblePhase;

        public void Apply(LiveState s)
        {
            _fader.Target = s.EngineOn ? 1 : 0;
            _speed.Target = Unit(s.Speed01);
            _throttle.Target = Unit(s.Throttle01);
            _wobbleDepth.Target = s.Punctured ? 1 : 0;
        }

        public bool Render(Span<float> mono)
        {
            if (_fader.Idle) return false;
            int n = mono.Length;
            double wobble = 1 + 0.04 * Step(_wobbleDepth, n) * Math.Sin(2 * Math.PI * _wobblePhase);
            _wobblePhase += 6.0 * n / AudioFormat.SampleRate;
            _wobblePhase -= Math.Floor(_wobblePhase);
            double f = (38 + (110 - 38) * Step(_speed, n)) * wobble;
            _sawA.Frequency = f * (1 + Detune);
            _sawB.Frequency = f / (1 + Detune);
            _sub.Frequency = f / 2;
            var b = _b.AsSpan(0, n);
            var c = _c.AsSpan(0, n);
            _sawA.Read(mono);
            _sawB.Read(b);
            _sub.Read(c);
            for (int i = 0; i < n; i++) mono[i] += b[i] + 0.12f * c[i];
            _lowPass.UpdateLowPassFilter(AudioFormat.SampleRate, (float)(300 + 1500 * Step(_throttle, n)), FilterQ);
            _lowPass.Transform(mono, mono);
            _fader.Apply(mono, Peak);
            return true;
        }
    }

    /// <summary>Noise low-passed at 500 Hz.</summary>
    private sealed class RumbleVoice
    {
        private const double Peak = 0.268;  // puts the filtered noise peak at about -20 dBFS (measured over 10 s)

        private readonly SeededNoise _noise = new(1);
        private readonly BiQuadFilter _lowPass = BiQuadFilter.LowPassFilter(AudioFormat.SampleRate, 500, FilterQ);
        private readonly Fader _fader = new();

        public void Apply(LiveState s) => _fader.Target = Unit(s.OffRoad01);

        public bool Render(Span<float> mono)
        {
            if (_fader.Idle) return false;
            _noise.Read(mono);
            _lowPass.Transform(mono, mono);
            _fader.Apply(mono, Peak);
            return true;
        }
    }

    /// <summary>Noise band-passed around 2.5 kHz, with the centre wobbling slightly.</summary>
    private sealed class SkidVoice
    {
        private const double Peak = 0.131;  // puts the filtered noise peak at about -22 dBFS (measured over 10 s)

        private readonly SeededNoise _noise = new(2);
        private readonly BiQuadFilter _bandPass = BiQuadFilter.BandPassFilterConstantPeakGain(AudioFormat.SampleRate, 2500, 3f);
        private readonly Fader _fader = new();
        private double _phase;

        public void Apply(LiveState s) => _fader.Target = Unit(s.Skid01);

        public bool Render(Span<float> mono)
        {
            if (_fader.Idle) return false;
            _phase += 9.0 * mono.Length / AudioFormat.SampleRate;
            _phase -= Math.Floor(_phase);
            float centre = (float)(2500 * (1 + 0.05 * Math.Sin(2 * Math.PI * _phase)));
            _bandPass.UpdateBandPassFilter(AudioFormat.SampleRate, centre, 3f);
            _noise.Read(mono);
            _bandPass.Transform(mono, mono);
            _fader.Apply(mono, Peak);
            return true;
        }
    }

    /// <summary>Square tones at 650 and 900 Hz, switching every 0.45 s, through a 2.5 kHz low-pass, panned.</summary>
    private sealed class SirenVoice
    {
        private const double Peak = 0.0815; // puts the peak at about -20 dBFS (the low-pass overshoots the square by about 1.8 dB)
        private const int SwitchSamples = (int)(0.45 * AudioFormat.SampleRate);

        private readonly GlideOscillator _osc = new() { Wave = OscillatorWave.Square };
        // two biquads in series make a 4th-order Butterworth low-pass at 2.5 kHz
        private readonly BiQuadFilter _lowPassA = BiQuadFilter.LowPassFilter(AudioFormat.SampleRate, 2500, 0.5412f);
        private readonly BiQuadFilter _lowPassB = BiQuadFilter.LowPassFilter(AudioFormat.SampleRate, 2500, 1.3066f);
        private readonly Fader _fader = new();
        private readonly Smoothed _pan = new(FadeSeconds);
        private int _sinceSwitch;
        private bool _high;

        public float Left { get; private set; } = 1;
        public float Right { get; private set; } = 1;

        public void Apply(LiveState s)
        {
            _fader.Target = Unit(s.Siren01);
            _pan.Target = Math.Clamp(s.SirenPan, -1.0, 1.0);
        }

        public bool Render(Span<float> mono)
        {
            if (_fader.Idle) return false;
            int n = mono.Length;
            _sinceSwitch += n;
            if (_sinceSwitch >= SwitchSamples)
            {
                _sinceSwitch -= SwitchSamples;
                _high = !_high;
            }
            _osc.Frequency = _high ? 900 : 650;
            _osc.Read(mono);
            _lowPassA.Transform(mono, mono);
            _lowPassB.Transform(mono, mono);
            _fader.Apply(mono, Peak);
            double pan = Step(_pan, n);
            Left = (float)Math.Min(1.0, 1 - pan);
            Right = (float)Math.Min(1.0, 1 + pan);
            return true;
        }
    }

    /// <summary>A sine that rises from 900 Hz to 2,200 Hz as the missile closes in.</summary>
    private sealed class WhineVoice
    {
        private const double Peak = 0.0631; // -24 dBFS

        private readonly GlideOscillator _osc = new() { Wave = OscillatorWave.Sine };
        private readonly Fader _fader = new();
        private readonly Smoothed _closeness = new(FadeSeconds);

        public void Apply(LiveState s)
        {
            _fader.Target = Unit(s.Whine01);
            _closeness.Target = Unit(s.WhineCloseness01);
        }

        public bool Render(Span<float> mono)
        {
            if (_fader.Idle) return false;
            _osc.Frequency = 900 + 1300 * Step(_closeness, mono.Length);
            _osc.Read(mono);
            _fader.Apply(mono, Peak);
            return true;
        }
    }
}
