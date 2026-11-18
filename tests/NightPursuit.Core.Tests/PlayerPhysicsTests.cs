using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class PlayerPhysicsTests
{
    const double Dt = 1 / 120.0;

    [Fact]
    public void Full_throttle_tops_out_near_150_mph()   // top speed comes from fall-off and coast, not a hard cap: 9(1-r^2) = 0.6 -> r = 0.966 -> 149.7 mph (decision D3)
    {
        var car = new PlayerCar(); var f = new FeelSettings(); var track = TestWorlds.Straight();
        for (int i = 0; i < 120 * 60; i++) CarPhysics.StepPlayer(car, new(1, 0), 0, track, f, Dt);
        Assert.InRange(Units.MsToMph(car.Speed), 148, 151);
    }

    [Fact]
    public void Brakes_stop_from_60_mph_in_under_30_m()
    {
        var car = new PlayerCar { Speed = 26.8 }; var f = new FeelSettings(); var track = TestWorlds.Straight();
        for (int i = 0; i < 120 * 5; i++) CarPhysics.StepPlayer(car, new(0, 1), 0, track, f, Dt);
        Assert.Equal(0, car.Speed);
        Assert.True(car.Distance < 30, $"stopped after {car.Distance} m");
    }

    [Fact]
    public void Uphill_slows_coasting_car_more_than_flat()
    {
        var f = new FeelSettings();
        var height = new double[4000];
        for (int i = 0; i < height.Length; i++) height[i] = 0.5 * i;   // 10% grade: 0.5 m per 5 m segment
        var hill = Track.FromSegments(new double[4000], height);
        var flat = TestWorlds.Straight();
        var up = new PlayerCar { Speed = 30 }; var level = new PlayerCar { Speed = 30 };
        for (int i = 0; i < 120 * 5; i++)
        {
            CarPhysics.StepPlayer(up, new(0, 0), 0, hill, f, Dt);
            CarPhysics.StepPlayer(level, new(0, 0), 0, flat, f, Dt);
        }
        Assert.True(up.Speed < level.Speed, $"uphill {up.Speed}, flat {level.Speed}");
    }

    [Fact]
    public void No_input_on_bend_settles_at_predicted_drift()   // DRV-5: drift ~ k*v^2/Straighten
    {
        var car = new PlayerCar(); var f = new FeelSettings(); var track = TestWorlds.Bend(300);
        for (int i = 0; i < 120 * 10; i++)
        {
            car.Speed = 40;
            CarPhysics.StepPlayer(car, new(0, 0), 0, track, f, Dt);
        }
        Assert.InRange(car.LateralVel, -0.667 - 0.05, -0.667 + 0.05);
    }

    [Fact]
    public void Held_key_at_top_speed_slides_no_faster_than_10_mps()
    {
        var car = new PlayerCar(); var f = new FeelSettings(); var track = TestWorlds.Straight();
        double max = 0;
        for (int i = 0; i < 120 * 3; i++)
        {
            car.Speed = 69;
            CarPhysics.StepPlayer(car, new(0, 0), 1, track, f, Dt);
            max = Math.Max(max, Math.Abs(car.LateralVel - car.PushVel));
        }
        Assert.True(max <= 10.01, $"max slide {max}");
        Assert.True(max > 5, "the key should actually steer");
    }

    [Fact]
    public void Letting_go_after_a_lane_change_stops_the_slide_quickly()   // DRV-4: no overshoot weave
    {
        var car = new PlayerCar(); var f = new FeelSettings(); var track = TestWorlds.Straight();
        var steer = new SteeringInput();
        var right = new ControlFrame { SteerDir = 1 };
        double peak = 0;
        int guard = 0;
        while (car.Lateral < 3 && guard++ < 120 * 10)
        {
            car.Speed = 67; steer.Update(right, f, Dt);
            CarPhysics.StepPlayer(car, new(0, 0), steer.Value, track, f, Dt);
            peak = Math.Max(peak, car.Lateral);
        }
        Assert.True(guard < 120 * 10, "never reached lateral 3");
        for (int i = 0; i < 120; i++)   // 1 s after release
        {
            car.Speed = 67; steer.Update(ControlFrame.Empty, f, Dt);
            CarPhysics.StepPlayer(car, new(0, 0), steer.Value, track, f, Dt);
            peak = Math.Max(peak, car.Lateral);
            if (i == 59) Assert.True(Math.Abs(car.LateralVel) < 0.5, $"still sliding at {car.LateralVel}");
        }
        Assert.True(peak <= 4.6, $"peak lateral {peak}");
    }

    [Fact]
    public void Off_road_sets_flag_and_shake()
    {
        var f = new FeelSettings(); var track = TestWorlds.Straight();
        var off = new PlayerCar { Lateral = -9, Speed = 30 }; var on = new PlayerCar { Lateral = 0, Speed = 30 };
        CarPhysics.StepPlayer(off, new(0, 0), 0, track, f, Dt);
        CarPhysics.StepPlayer(on, new(0, 0), 0, track, f, Dt);
        Assert.True(off.OffRoad);
        Assert.False(on.OffRoad);
        Assert.True(off.Shake >= 0.12 - Dt);
        Assert.True(off.Speed < on.Speed);
    }

    [Fact]
    public void Guard_rail_clamps_and_damages()
    {
        var car = new PlayerCar { Lateral = -13, Speed = 30 };
        CarPhysics.StepPlayer(car, new(0, 0), 0, TestWorlds.Straight(), new FeelSettings(), Dt);
        Assert.Equal(-12, car.Lateral);
        Assert.True(car.Armor < 100);
        Assert.True(car.Shake >= 0.3 - Dt);
    }

    [Fact]
    public void Keyboard_steering_ramps_and_returns()
    {
        var f = new FeelSettings(); var s = new SteeringInput();
        var right = new ControlFrame { SteerDir = 1 };
        for (int i = 0; i < 15; i++) s.Update(right, f, 1 / 120.0);   // 0.125 s at 4/s
        Assert.Equal(0.5, s.Value, 6);
        Assert.False(s.UsingStick);
        for (int i = 0; i < 12; i++) s.Update(ControlFrame.Empty, f, 1 / 168.0);   // 1/14 s at 7/s
        Assert.Equal(0, s.Value, 6);
    }

    [Fact]
    public void Reversing_steer_moves_at_ramp_plus_return()
    {
        var f = new FeelSettings(); var s = new SteeringInput();
        s.Update(new ControlFrame { SteerDir = 1 }, f, 0.25);   // -> 1
        s.Update(new ControlFrame { SteerDir = -1 }, f, 0.1);   // 11/s * 0.1 = 1.1 down from 1
        Assert.Equal(-0.1, s.Value, 6);
    }

    [Fact]
    public void Stick_overrides_only_when_connected_and_no_key()
    {
        var f = new FeelSettings(); var s = new SteeringInput();
        s.Update(new ControlFrame { PadConnected = true, PadSteer = 0.7 }, f, Dt);
        Assert.Equal(0.7, s.Value, 9); Assert.True(s.UsingStick);
        s.Update(new ControlFrame { PadConnected = true, PadSteer = 5 }, f, Dt);
        Assert.Equal(1, s.Value, 9);
        s.Update(new ControlFrame { PadConnected = false, PadSteer = 0.7 }, f, Dt);
        Assert.False(s.UsingStick);
        s.Reset();
        Assert.Equal(0, s.Value);
    }
}
