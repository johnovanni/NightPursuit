using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class CaltropTests
{
    const double Dt = 1.0 / 120;
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static AgentCar Runner(World w, double distance, double lateral, double speed, IDriver? driver = null)
    {
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, distance, lateral, speed);
        car.Driver = driver ?? new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    [Fact]
    public void Caltrops_puncture_a_car_that_crosses_them()
    {
        var w = Straight();
        w.Hazards.Add(new Caltrops { Distance = 100, Lateral = 0 });
        var car = Runner(w, 90, 0, 30);
        for (int i = 0; i < 60; i++) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }
        Assert.True(car.Punctured);
        Assert.Single(w.Events.OfType<VehiclePunctured>(), e => e.VehicleId == car.Id);
        Assert.Contains(w.Events, e => e is Message { Text: "RUNNER PUNCTURED", Tone: Tone.Good });
        Assert.Single(w.Hazards);   // the caltrops stay where they are
    }

    [Fact]
    public void Caltrops_cover_one_lane_only()
    {
        var w = Straight();
        w.Hazards.Add(new Caltrops { Distance = 100, Lateral = 0 });
        var car = Runner(w, 90, 5, 30);
        for (int i = 0; i < 120; i++) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }
        Assert.False(car.Punctured);
    }

    [Fact]
    public void Punctured_car_limps_off_the_road_and_crashes_without_a_wreck()
    {
        var w = Straight();
        var car = Runner(w, 100, 5, 40, new RunnerDriver());
        car.Punctured = true;
        for (int i = 0; i < 120 * 8 && car.State != VehicleState.Wrecked; i++) { w.Player.Speed = 40; w.StepOnce(ControlFrame.Empty); }
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.RanOffRoad });
        Assert.DoesNotContain(w.Hazards, h => h is Wreck);
        Assert.True(Math.Abs(car.Lateral) > 8.5, $"Lateral {car.Lateral}");
    }

    [Fact]
    public void Punctured_car_in_the_centre_lane_heads_for_its_own_side()
    {
        var w = Straight();
        var car = Runner(w, 100, 0, 40, new RunnerDriver());
        car.Punctured = true;
        car.Side = -1;
        for (int i = 0; i < 120 * 8 && car.State != VehicleState.Wrecked; i++) { w.Player.Speed = 40; w.StepOnce(ControlFrame.Empty); }
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.RanOffRoad });
        Assert.True(car.Lateral < -8.5, $"Lateral {car.Lateral}");
    }

    [Fact]
    public void Player_ignores_own_oil_and_caltrops()
    {
        var w = Straight();
        for (int i = 0; i < 120; i++)
        {
            w.Player.Speed = 40;
            w.Hazards.Clear();
            w.Hazards.Add(new OilSlick { Distance = w.Player.Distance, Lateral = w.Player.Lateral });
            w.Hazards.Add(new Caltrops { Distance = w.Player.Distance, Lateral = w.Player.Lateral });
            w.StepOnce(ControlFrame.Empty);
        }
        Assert.Equal(0, w.Player.GripLoss);
        Assert.Equal(0, w.Player.PunctureLeft);
        Assert.Equal(100, w.Player.Armor);
    }

    [Fact]
    public void Hostile_oil_takes_grip_for_1_s_once()
    {
        var w = Straight();
        var slick = new OilSlick { Distance = 0, Lateral = 0, Hostile = true };
        w.Hazards.Add(slick);
        var right = new ControlFrame { SteerDir = 1 };
        w.Player.Speed = 40;
        w.StepOnce(ControlFrame.Empty);          // the hit lands at the end of this step, before any steering
        Assert.Equal(1.0, w.Player.GripLoss, 1e-6);
        Assert.InRange(w.Player.Speed, 35.9, 36.1);
        Assert.True(slick.HitPlayer);
        Assert.Single(w.Events.OfType<Message>(), m => m.Text == "OIL - NO GRIP" && m.Tone == Tone.Bad);

        for (int i = 1; i < 108; i++)            // 0.9 s in total
        {
            slick.Distance = w.Player.Distance; slick.Lateral = w.Player.Lateral;
            w.StepOnce(right);
            Assert.True(Math.Abs(w.Player.Heading) < 1e-9, $"Heading {w.Player.Heading} at step {i}");
        }
        for (int i = 108; i < 144; i++)          // on to 1.2 s, still on the slick
        {
            slick.Distance = w.Player.Distance; slick.Lateral = w.Player.Lateral;
            w.StepOnce(right);
        }
        Assert.Single(w.Events.OfType<Message>(), m => m.Text == "OIL - NO GRIP");
        for (int i = 144; i < 156; i++) { slick.Distance = w.Player.Distance; slick.Lateral = w.Player.Lateral; w.StepOnce(right); }
        Assert.Equal(0, w.Player.GripLoss);
        Assert.True(w.Player.Heading > 0, $"Heading {w.Player.Heading}");
    }

    static World CaltropsOnPlayer(double speed)
    {
        var w = Straight();
        w.Player.Speed = speed;
        w.Hazards.Add(new Caltrops { Distance = 0, Lateral = 0, Hostile = true });
        return w;
    }

    [Fact]
    public void Hostile_caltrops_cost_10_armor_and_brake_gently_to_100_mph()
    {
        var w = CaltropsOnPlayer(67);
        var gas = new ControlFrame { Gas = true };
        w.StepOnce(gas);
        Assert.Equal(90, w.Player.Armor, 1e-9);
        Assert.Equal(5, w.Player.PunctureLeft, 1e-9);
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "PUNCTURED" });

        for (int i = 2; i <= 540; i++)           // to 4.5 s
        {
            double before = w.Player.Speed;
            w.StepOnce(gas);
            if (before > PlayerCar.PunctureSpeedCap)
            {
                Assert.Equal(0, w.Player.Throttle);
                Assert.InRange((before - w.Player.Speed) / Dt, 4.45, 4.55);
            }
        }
        Assert.InRange(w.Player.Speed, 46.75 - 0.3, 46.75 + 0.3);
        for (int i = 541; i <= 612; i++) w.StepOnce(gas);   // 5.1 s
        Assert.Equal(0, w.Player.PunctureLeft);
        Assert.True(w.Player.Throttle > 0);

        var w2 = CaltropsOnPlayer(67);
        var brake = new ControlFrame { PadConnected = true, PadBrake = 1 };
        w2.StepOnce(brake);
        for (int i = 2; i <= 120; i++)
        {
            double before = w2.Player.Speed;
            w2.StepOnce(brake);
            Assert.True((before - w2.Player.Speed) / Dt >= w2.Feel.BrakeDecel, $"slowdown {(before - w2.Player.Speed) / Dt}");
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public void Hostile_caltrops_never_wreck_a_weak_player(double armor)
    {
        var w = CaltropsOnPlayer(40);
        w.Player.Armor = armor;
        w.StepOnce(ControlFrame.Empty);
        Assert.NotEqual(VehicleState.Wrecked, w.Player.State);
        Assert.Equal(1, w.Player.Armor, 1e-9);
        Assert.Contains(w.Events, e => e is Message { Text: "PUNCTURED", Tone: Tone.Bad });
        Assert.Equal(5, w.Player.PunctureLeft, 1e-9);
    }

    [Fact]
    public void Hostile_caltrops_cap_a_slower_car_at_100_mph()
    {
        var w = CaltropsOnPlayer(40);
        var gas = new ControlFrame { Gas = true };
        w.StepOnce(gas);
        for (int i = 1; i < 600; i++)
        {
            double before = w.Player.Speed;
            w.StepOnce(gas);
            Assert.True(w.Player.Speed <= PlayerCar.PunctureSpeedCap + 0.1, $"Speed {w.Player.Speed} at step {i}");
            if (before <= PlayerCar.PunctureSpeedCap) Assert.Equal(0, w.Player.Brake);
        }
        for (int j = 0; j < 12 && w.Player.PunctureLeft > 0; j++) w.StepOnce(gas);   // the last of the 5 s
        Assert.Equal(0, w.Player.PunctureLeft);
        for (int j = 0; j < 360; j++) w.StepOnce(gas);
        Assert.True(w.Player.Speed > 45, $"Speed {w.Player.Speed}");
    }
}
