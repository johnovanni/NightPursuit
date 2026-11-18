#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using NightPursuit.Core.Control;

namespace NightPursuit.App.Debug;

/// <summary>
/// Debug-builds-only side panel (F12) with a slider for every feel value, bound two-way to the live
/// <see cref="FeelSettings"/> the run uses. Nothing in it takes keyboard focus, so the game keys keep working.
/// </summary>
public sealed class TuningPanel : Border
{
    static readonly FontFamily Font = new("Consolas");
    readonly StackPanel _stack = new() { Margin = new Thickness(12) };
    readonly FeelSettings _feel;

    public TuningPanel(FeelSettings feel)
    {
        _feel = feel;
        Width = 300;
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x04, 0x08, 0x0E));
        Focusable = false;
        Visibility = Visibility.Collapsed;

        _stack.Children.Add(new TextBlock { Text = "FEEL TUNING (F12)", Foreground = Brushes.Orange, FontFamily = Font, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        AddSlider("Throttle build-up per s", nameof(FeelSettings.ThrottleRampUp), 0.3, 6, "0.0");
        AddSlider("Throttle fall-off per s", nameof(FeelSettings.ThrottleRampDown), 0.3, 8, "0.0");
        AddSlider("Top speed mph", nameof(FeelSettings.MaxSpeedMph), 80, 220, "0");
        AddSlider("Acceleration m/s2", nameof(FeelSettings.MaxAccel), 3, 20, "0.0");
        AddSlider("Brakes m/s2", nameof(FeelSettings.BrakeDecel), 4, 30, "0");
        AddSlider("Nose turn rate deg/s", nameof(FeelSettings.TurnRateDeg), 10, 120, "0");
        AddSlider("Max nose angle deg", nameof(FeelSettings.MaxHeadingDeg), 5, 35, "0");
        AddSlider("Max sideways speed m/s", nameof(FeelSettings.MaxSideSpeed), 4, 25, "0.0");
        AddSlider("Steering build-up per s", nameof(FeelSettings.SteerRampUp), 1, 15, "0.0");
        AddSlider("Steering self-centre per s", nameof(FeelSettings.SteerReturn), 1, 20, "0.0");
        AddSlider("Straightening assist per s", nameof(FeelSettings.Straighten), 0, 15, "0.0");
        AddSlider("Speed lever rate mph/s", nameof(FeelSettings.LeverRateMph), 5, 80, "0");
        AddCheckBox("Lighter steering at high speed", nameof(FeelSettings.SpeedSensitiveSteering), new Thickness(0, 4, 0, 4));
        AddCheckBox("Straighten while steering", nameof(FeelSettings.AssistWhileSteering), new Thickness(0, 0, 0, 10));

        var reset = new Button { Content = "Reset to defaults", Focusable = false, Padding = new Thickness(8, 3, 8, 3) };
        reset.Click += (_, _) => _feel.Reset();
        _stack.Children.Add(reset);

        Child = new ScrollViewer { Content = _stack, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public void Toggle() =>
        Visibility = Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    void AddSlider(string label, string property, double min, double max, string format)
    {
        var header = new TextBlock { Foreground = Brushes.White, FontFamily = Font, FontSize = 13 };
        header.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = _feel, StringFormat = label + ":  {0:" + format + "}" });
        var slider = new Slider { Minimum = min, Maximum = max, Focusable = false, Margin = new Thickness(0, 2, 0, 8) };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(property) { Source = _feel, Mode = BindingMode.TwoWay });
        _stack.Children.Add(header);
        _stack.Children.Add(slider);
    }

    void AddCheckBox(string label, string property, Thickness margin)
    {
        var box = new CheckBox { Content = label, Foreground = Brushes.White, FontFamily = Font, Focusable = false, Margin = margin };
        box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(property) { Source = _feel, Mode = BindingMode.TwoWay });
        _stack.Children.Add(box);
    }
}
#endif
