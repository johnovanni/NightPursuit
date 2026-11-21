using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NightPursuit.Core.Audio;

/// <summary>One playing effect: the cue samples, then its fixed volume, then its pan.</summary>
internal sealed class CueVoice
{
    private readonly CueSource _source;

    public CueVoice(float[] samples, double volume, double pan)
    {
        _source = new CueSource(samples);
        Volume = volume;
        var gain = new VolumeSampleProvider(_source) { Volume = (float)volume };
        Output = new PanningSampleProvider(gain) { Pan = (float)pan };
    }

    /// <summary>The stereo end of the voice, which is what goes into the mixer.</summary>
    public ISampleProvider Output { get; }

    public double Volume { get; }

    /// <summary>How much this voice still matters: its volume times the share of the sound left to play.</summary>
    public double Rank => Volume * _source.RemainingShare;

    /// <summary>A mono provider over a cached sample array. Plays it once, then reports the end.</summary>
    private sealed class CueSource(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat => AudioFormat.Mono;

        public double RemainingShare => (samples.Length - Volatile.Read(ref _position)) / (double)samples.Length;

        public int Read(Span<float> buffer)
        {
            int pos = _position;
            int n = Math.Min(buffer.Length, samples.Length - pos);
            samples.AsSpan(pos, n).CopyTo(buffer);
            Volatile.Write(ref _position, pos + n);
            return n;
        }
    }
}

/// <summary>
/// Keeps at most <see cref="MaxVoices"/> one-off voices playing on a mixer. When a new one would be the 25th, the
/// quietest of all of them (the lowest volume times the share still to play) is dropped, the newcomer included.
/// Called from the game thread; the mixer reads on the audio thread, and the lock is held only for list changes.
/// </summary>
internal sealed class VoiceLimit
{
    public const int MaxVoices = 24;

    private readonly MixingSampleProvider _mixer;
    private readonly List<CueVoice> _voices = new(MaxVoices + 1);
    private readonly Lock _lock = new();

    public VoiceLimit(MixingSampleProvider mixer)
    {
        _mixer = mixer;
        _mixer.MixerInputEnded += OnInputEnded;
    }

    /// <summary>How many one-off voices are playing now.</summary>
    public int Count
    {
        get { lock (_lock) return _voices.Count; }
    }

    /// <summary>Starts the voice, dropping the quietest one if there are already 24.</summary>
    public void Add(CueVoice voice)
    {
        CueVoice? dropped = null;
        lock (_lock)
        {
            if (_voices.Count >= MaxVoices)
            {
                int quietest = -1;
                double lowest = voice.Rank;
                for (int i = 0; i < _voices.Count; i++)
                {
                    double rank = _voices[i].Rank;
                    if (rank < lowest) { lowest = rank; quietest = i; }
                }
                if (quietest < 0) return; // the newcomer is the quietest
                dropped = _voices[quietest];
                _voices.RemoveAt(quietest);
            }
            _voices.Add(voice);
        }
        // The mixer's own lock is taken outside ours, so the audio thread (mixer lock, then ours) can't deadlock.
        if (dropped != null) _mixer.RemoveMixerInput(dropped.Output);
        _mixer.AddMixerInput(voice.Output);
    }

    private void OnInputEnded(object? sender, SampleProviderEventArgs e)
    {
        lock (_lock)
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (ReferenceEquals(_voices[i].Output, e.SampleProvider))
                {
                    _voices.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
