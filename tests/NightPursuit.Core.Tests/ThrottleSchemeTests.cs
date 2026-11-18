using NightPursuit.Core.Control;

namespace NightPursuit.Core.Tests;

public class ThrottleSchemeTests
{
    [Fact] public void Ramp_builds_at_ramp_rate() { var s = new RampPedalScheme(); var f = new FeelSettings(); PedalOutput o = default; for (int i = 0; i < 60; i++) o = s.Update(new ControlFrame { Gas = true }, 0, f, 1 / 120.0); Assert.Equal(0.75, o.Throttle, 6); }
    [Fact] public void Ramp_falls_away_on_release() { var s = new RampPedalScheme(); var f = new FeelSettings(); for (int i = 0; i < 120; i++) s.Update(new ControlFrame { Gas = true }, 0, f, 1 / 120.0); var o = s.Update(new ControlFrame(), 0, f, 0.1); Assert.Equal(0.7, o.Throttle, 6); }
    [Fact] public void Cruise_holds_speed_and_brake_cancels() { var s = new RampPedalScheme(); var f = new FeelSettings(); s.Handle(new GameAction(ActionKind.Cruise), 40, f); Assert.Equal(40, s.TargetSpeed); s.Update(new ControlFrame { Brake = true }, 40, f, 0.01); Assert.Null(s.TargetSpeed); }
    [Fact] public void Lever_preset_sets_fraction_of_top_speed() { var s = new SpeedLeverScheme(); var f = new FeelSettings(); s.Handle(new GameAction(ActionKind.LeverPreset, 8), 0, f); Assert.Equal(0.8 * f.MaxSpeed, s.TargetSpeed!.Value, 6); }
    [Fact] public void Lever_brakes_when_too_fast() { var s = new SpeedLeverScheme(); var f = new FeelSettings(); s.Handle(new GameAction(ActionKind.LeverPreset, 2), 0, f); var o = s.Update(new ControlFrame(), 60, f, 1 / 120.0); Assert.True(o.Brake > 0); Assert.Equal(0, o.Throttle); }
    [Fact] public void Max_heading_shrinks_with_speed() { var f = new FeelSettings(); Assert.Equal(20 * Math.PI / 180, f.MaxHeadingAt(20), 9); Assert.Equal(Math.Asin(10 / 69.3), f.MaxHeadingAt(69.3), 9); }

    [Fact] public void Feel_defaults_and_reset()
    {
        var f = new FeelSettings();
        Assert.Equal(90, f.TurnRateDeg); Assert.Equal(8, f.Straighten); Assert.True(f.AssistWhileSteering); Assert.True(f.SpeedSensitiveSteering);
        Assert.Equal(155, f.MaxSpeedMph); Assert.Equal(30, f.LeverRateMph);
        var changed = new List<string?>();
        f.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        f.AssistWhileSteering = false; f.TurnRateDeg = 10; f.SpeedSensitiveSteering = false;
        Assert.Equal(new[] { "AssistWhileSteering", "TurnRateDeg", "SpeedSensitiveSteering" }, changed);
        f.Reset();
        Assert.True(f.AssistWhileSteering); Assert.Equal(90, f.TurnRateDeg); Assert.True(f.SpeedSensitiveSteering);
    }

    [Fact] public void Lever_up_down_and_clamp()
    {
        var s = new SpeedLeverScheme(); var f = new FeelSettings();
        s.Reset(20);
        s.Handle(new GameAction(ActionKind.LeverUp), 20, f);
        Assert.Equal(20 + 5 / 2.23694, s.TargetSpeed!.Value, 6);
        s.Handle(new GameAction(ActionKind.LeverPreset, 10), 20, f);
        s.Handle(new GameAction(ActionKind.LeverUp), 20, f);
        Assert.Equal(f.MaxSpeed, s.TargetSpeed!.Value, 6);
        s.Handle(new GameAction(ActionKind.LeverPreset, 0), 20, f);
        s.Handle(new GameAction(ActionKind.LeverDown), 20, f);
        Assert.Equal(0, s.TargetSpeed!.Value, 6);
    }

    [Fact] public void Lever_holds_steady_state_throttle()
    {
        var s = new SpeedLeverScheme(); var f = new FeelSettings();
        s.Reset(30);
        PedalOutput o = default;
        for (int i = 0; i < 240; i++) o = s.Update(new ControlFrame(), 30, f, 1 / 120.0);
        double r = 30 / f.MaxSpeed;
        Assert.Equal((0.6 + 1.6 * r) / (f.MaxAccel * (1 - r * r) + 1.6 * r), o.Throttle, 6);
        Assert.Equal(0, o.Brake);
    }

    [Fact] public void Cruise_toggles_and_has_minimum_speed()
    {
        var s = new RampPedalScheme(); var f = new FeelSettings();
        s.Handle(new GameAction(ActionKind.Cruise), 2, f);
        Assert.Equal(5, s.TargetSpeed);
        s.Handle(new GameAction(ActionKind.Cruise), 2, f);
        Assert.Null(s.TargetSpeed);
    }

    [Fact] public void Control_frame_empty_is_neutral()
    {
        Assert.Equal(0, ControlFrame.Empty.SteerDir);
        Assert.False(ControlFrame.Empty.Gas);
        Assert.Same(ControlFrame.Empty, ControlFrame.Empty);
    }
}
