using System.IO;
using NightPursuit.Core.Audio;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Audio;

/// <summary>
/// The game's sound: the sound bank, mixer, director, music slots and (in the game) the output device. The game loop
/// calls <see cref="NoteMenuKey"/> for each menu key and then <see cref="Update"/> once a frame, all on the game thread.
/// With no device (<c>openDevice</c> false, or none found) it still runs the director and the mixer, so the
/// diagnostics can time and render it.
/// </summary>
public sealed class SoundSystem : IDisposable
{
    const double BossFade = 2.0, ScreenFade = 1.5;
    // A faded-out track is kept this long past its fade, so the mixer has surely dropped it before it is closed.
    const double CloseAfterFade = 0.5;

    readonly string _musicDir;
    readonly SoundDirector _director;
    readonly AudioOutput? _output;
    readonly HashSet<string> _badFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(MusicDecoder Decoder, double CloseAt)> _retired = new();
    readonly List<string> _pending = new();   // notices found while playing, posted by the next Update
    MusicCue? _cue;
    MusicDecoder? _current;
    double _now;
    bool _lostShown;

    public SoundSystem(string contentDir, IEnumerable<MissionDefinition> missions, bool openDevice)
    {
        _musicDir = Path.Combine(contentDir, "music");
        var problems = new List<string>();

        var bank = SoundBank.Load(Path.Combine(contentDir, "sounds"), out var bankProblems);
        problems.AddRange(bankProblems);
        Mixer = new AudioMixer(bank);

        var (manifest, musicProblems) = MusicManifest.Load(_musicDir);
        problems.AddRange(musicProblems);
        var slots = new MusicSlots(manifest);
        _director = new SoundDirector(slots);

        // Every named file must be there and open, once, at start-up. A bad one is silent from then on.
        foreach (string file in slots.FilesNamed(missions))
        {
            using var probe = MusicDecoder.TryOpen(Path.Combine(_musicDir, file), 1, out string? problem);
            if (problem is not null)
            {
                _badFiles.Add(file);
                problems.Add(problem);
            }
        }

        if (openDevice)
        {
            _output = AudioOutput.TryStart(Mixer, out string? deviceProblem);
            if (deviceProblem is not null) problems.Add(deviceProblem);
        }
        Problems = problems;
    }

    public AudioMixer Mixer { get; }

    /// <summary>Notices for start-up problems: the sound files, the music list, the music files and the device.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>True when a sound device is playing (it can still be lost later).</summary>
    public bool HasOutput => _output is not null;

    /// <summary>Hands the director each menu key given to the flow this frame, before <see cref="Update"/>.</summary>
    public void NoteMenuKey(MenuKey key) => _director.NoteMenuKey(key);

    /// <summary>One call a frame, after the flow has updated.</summary>
    public void Update(ScreenFlow flow, double seconds)
    {
        if (seconds > 0) _now += seconds;
        var frame = _director.Update(flow, seconds);

        var s = flow.Settings;
        if (Mixer.SoundOn != s.SoundOn) Mixer.SoundOn = s.SoundOn;
        if (Mixer.MusicVolume != s.MusicVolume) Mixer.MusicVolume = s.MusicVolume;
        if (Mixer.EffectsVolume != s.EffectsVolume) Mixer.EffectsVolume = s.EffectsVolume;

        foreach (var cue in frame.Cues) Mixer.Play(cue);
        Mixer.Live.Set(frame.Live);

        if (_cue != frame.Music) ChangeMusic(frame.Music);
        _cue = frame.Music;
        Mixer.MusicLevel = frame.Music.Level;

        CloseRetired();
        if (_output is { Lost: true } && !_lostShown)
        {
            _lostShown = true;
            _pending.Add("SOUND STOPPED: THE SOUND DEVICE WENT AWAY");
        }
        if (_pending.Count > 0)
        {
            foreach (string notice in _pending) flow.ShowNotice(notice);
            _pending.Clear();
        }
    }

    // Opens the new track on the game thread (the slot, track or generation changed) and fades to it.
    void ChangeMusic(MusicCue cue)
    {
        bool changed = _cue is not { } o || o.Slot != cue.Slot || o.Track != cue.Track || o.Generation != cue.Generation;
        if (!changed) return;   // only the level moved: the mixer ramps that itself

        MusicDecoder? next = null;
        if (cue.Track is { } track && !_badFiles.Contains(track.File))
        {
            next = MusicDecoder.TryOpen(Path.Combine(_musicDir, track.File), track.Volume, out string? problem);
            if (next is null)
            {
                _badFiles.Add(track.File);   // a file that stopped working since start-up: one notice, then silence
                if (problem is not null) _pending.Add(problem);
            }
        }
        if (next is null && _current is null) return;   // silence to silence

        double fade = cue.Slot == MusicSlot.BossTheme ? BossFade : ScreenFade;
        if (_current is not null) _retired.Add((_current, _now + fade + CloseAfterFade));
        _current = next;
        Mixer.SetMusic(next, fade);
    }

    void CloseRetired()
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
            if (_retired[i].CloseAt <= _now)
            {
                _retired[i].Decoder.Dispose();
                _retired.RemoveAt(i);
            }
    }

    /// <summary>Stops the device first, then closes every music file.</summary>
    public void Dispose()
    {
        _output?.Dispose();
        _current?.Dispose();
        _current = null;
        foreach (var (decoder, _) in _retired) decoder.Dispose();
        _retired.Clear();
    }
}
