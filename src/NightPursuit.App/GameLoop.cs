using System.Windows;
using System.Windows.Media;
using NightPursuit.App.Input;
using NightPursuit.App.Rendering;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;

namespace NightPursuit.App;

/// <summary>Drives the mission once per rendered frame.</summary>
public sealed class GameLoop
{
    readonly Window _window;
    readonly MissionRun _run;
    readonly GameView _view;
    readonly InputHub _input;
    TimeSpan _last;
    bool _haveLast;
    bool _gasSeen;   // the intro help stays up until the first gas

    public GameLoop(Window window, MissionRun run, GameView view, InputHub input)
    {
        _window = window;
        _run = run;
        _view = view;
        _input = input;
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop() => CompositionTarget.Rendering -= OnFrame;

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_haveLast && now == _last) return;   // Rendering can fire more than once per frame
        double seconds = _haveLast ? (now - _last).TotalSeconds : 0;
        _last = now;
        _haveLast = true;

        // Losing focus pauses the run, including mid-respawn, so it never carries on with nobody at the keyboard.
        if (!_window.IsActive && _run.State == RunState.Playing) _run.Handle(new GameAction(ActionKind.Pause));

        // Read first: polling the pad is what queues its press-edge actions, and they must act this frame.
        var held = _input.Read(_window.IsActive);
        foreach (var a in _input.Drain())
        {
            // The help screen is an App matter: the run never sees it.
            if (a.Kind == ActionKind.ToggleHelp) { _view.ShowHelp = !_view.ShowHelp; continue; }
            if (a.Kind == ActionKind.Restart && _run.CanRestart) { _view.ShowHelp = true; _gasSeen = false; }
            _run.Handle(a);
        }
        if (!_gasSeen && (held.Gas || held.PadGas > 0.2))
        {
            _gasSeen = true;
            _view.ShowHelp = false;
        }
        _run.Update(held, seconds);   // Core caps a long stall at 0.25 s
        _view.Effects.Absorb(_run.World);
        if (_run.State != RunState.Paused) _view.Effects.Update(seconds);
        _view.InvalidateVisual();
    }
}
