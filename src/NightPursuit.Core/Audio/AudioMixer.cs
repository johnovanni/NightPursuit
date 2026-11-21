using NAudio.Effects;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NightPursuit.Core.Audio;

/// <summary>One effect to play: volume 0 to 1, pan -1 (left) to +1 (right).</summary>
public readonly record struct CuePlay(SoundCue Cue, double Volume = 1, double Pan = 0);

/// <summary>
/// The whole mix, as one stereo 44,100 Hz float provider: one-off effects (at most 24 at once) on an effects bus,
/// music on a music bus, each with its own volume, then a master limiter and a hard clamp.
/// <see cref="Play"/> and the property setters are for the game thread; <see cref="Read"/> is for the audio thread.
/// </summary>
public sealed class AudioMixer : ISampleProvider
{
    private const double RampSeconds = 0.02;

    private readonly SoundBank _bank;
    private readonly MixingSampleProvider _effects = new(AudioFormat.Stereo) { ReadFully = true };
    private readonly VoiceLimit _voices;
    private readonly MusicChannel _music = new();
    private readonly GainEffect _effectsGain = new();
    private readonly GainEffect _musicGain = new();
    private readonly Smoothed _soundGain = new(RampSeconds, 1.0);
    private readonly ISampleProvider _output;
    private int _musicVolume;
    private int _effectsVolume;
    private int _soundOn = 1;

    public AudioMixer(SoundBank bank)
    {
        _bank = bank;
        _voices = new VoiceLimit(_effects);
        Live = new LiveVoices();
        AddEffectsInput(Live);
        MusicVolume = 7;
        EffectsVolume = 8;

        var master = new MixingSampleProvider(AudioFormat.Stereo) { ReadFully = true };
        master.AddMixerInput(new EffectSampleProvider(_effects, _effectsGain));
        master.AddMixerInput(new EffectSampleProvider(_music, _musicGain));
        var limiter = new LimiterEffect { CeilingDb = -1, LookaheadMs = 2, ReleaseMs = 100 };
        _output = new HardClamp(new EffectSampleProvider(master, limiter), AudioFormat.Ceiling);
    }

    public WaveFormat WaveFormat => AudioFormat.Stereo;

    /// <summary>The continuous sounds (engine, rumble, skid, siren, whine). The game calls <see cref="LiveVoices.Set"/>.</summary>
    public LiveVoices Live { get; }

    /// <summary>The linear gain for a volume setting: 0 at 0, otherwise 3 dB a step with 10 = 1.0.</summary>
    public static double Gain(int volume) =>
        volume <= 0 ? 0.0 : Math.Pow(10.0, (Math.Min(volume, 10) - 10) * 3.0 / 20.0);

    /// <summary>False writes silence. Voices and music keep running underneath, so nothing jumps on return.</summary>
    public bool SoundOn
    {
        get => Volatile.Read(ref _soundOn) != 0;
        set
        {
            Volatile.Write(ref _soundOn, value ? 1 : 0);
            _soundGain.Target = value ? 1.0 : 0.0;
        }
    }

    /// <summary>Music volume, 0 to 10.</summary>
    public int MusicVolume
    {
        get => Volatile.Read(ref _musicVolume);
        set
        {
            value = Math.Clamp(value, 0, 10);
            Volatile.Write(ref _musicVolume, value);
            _musicGain.LinearGain = (float)Gain(value);
        }
    }

    /// <summary>Effects volume, 0 to 10.</summary>
    public int EffectsVolume
    {
        get => Volatile.Read(ref _effectsVolume);
        set
        {
            value = Math.Clamp(value, 0, 10);
            Volatile.Write(ref _effectsVolume, value);
            _effectsGain.LinearGain = (float)Gain(value);
        }
    }

    /// <summary>The music pause level: 1.0, or 0.4 while paused. Changes ramp over 0.3 s.</summary>
    public double MusicLevel
    {
        get => _music.Level;
        set => _music.Level = value;
    }

    /// <summary>How many one-off effects are playing now.</summary>
    public int ActiveVoices => _voices.Count;

    /// <summary>Starts a one-off effect. A cue with no loaded sound is ignored. For the game thread.</summary>
    public void Play(CuePlay p)
    {
        var samples = _bank.Samples(p.Cue);
        if (samples == null) return;
        double volume = Math.Clamp(p.Volume, 0.0, 1.0);
        double pan = Math.Clamp(p.Pan, -1.0, 1.0);
        _voices.Add(new CueVoice(samples, volume, pan));
    }

    /// <summary>
    /// Adds a permanent stereo input to the effects bus (it follows the effects volume and the limiter, and is
    /// not part of the 24-voice limit). It must always return the full buffer. This is where live voices plug in.
    /// </summary>
    public void AddEffectsInput(ISampleProvider input)
    {
        if (!input.WaveFormat.Equals(AudioFormat.Stereo))
            throw new ArgumentException("An effects input must be 44,100 Hz stereo 32-bit float.", nameof(input));
        _effects.AddMixerInput(input);
    }

    /// <summary>Cross-fades from the current track to <paramref name="next"/>; null fades to silence.</summary>
    public void SetMusic(ISampleProvider? next, double fadeSeconds) => _music.Set(next, fadeSeconds);

    public int Read(Span<float> buffer)
    {
        int n = _output.Read(buffer);
        if (n < buffer.Length) buffer[n..].Clear();
        for (int i = 0; i < buffer.Length; i += 2)
        {
            float g = (float)_soundGain.Next();
            buffer[i] *= g;
            if (i + 1 < buffer.Length) buffer[i + 1] *= g;
        }
        return buffer.Length;
    }
}
