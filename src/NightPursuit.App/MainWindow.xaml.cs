using System.Windows;
using System.Windows.Input;
using NightPursuit.App.Input;
using NightPursuit.Core.Missions;

namespace NightPursuit.App;

public partial class MainWindow : Window
{
    readonly InputHub _input = new();
    readonly GameLoop _loop;

    public MainWindow(MissionRun run)
    {
        InitializeComponent();
        Game.Run = run;

        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_input.OnKeyDown(key, e.IsRepeat)) e.Handled = true;
        };
        PreviewKeyUp += (_, e) => _input.OnKeyUp(e.Key == Key.System ? e.SystemKey : e.Key);
        // Losing focus while a key is held means the KeyUp never arrives here.
        Deactivated += (_, _) => _input.ReleaseAll();
        PreviewMouseWheel += (_, e) =>
        {
            _input.OnWheel(e.Delta);
            e.Handled = true;
        };

        _loop = new GameLoop(this, run, Game, _input);
        Closed += (_, _) => _loop.Stop();
    }

    /// <summary>Stops the game loop (used when a fatal error is being shown).</summary>
    public void StopLoop() => _loop.Stop();
}
