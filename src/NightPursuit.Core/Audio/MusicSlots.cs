using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Audio;

/// <summary>Which music slot a cue belongs to.</summary>
public enum MusicSlot { None, Title, Briefing, MissionTheme, BossTheme, Results, GameOver, Finished }

/// <summary>
/// What should be playing now. <see cref="Slot"/> is the slot that supplies the track (so a title that borrows the
/// briefing track reports Briefing and carries on without a restart). A null <see cref="Track"/> is silence.
/// <see cref="Generation"/> goes up when the same slot must start again from the beginning.
/// </summary>
public readonly record struct MusicCue(MusicSlot Slot, MusicTrack? Track, double Level, int Generation)
{
    /// <summary>True when the same music carries on: same slot, track and generation (only the level may differ).</summary>
    public bool SameMusic(MusicCue other) => Slot == other.Slot && Track == other.Track && Generation == other.Generation;
}

/// <summary>Decides, once a frame, which music slot plays for the screen the game is on.</summary>
public sealed class MusicSlots(MusicManifest manifest)
{
    /// <summary>Music level while paused, and under the pause menu's side screens (not Settings).</summary>
    public const double PauseLevel = 0.4;

    // The last cue for a screen that is not a side screen; side screens carry on with it.
    MusicSlot _slot = MusicSlot.None;
    MusicTrack? _track;
    Screen? _lastMain;
    int _generation;

    World? _world;
    double _seenTime = double.NaN;
    bool _bossSeen;

    public MusicCue Update(ScreenFlow flow)
    {
        var screen = flow.Current;
        if (IsSide(screen))
        {
            // Quiet whenever the run is paused (the pause menu, or help opened from play, which pauses it); Settings stays full so volumes can be judged.
            bool paused = _lastMain == Screen.Pause || flow.Game?.Run?.State == RunState.Paused;
            double level = screen != Screen.Settings && paused ? PauseLevel : 1.0;
            return new MusicCue(_slot, _track, level, _generation);
        }

        _lastMain = screen;
        if (screen is Screen.Play or Screen.Pause) WatchWorld(flow.Game?.Run?.World);

        var mission = flow.Game?.Mission.Music;
        (_slot, _track) = screen switch
        {
            Screen.Title => manifest.Title is { } t ? (MusicSlot.Title, t) : (MusicSlot.Briefing, manifest.Briefing),
            Screen.Briefing => (MusicSlot.Briefing, manifest.Briefing),
            Screen.Play or Screen.Pause when _bossSeen && mission?.Boss is { } boss => (MusicSlot.BossTheme, boss),
            Screen.Play or Screen.Pause => (MusicSlot.MissionTheme, mission?.Theme),
            Screen.MissionComplete => (MusicSlot.Results, manifest.Results),
            Screen.GameOver or Screen.MissionFailed => Fallback(MusicSlot.GameOver, manifest.GameOver),
            Screen.Finished => Fallback(MusicSlot.Finished, manifest.Finished),
            _ => (MusicSlot.None, null),
        };
        return new MusicCue(_slot, _track, screen == Screen.Pause ? PauseLevel : 1.0, _generation);
    }

    (MusicSlot, MusicTrack?) Fallback(MusicSlot own, MusicTrack? track) =>
        track is not null ? (own, track) : (MusicSlot.Results, manifest.Results);

    static bool IsSide(Screen s) =>
        s is Screen.Settings or Screen.Controls or Screen.QuitConfirm or Screen.HighScores or Screen.EnterInitials;

    // A new World is a restart or the next mission: start over. Events are read once per new Time, as the effects do,
    // so frames where the game is not stepped can neither repeat nor lose the sighting.
    void WatchWorld(World? world)
    {
        if (world is null) return;
        if (!ReferenceEquals(world, _world))
        {
            _world = world;
            _seenTime = double.NaN;
            _bossSeen = false;
            _generation++;
        }
        if (world.Time == _seenTime) return;
        _seenTime = world.Time;
        foreach (var e in world.Events)
            if (e is BossAppeared) _bossSeen = true;
    }

    /// <summary>Every file the music list and the missions name, once each, for the start-up check.</summary>
    public IEnumerable<string> FilesNamed(IEnumerable<MissionDefinition> missions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = manifest.Tracks().Concat(missions.SelectMany(m => new[] { m.Music?.Theme, m.Music?.Boss }.OfType<MusicTrack>()));
        foreach (var t in tracks)
            if (seen.Add(t.File)) yield return t.File;
    }
}
