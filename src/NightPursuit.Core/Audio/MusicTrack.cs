using NAudio.Wave;

namespace NightPursuit.Core.Audio;

/// <summary>
/// One music track with its own fade. A fade always starts from the level the track has reached, so changing
/// your mind mid-fade never jumps. <see cref="Fade"/> is for the game thread; <see cref="Read"/> for the audio thread.
/// </summary>
internal sealed class MusicTrack(ISampleProvider source) : ISampleProvider
{
    private readonly Lock _lock = new();
    private bool _requested;
    private double _requestedTarget;
    private int _requestedSamples = 1;
    private double _gain; // starts silent
    private double _target;
    private double _step;
    private volatile bool _finished;

    public WaveFormat WaveFormat => AudioFormat.Stereo;

    /// <summary>True once a fade-out has reached silence.</summary>
    public bool Finished => _finished;

    /// <summary>Starts a straight-line fade to <paramref name="target"/> (0 or 1) over the given time.</summary>
    public void Fade(double target, double seconds)
    {
        lock (_lock)
        {
            _requested = true;
            _requestedTarget = target;
            _requestedSamples = Math.Max(1, (int)Math.Round(seconds * AudioFormat.SampleRate));
        }
    }

    public int Read(Span<float> buffer)
    {
        lock (_lock)
        {
            if (_requested)
            {
                _requested = false;
                _target = _requestedTarget;
                _step = (_target - _gain) / _requestedSamples;
                _finished = false;
            }
        }
        int n = source.Read(buffer);
        for (int i = 0; i < n; i += 2)
        {
            if (_gain != _target)
            {
                _gain += _step;
                if ((_step > 0 && _gain >= _target) || (_step < 0 && _gain <= _target) || _step == 0) _gain = _target;
                if (_gain == 0 && _target == 0) _finished = true;
            }
            float g = (float)_gain;
            buffer[i] *= g;
            if (i + 1 < n) buffer[i + 1] *= g;
        }
        if (_target == 0 && _gain == 0) _finished = true;
        return n;
    }
}
