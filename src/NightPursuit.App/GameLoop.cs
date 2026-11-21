using System.Windows;
using System.Windows.Media;
using NightPursuit.App.Audio;
using NightPursuit.App.Input;
using NightPursuit.App.Rendering;
using NightPursuit.App.Screens;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;

namespace NightPursuit.App;

/// <summary>
/// Drives the screen flow once per rendered frame. Each frame's presses go to one side only: game actions in play,
/// menu keys on a screen; the other queue is dropped, so a key that opens or closes a screen never also acts on the game.
/// </summary>
public sealed class GameLoop
{
    readonly Window _window;
    readonly ScreenFlow _flow;
    readonly GameView _view;
    readonly ScreenHost _host;
    readonly InputHub _input;
    readonly SoundSystem? _sound;   // null when sound could not be built
    bool _soundBroken;   // a sound failure silences and disposes the sound system for good; the game carries on
    TimeSpan _last;
    bool _haveLast;
    bool _everActive;   // the window has had focus at least once since it opened
    // Space and pad A both confirm a screen and fire: after a screen hands over to play, the guns stay off until both are let go.
    bool _fireLatch;
    bool _closing;
    // A result screen ignores Confirm until the confirm/fire controls have been released once (a player still firing as the mission ends).
    readonly ReleaseFirstGate _resultGate = new();
    Screen _lastScreen;

    public GameLoop(Window window, ScreenFlow flow, GameView view, ScreenHost host, InputHub input, SoundSystem? sound)
    {
        _sound = sound;
        _window = window;
        _flow = flow;
        _view = view;
        _host = host;
        _input = input;
        _lastScreen = flow.Current;
        _host.Show(flow);   // the first screen is up before the first frame
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop() => CompositionTarget.Rendering -= OnFrame;

    /// <summary>
    /// The window lost focus (it may be minimised, when WPF stops raising frames): the flow has just paused play, so tell the sound
    /// at once. Silent live voices and the quieter pause level then reach the mixer without waiting for a frame.
    /// </summary>
    public void OnDeactivated()
    {
        if (_soundBroken || _sound is null) return;
        try { _sound.Update(_flow, 0); }
        catch (Exception ex) { SoundFailed(ex); }
    }

    /// <summary>Switches the sound off for good (a fatal error is being shown), so nothing plays under the error box.</summary>
    public void StopSound()
    {
        if (_sound is null) return;
        try { _sound.Mixer.SoundOn = false; } catch { /* best effort */ }
        try { _sound.Dispose(); } catch { /* best effort */ }
        _soundBroken = true;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_haveLast && now == _last) return;   // Rendering can fire more than once per frame
        double seconds = _haveLast ? (now - _last).TotalSeconds : 0;
        _last = now;
        _haveLast = true;

        // A newly opened window can take a moment to get focus (for example when started from a terminal).
        // Until it has had focus once, hold everything still instead of pausing: pausing here left the game
        // paused behind the intro help, so pressing gas hid the help but nothing moved.
        if (!_everActive)
        {
            if (!_window.IsActive) { _view.InvalidateVisual(); return; }
            _everActive = true;
            seconds = 0;   // don't run the time spent waiting for focus
        }

        // Losing focus pauses play, including mid-respawn, so it never carries on with nobody at the keyboard (D12).
        if (!_window.IsActive) _flow.FocusLost();

        // Read first: polling the pad is what queues its press-edge actions and menu keys, and they must act this frame.
        var held = _input.Read(_window.IsActive);
        _resultGate.Update(held.Fire || held.PadFire || _input.EnterHeld);   // Space, pad A, Enter
        bool startedOnScreen = _flow.Current != Screen.Play;
        if (!startedOnScreen)
        {
            foreach (var a in _input.Drain()) _flow.HandleGameAction(a);
            foreach (var _ in _input.DrainMenu()) { }   // dropped: in play, keys are game actions
            if (_flow.ShowIntroHelp && (held.Gas || held.PadGas > 0.2)) _flow.IntroHelpSeen();   // gas to start
        }
        else
        {
            bool holdConfirm = _resultGate.Waiting && IsResultScreen(_flow.Current);
            foreach (var k in _input.DrainMenu())
            {
                if (holdConfirm && k.Input == MenuInput.Confirm) continue;
                _flow.Handle(k);
                if (!_soundBroken && _sound is not null)
                {
                    try { _sound.NoteMenuKey(k); }
                    catch (Exception ex) { SoundFailed(ex); }
                }
            }
            foreach (var _ in _input.Drain()) { }   // dropped: on a screen, keys are menu keys
        }

        if (startedOnScreen && _flow.Current == Screen.Play) _fireLatch = true;
        if (_fireLatch)
        {
            if (held.Fire || held.PadFire) held = WithoutFire(held);
            else _fireLatch = false;
        }

        _flow.Update(held, seconds);   // Core caps a long stall at 0.25 s
        if (!_soundBroken && _sound is not null)
        {
            try { _sound.Update(_flow, seconds); }
            catch (Exception ex) { SoundFailed(ex); }   // audio must never crash or freeze the game
        }
        if (_flow.Current != _lastScreen)
        {
            _lastScreen = _flow.Current;
            if (IsResultScreen(_lastScreen)) _resultGate.Begin();
        }

        var run = _flow.Game?.Run;
        _view.Run = run;
        _view.ShowHelp = _flow.ShowIntroHelp && _flow.Current == Screen.Play;
        _view.ShakeEnabled = _flow.Settings.ScreenShake;
        _view.TextScale = _flow.Settings.TextScale;
        _view.Covered = _flow.Current != Screen.Play;
        _view.SoundOff = !_flow.Settings.SoundOn;
        bool soundUnavailable = _soundBroken || _sound is not { Working: true };
        _view.NoSound = _flow.Settings.SoundOn && soundUnavailable;
        _flow.SoundUnavailable = soundUnavailable;
        _view.Notice = _flow.Current == Screen.Play ? _flow.Notice : null;
        if (run is not null)
        {
            _view.Effects.Absorb(run.World);   // a new world (next mission, restart) clears the old effects
            // Behind a screen the road is frozen, explosions included.
            if (_flow.Current == Screen.Play && run.State != RunState.Paused) _view.Effects.Update(seconds);
        }
        _host.Show(_flow);
        _view.InvalidateVisual();

        if (_flow.ExitRequested && !_closing)
        {
            _closing = true;
            Stop();
            _window.Close();
        }
    }

    // A sound failure silences the sound system for good (so a frozen engine or music cannot keep playing), shows one
    // notice and writes the exception to sound-error.log in the data folder. The game itself carries on.
    void SoundFailed(Exception ex)
    {
        if (_soundBroken) return;
        _soundBroken = true;
        try { _sound?.Mixer.SoundOn = false; } catch { /* best effort */ }
        try { _sound?.Dispose(); } catch { /* best effort */ }
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.DataDirectory);
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.DataDirectory, "sound-error.log"), $"{DateTime.Now:s} {ex}{Environment.NewLine}");
        }
        catch { /* nothing more to do */ }
        _flow.ShowNotice("SOUND STOPPED: SOMETHING WENT WRONG");
    }

    static bool IsResultScreen(Screen s) => s is Screen.MissionComplete or Screen.GameOver or Screen.Finished or Screen.EnterInitials;

    static ControlFrame WithoutFire(ControlFrame f) => new()
    {
        SteerDir = f.SteerDir, Gas = f.Gas, Brake = f.Brake, Fire = false,
        PadConnected = f.PadConnected, PadFire = false, PadSteer = f.PadSteer, PadGas = f.PadGas, PadBrake = f.PadBrake,
    };
}
