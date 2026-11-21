using NAudio.Wave;

namespace NightPursuit.Core.Audio;

/// <summary>Endless mono white noise in [-1, 1) that repeats exactly for the same seed.</summary>
public sealed class SeededNoise(int seed) : ISampleProvider
{
    private readonly Random _random = new(seed);

    public WaveFormat WaveFormat => AudioFormat.Mono;

    public int Read(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++) buffer[i] = (float)(_random.NextDouble() * 2 - 1);
        return buffer.Length;
    }
}
