using NAudio.Wave;

namespace NightPursuit.Core.Audio;

/// <summary>The shapes a <see cref="GlideOscillator"/> can make.</summary>
public enum OscillatorWave { Sine, Saw, Square, Triangle }

/// <summary>
/// An endless mono oscillator with a phase accumulator, so changing <see cref="Frequency"/> bends the pitch
/// without a jump. NAudio's SignalGenerator jumps when its frequency changes, so the live voices use this.
/// Set <see cref="Frequency"/> and <see cref="Wave"/> from any thread; read on the audio thread.
/// </summary>
public sealed class GlideOscillator : ISampleProvider
{
    private double _frequency = 440;
    private double _phase; // 0 to 1

    public WaveFormat WaveFormat => AudioFormat.Mono;

    public OscillatorWave Wave { get; set; } = OscillatorWave.Sine;

    /// <summary>Pitch in Hz.</summary>
    public double Frequency
    {
        get => Volatile.Read(ref _frequency);
        set => Volatile.Write(ref _frequency, value);
    }

    public int Read(Span<float> buffer)
    {
        double step = Frequency / AudioFormat.SampleRate;
        var wave = Wave;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = wave switch
            {
                OscillatorWave.Saw => (float)(2 * _phase - 1),
                OscillatorWave.Square => _phase < 0.5 ? 1f : -1f,
                OscillatorWave.Triangle => (float)(_phase < 0.5 ? 4 * _phase - 1 : 3 - 4 * _phase),
                _ => (float)Math.Sin(2 * Math.PI * _phase),
            };
            _phase += step;
            if (_phase >= 1) _phase -= Math.Floor(_phase);
        }
        return buffer.Length;
    }
}
