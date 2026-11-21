using NAudio.Wave;

namespace NightPursuit.Core.Audio;

/// <summary>The safety net after the limiter: no output sample ever goes past the ceiling.</summary>
public sealed class HardClamp(ISampleProvider source, float ceiling = 0.891f) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int n = source.Read(buffer);
        for (int i = 0; i < n; i++) buffer[i] = Math.Clamp(buffer[i], -ceiling, ceiling);
        return n;
    }
}
