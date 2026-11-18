using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class SmokeTests
{
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    [Fact]
    public void Pursuer_in_smoke_loses_the_player_and_slows()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -30, 0, 40);
        w.Agents.Add(car);
        w.Hazards.Add(new SmokeCloud { Distance = -30, PrevDistance = -30, Lateral = 0, Speed = 0 });

        for (int i = 0; i < 120; i++) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }
        Assert.True(car.Blinded > 0, $"Blinded {car.Blinded}");
        for (int i = 0; i < 120; i++) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }

        // StepOnce does not clear Events, so they hold everything published over the 2 s.
        var lost = w.Events.OfType<Message>().Where(m => m.Text == "PURSUER LOST YOU IN THE SMOKE").ToList();
        Assert.Equal(Tone.Good, Assert.Single(lost).Tone);
        Assert.True(car.Speed <= 0.6 * 40 + 0.5, $"speed {car.Speed}");
    }

    [Fact]
    public void Blindness_lasts_2_s_after_leaving_the_smoke()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 100, 0, 30);
        w.Agents.Add(car);
        var state = new RunnerDriver.State { Target = 0, LaneTimer = 100 };
        car.DriverState = state;
        car.Blinded = 2;
        for (int i = 1; i <= 240; i++)
        {
            w.Player.Speed = 30;
            w.StepOnce(ControlFrame.Empty);
            if (i == 238) Assert.True(car.Blinded > 0, $"Blinded {car.Blinded} at 1.983 s");
            Assert.Same(state, car.DriverState);
        }
        Assert.Equal(0, car.Blinded);
    }

    [Fact]
    public void Blind_car_wanders_but_stays_on_the_road()
    {
        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Runner, 100, 0, 30);
        w.Agents.Add(car);
        var targets = new HashSet<double>();
        for (int i = 0; i < 120 * 10; i++)
        {
            car.Blinded = 2;
            w.Player.Speed = 30;
            w.StepOnce(ControlFrame.Empty);
            targets.Add(car.WanderLateral);
            Assert.True(Math.Abs(car.Lateral) <= 6.01, $"lateral {car.Lateral} at step {i}");
        }
        Assert.True(targets.Count >= 5, $"distinct targets {targets.Count}");
    }
}
