using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NightPursuit.Core.Audio;

/// <summary>
/// The music bus: the current track and, while it fades out, the one before it, each in a fade, mixed together.
/// Also holds the pause level (1.0, or 0.4 while paused), which ramps over 0.3 s. Always fills the buffer.
/// </summary>
public sealed class MusicChannel : ISampleProvider
{
    private const double LevelRampSeconds = 0.3;

    private readonly MixingSampleProvider _mixer = new(AudioFormat.Stereo) { ReadFully = true };
    private readonly Smoothed _level = new(LevelRampSeconds, 1.0);
    private readonly ConcurrentQueue<ISampleProvider> _finished = new();
    private readonly Lock _lock = new();
    private FadeInOutSampleProvider? _current;

    public WaveFormat WaveFormat => AudioFormat.Stereo;

    /// <summary>The level the music ramps to: 1.0 normally, 0.4 while paused.</summary>
    public double Level
    {
        get => _level.Target;
        set => _level.Target = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// Fades the current track out and <paramref name="next"/> in over <paramref name="fadeSeconds"/>.
    /// Null fades to silence. The track must be stereo, 44,100 Hz, float.
    /// </summary>
    public void Set(ISampleProvider? next, double fadeSeconds)
    {
        if (next != null && !next.WaveFormat.Equals(AudioFormat.Stereo))
            throw new ArgumentException("Music must be 44,100 Hz stereo 32-bit float.", nameof(next));
        double ms = Math.Max(1.0, fadeSeconds * 1000.0);

        FadeInOutSampleProvider? fresh = null;
        if (next != null)
        {
            fresh = new FadeInOutSampleProvider(next, initiallySilent: true);
            fresh.FadeOutComplete += (_, _) => _finished.Enqueue(fresh);
        }
        FadeInOutSampleProvider? old;
        lock (_lock)
        {
            old = _current;
            _current = fresh;
        }
        old?.BeginFadeOut(ms);
        if (fresh != null)
        {
            _mixer.AddMixerInput(fresh);
            fresh.BeginFadeIn(ms);
        }
    }

    public int Read(Span<float> buffer)
    {
        // A finished fade-out is removed here, not inside the mixer's own read, which would disturb its loop.
        while (_finished.TryDequeue(out var done)) _mixer.RemoveMixerInput(done);

        int n = _mixer.Read(buffer);
        if (n < buffer.Length) buffer[n..].Clear();
        for (int i = 0; i < buffer.Length; i += 2)
        {
            float g = (float)_level.Next();
            buffer[i] *= g;
            if (i + 1 < buffer.Length) buffer[i + 1] *= g;
        }
        return buffer.Length;
    }
}
