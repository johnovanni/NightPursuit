using System.Windows;
using System.Windows.Media;
using NightPursuit.App.Input;
using NightPursuit.App.Rendering;
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

        foreach (var a in _input.Drain()) _run.Handle(a);
        _run.Update(_input.Read(_window.IsActive), seconds);   // Core caps a long stall at 0.25 s
        _view.InvalidateVisual();
    }
}
