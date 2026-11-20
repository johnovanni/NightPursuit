using System.Windows;
using System.Windows.Input;
using NightPursuit.App.Input;
using NightPursuit.Core.Session;

namespace NightPursuit.App;

public partial class MainWindow : Window
{
    readonly InputHub _input = new();
    readonly GameLoop _loop;

    public MainWindow(ScreenFlow flow)
    {
        InitializeComponent();
#if DEBUG
        // Every run of every game is built with the flow's FeelSettings (and keeps it across a restart), so this is the live one.
        var tuning = new Tuning.TuningPanel(flow.Feel);
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
            // Cruise or the lever would keep driving an unattended car (decision D12); on any other screen nothing changes.
            flow.FocusLost();
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (IsActive) _input.OnWheel(e.Delta);
            e.Handled = true;
        };

        _loop = new GameLoop(this, flow, View, Host, _input);
        Closed += (_, _) => _loop.Stop();
    }

    /// <summary>Stops the game loop (used when a fatal error is being shown).</summary>
    public void StopLoop() => _loop.Stop();
}
