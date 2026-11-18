using System.Windows;
using System.Windows.Input;
using NightPursuit.App.Input;
using NightPursuit.Core.Control;
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
#if DEBUG
        // The run keeps the FeelSettings it was given when it rebuilds after a restart, so this stays the live one.
        var tuning = new Tuning.TuningPanel(run.World.Feel);
        Root.Children.Add(tuning);
#endif

        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
#if DEBUG
            if (key == Key.F12)
            {
                if (!e.IsRepeat) tuning.Toggle();
                e.Handled = true;
                return;
            }
#endif
            if (_input.OnKeyDown(key, e.IsRepeat)) e.Handled = true;
        };
        PreviewKeyUp += (_, e) => _input.OnKeyUp(e.Key == Key.System ? e.SystemKey : e.Key);
        // Losing focus while a key is held means the KeyUp never arrives here.
        Deactivated += (_, _) =>
        {
            _input.ReleaseAll();
            // Cruise or the lever would keep driving an unattended car (decision D12).
            if (run.State == RunState.Playing) run.Handle(new GameAction(ActionKind.Pause));
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (IsActive) _input.OnWheel(e.Delta);
            e.Handled = true;
        };

        _loop = new GameLoop(this, run, Game, _input);
        Closed += (_, _) => _loop.Stop();
    }

    /// <summary>Stops the game loop (used when a fatal error is being shown).</summary>
    public void StopLoop() => _loop.Stop();
}
