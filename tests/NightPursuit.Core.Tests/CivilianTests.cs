using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class CivilianTests
{
    const double Hz = 120;

    static World Straight()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 0;
        w.Player.SnapPrev();
        return w;
    }

    static AgentCar Civilian(World w, double distance, double lateral, double speed, double? armor = null)
    {
        var car = AgentCar.Create(w.NextId(), AgentKind.Civilian, distance, lateral, speed);
        if (armor is { } a) car.Armor = a;
        w.Agents.Add(car);
        return car;
    }

    static AgentCar Still(World w, AgentKind kind, double distance, double lateral, double speed)
    {
        var car = AgentCar.Create(w.NextId(), kind, distance, lateral, speed);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    /// <summary>Steps <paramref name="steps"/> times with the player held at (<paramref name="speed"/>, <paramref name="lateral"/>).</summary>
    static void Run(World w, int steps, double speed, double lateral, ControlFrame? input = null, Action? each = null)
    {
        for (int i = 0; i < steps; i++)
        {
            w.Player.Speed = speed;
            w.Player.Lateral = lateral;
            w.Player.Armor = 100;
            w.StepOnce(input ?? ControlFrame.Empty);
            each?.Invoke();
        }
    }

    static bool Touching(Vehicle a, Vehicle b) =>
        Math.Abs(a.Distance - b.Distance) < (a.Stats.Length + b.Stats.Length) / 2
        && Math.Abs(a.Lateral - b.Lateral) < (a.Stats.Width + b.Stats.Width) / 2;

    [Fact]
    public void Civilian_moves_aside_for_a_fast_car_behind()
    {
        var w = Straight();
        w.Player.Distance = 0;
        var civ = Civilian(w, 60, 0, 30);
        bool contact = false;
        Run(w, 3 * 120, 50, 0, each: () => contact |= Touching(w.Player, civ));
        Assert.True(Math.Abs(civ.Lateral) >= 3.1, $"lateral {civ.Lateral}");
        Assert.False(contact);
    }

    [Fact]
    public void Civilian_swerves_away_from_the_threat()
    {
        var w = Straight();
        var civ = Civilian(w, 60, 0, 30);
        Run(w, 3 * 120, 50, -1);
        Assert.True(civ.Lateral > 4.5, $"lateral {civ.Lateral}");
    }

    [Fact]
    public void Civilian_swerves_out_of_the_gun_line()
    {
        var w = Straight();
        var civ = Civilian(w, 60, 0, 30, armor: 1000);   // armor 4 would be shot away before it could swerve
        Run(w, (int)(1.5 * Hz), 30, 0, new ControlFrame { Fire = true });
        Assert.True(Math.Abs(civ.Lateral) >= 1.8, $"lateral {civ.Lateral}");
        Assert.True(civ.Speed < 0.75 * 30, $"speed {civ.Speed}");
    }

    [Fact]
    public void Civilians_pull_over_in_a_pursuit()
    {
        var w = Straight();
        var civ = Civilian(w, 0, 0, 30);
        Still(w, AgentKind.PoliceCruiser, -100, 5, 30);
        w.Player.Distance = -300;
        int steps = 5 * 120;
        double widest = 0;
        Run(w, steps, 0, 0, each: () => { w.Heat.Set(75); widest = Math.Max(widest, Math.Abs(civ.Lateral)); });
        Assert.Equal(VehicleState.Driving, civ.State);
        Assert.True(widest <= 8.5, $"overshot to {widest}");
        Assert.True(Math.Abs(civ.Lateral) >= 7.5, $"lateral {civ.Lateral}");
        Assert.True(civ.Speed <= 12.5, $"speed {civ.Speed}");
    }

    [Fact]
    public void A_pulled_over_civilian_clears_the_outer_lane()
    {
        var w = Straight();
        var civ = Civilian(w, 0, 0, 30);
        Still(w, AgentKind.PoliceCruiser, -100, 5, 30);
        w.Player.Distance = -250;
        bool contact = false;
        Run(w, 8 * 120, 50, 5, each: () => { w.Heat.Set(75); contact |= Touching(w.Player, civ); });
        Assert.True(w.Player.Distance > civ.Distance, "the player should have passed");
        Assert.False(contact);
    }

    [Fact]
    public void Civilian_is_not_blinded_by_smoke_but_slows()
    {
        var w = Straight();
        w.Player.Distance = -300;
        var civ = Civilian(w, 100, 0, 20);
        w.Hazards.Add(new SmokeCloud { Distance = 110, PrevDistance = 110, Lateral = 0, Speed = 0 });
        double worst = 0;
        Run(w, 120, 0, 0, each: () => worst = Math.Max(worst, Math.Max(civ.Blinded, Math.Abs(civ.Lateral))));
        Assert.Equal(0, civ.Blinded);
        Assert.DoesNotContain(w.Events.OfType<Message>(), m => m.Text.Contains("LOST YOU"));
        Assert.True(civ.Speed <= 0.6 * 20 + 0.3, $"speed {civ.Speed}");
        Assert.True(worst < 0.3, $"drifted {worst}");
    }

    [Fact]
    public void A_hit_civilian_slows_and_edges_over()
    {
        var w = Straight();
        w.Player.Distance = -300;
        var civ = Civilian(w, 100, 1, 30);
        civ.HitFlash = 0.1;
        Run(w, 2 * 120, 0, 0);
        Assert.True(civ.Speed <= 0.5 * 30 + 0.5, $"speed {civ.Speed}");
        Assert.True(civ.Lateral > 3, $"lateral {civ.Lateral}");
    }

    [Fact]
    public void Civilian_holds_lane_when_both_neighbours_are_taken()
    {
        var w = Straight();
        var civ = Civilian(w, 100, 0, 30);
        Still(w, AgentKind.Runner, 100, -5, 30);
        Still(w, AgentKind.Runner, 100, 5, 30);
        w.Player.Distance = 71;
        Run(w, 1, 40, 0);                 // the threat starts the swerve, then drops back so nothing rear-ends the slowed car
        Run(w, 239, 20, 0);
        Assert.True(Math.Abs(civ.Lateral) < 0.3, $"lateral {civ.Lateral}");
        Assert.True(civ.Speed <= 0.6 * 30 + 0.3, $"speed {civ.Speed}");
        Assert.True(((CivilianDriver.State)civ.DriverState!).SwerveHeld);
    }

    [Theory]
    [InlineData(0)]     // alongside in the lane the civilian would cross
    [InlineData(-10)]   // just behind in it, closing fast
    public void A_civilian_never_pulls_over_across_the_player(double playerOffset)
    {
        var w = Straight();
        var civ = Civilian(w, 100, 0, 30);          // lane 0, Side +1: it would prefer the +side shoulder
        Still(w, AgentKind.PoliceCruiser, 0, 5, 30);
        w.Player.Distance = 100 + playerOffset;
        bool contact = false;
        double widest = 0;
        Run(w, 3 * 120, playerOffset == 0 ? 30 : 50, 5, each: () =>
        {
            w.Heat.Set(75);
            contact |= Touching(w.Player, civ);
            widest = Math.Max(widest, civ.Lateral);
        });
        Assert.False(contact);
        Assert.True(widest <= 1.0, $"crossed toward the player: {widest}");
    }

    [Fact]
    public void FreeNeighbourLane_prefers_a_side_and_reports_none()
    {
        var w = Straight();
        var civ = Civilian(w, 100, 0, 30);
        var view = new WorldView(w);
        Assert.Equal(5, DriverHelpers.FreeNeighbourLane(civ, view, 15, 40, 1));
        Assert.Equal(-5, DriverHelpers.FreeNeighbourLane(civ, view, 15, 40, -1));
        Still(w, AgentKind.Runner, 90, 5, 30);                       // 10 m behind in +5
        Assert.Equal(-5, DriverHelpers.FreeNeighbourLane(civ, view, 15, 40, 1));
        Assert.Equal(5, DriverHelpers.FreeNeighbourLane(civ, view, 5, 40, 1));   // outside a 5 m window
        w.Hazards.Add(new Wreck { Distance = 120, Lateral = -5 });
        Assert.Equal(5, DriverHelpers.FreeNeighbourLane(civ, view, 5, 40, -1));
        Still(w, AgentKind.Runner, 100, 5, 30);
        Assert.Null(DriverHelpers.FreeNeighbourLane(civ, view, 5, 40, 1));
    }
}
