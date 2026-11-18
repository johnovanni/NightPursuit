using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ControlsLab;

public partial class MainWindow : Window
{
    readonly Tuning _tuning = new();
    readonly World _world;
    readonly InputReader _input = new();
    TimeSpan _lastRender;

    public MainWindow()
    {
        InitializeComponent();
        _world = new World(_tuning);
        Game.World = _world;
        BuildTuningPanel();

        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_input.OnKeyDown(key, e.IsRepeat)) e.Handled = true;
        };
        PreviewMouseWheel += (_, e) =>
        {
            _input.OnWheel(e.Delta);
            e.Handled = true;
        };
        CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastRender) return;   // Rendering can fire more than once per frame
        double dt = _lastRender == default ? 1 / 60.0 : Math.Min(0.05, (now - _lastRender).TotalSeconds);
        _lastRender = now;

        foreach (var a in _input.Drain())
        {
            if (a.Kind == ActionKind.ToggleTuning)
                TuningPanel.Visibility = TuningPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            else
                _world.Handle(a);
        }
        _world.Update(_input.Read(IsActive), dt);
        Game.InvalidateVisual();
    }

    void BuildTuningPanel()
    {
        AddSlider("Throttle build-up per s", nameof(Tuning.ThrottleRampUp), 0.3, 6, "0.0");
        AddSlider("Throttle fall-off per s", nameof(Tuning.ThrottleRampDown), 0.3, 8, "0.0");
        AddSlider("Top speed mph", nameof(Tuning.MaxSpeedMph), 80, 220, "0");
        AddSlider("Acceleration m/s2", nameof(Tuning.MaxAccel), 3, 20, "0.0");
        AddSlider("Brakes m/s2", nameof(Tuning.BrakeDecel), 4, 30, "0");
        AddSlider("Nose turn rate deg/s", nameof(Tuning.TurnRateDeg), 10, 120, "0");
        AddSlider("Max nose angle deg", nameof(Tuning.MaxHeadingDeg), 5, 35, "0");
        AddSlider("Max sideways speed m/s", nameof(Tuning.MaxSideSpeed), 4, 25, "0.0");
        AddSlider("Steering build-up per s", nameof(Tuning.SteerRampUp), 1, 15, "0.0");
        AddSlider("Steering self-centre per s", nameof(Tuning.SteerReturn), 1, 20, "0.0");
        AddSlider("Straightening assist per s", nameof(Tuning.Straighten), 0, 15, "0.0");
        AddSlider("Speed lever rate mph/s", nameof(Tuning.LeverRateMph), 5, 80, "0");

        var check = new CheckBox
        {
            Content = "Lighter steering at high speed", Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"),
            Focusable = false, Margin = new Thickness(0, 4, 0, 10),
        };
        check.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding(nameof(Tuning.SpeedSensitiveSteering)) { Source = _tuning, Mode = BindingMode.TwoWay });
        TuningStack.Children.Add(check);
        var assist = new CheckBox
        {
            Content = "Straighten while steering", Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"),
            Focusable = false, Margin = new Thickness(0, 0, 0, 10),
        };
        assist.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding(nameof(Tuning.AssistWhileSteering)) { Source = _tuning, Mode = BindingMode.TwoWay });
        TuningStack.Children.Add(assist);

        var reset = new Button { Content = "Reset to defaults", Focusable = false, Padding = new Thickness(8, 3, 8, 3) };
        reset.Click += (_, _) => _tuning.Reset();
        TuningStack.Children.Add(reset);
    }

    void AddSlider(string label, string property, double min, double max, string format)
    {
        var header = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        header.SetBinding(TextBlock.TextProperty,
            new Binding(property) { Source = _tuning, StringFormat = label + ":  {0:" + format + "}" });
        var slider = new Slider { Minimum = min, Maximum = max, Focusable = false, Margin = new Thickness(0, 2, 0, 8) };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(property) { Source = _tuning, Mode = BindingMode.TwoWay });
        TuningStack.Children.Add(header);
        TuningStack.Children.Add(slider);
    }
}
