using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NightPursuit.Core.Audio;

/// <summary>
/// The music bus: the current track and, while they fade out, the ones before it, each in its own fade, mixed
/// together. Also holds the pause level (1.0, or 0.4 while paused), which ramps over 0.3 s. Always fills the buffer.
/// </summary>
public sealed class MusicChannel : ISampleProvider
{
    private const double LevelRampSeconds = 0.3;
    private const int MaxFading = 4; // more than this and the oldest fading track is cut, so nothing piles up

    private readonly MixingSampleProvider _mixer = new(AudioFormat.Stereo) { ReadFully = true };
    private readonly Smoothed _level = new(LevelRampSeconds, 1.0);
    private readonly List<FadingTrack> _fading = new();
    private readonly Lock _lock = new();
    private FadingTrack? _current;

    public WaveFormat WaveFormat => AudioFormat.Stereo;

    /// <summary>The level the music ramps to: 1.0 normally, 0.4 while paused.</summary>
    public double Level
    {
        get => _level.Target;
        set => _level.Target = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// Fades the current track out and <paramref name="next"/> in over <paramref name="fadeSeconds"/>, each from
    /// the level it has reached. Null fades to silence. The track must be stereo, 44,100 Hz, float.
    /// </summary>
    public void Set(ISampleProvider? next, double fadeSeconds)
    {
        if (next != null && !next.WaveFormat.Equals(AudioFormat.Stereo))
            throw new ArgumentException("Music must be 44,100 Hz stereo 32-bit float.", nameof(next));
        double seconds = Math.Max(0.001, fadeSeconds);

        var fresh = next == null ? null : new FadingTrack(next);
        FadingTrack? old;
        List<FadingTrack>? cut = null;
        lock (_lock)
        {
            old = _current;
            _current = fresh;
            if (old != null) _fading.Add(old);
            while (_fading.Count > MaxFading)
            {
                (cut ??= new()).Add(_fading[0]);
                _fading.RemoveAt(0);
            }
        }
        old?.Fade(0, seconds);
        if (cut != null) foreach (var t in cut) _mixer.RemoveMixerInput(t);
        if (fresh != null)
        {
            fresh.Fade(1, seconds);
            _mixer.AddMixerInput(fresh);
        }
    }

    public int Read(Span<float> buffer)
    {
        // Finished fade-outs are removed here, never from inside the mixer's own read loop.
        lock (_lock)
        {
            for (int i = _fading.Count - 1; i >= 0; i--)
            {
                if (!_fading[i].Finished) continue;
                _mixer.RemoveMixerInput(_fading[i]);
                _fading.RemoveAt(i);
            }
        }

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
