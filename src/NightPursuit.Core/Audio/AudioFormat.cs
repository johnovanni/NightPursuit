using NAudio.Wave;

namespace NightPursuit.Core.Audio;

/// <summary>The one sample format the whole mix uses: 44,100 Hz, 32-bit float, interleaved.</summary>
public static class AudioFormat
{
    public const int SampleRate = 44100;

    /// <summary>The loudest any output sample may be: -1 dBFS (0.891), the limiter ceiling and the hard clamp.</summary>
    public const float Ceiling = 0.891f;

    public static WaveFormat Stereo { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public static WaveFormat Mono { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
}
