namespace NightPursuit.Core.Audio;

/// <summary>
/// A value that moves in a straight line to its target over a fixed time, so changing a parameter never clicks.
/// Set <see cref="Target"/> from any thread; call <see cref="Next"/> once per sample on the audio thread.
/// </summary>
public sealed class Smoothed
{
    private readonly int _rampSamples;
    private double _requested;
    private double _target;
    private double _current;
    private double _step;
    private int _remaining;

    public Smoothed(double seconds)
    {
        _rampSamples = Math.Max(1, (int)Math.Round(seconds * AudioFormat.SampleRate));
    }

    /// <summary>Where the value is heading.</summary>
    public double Target
    {
        get => Volatile.Read(ref _requested);
        set => Volatile.Write(ref _requested, value);
    }

    /// <summary>The value for the next sample.</summary>
    public double Next()
    {
        double requested = Volatile.Read(ref _requested);
        if (requested != _target)
        {
            _target = requested;
            _remaining = _rampSamples;
            _step = (_target - _current) / _rampSamples;
        }
        if (_remaining > 0)
        {
            _current += _step;
            if (--_remaining == 0) _current = _target;
        }
        return _current;
    }
}
