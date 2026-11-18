using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class AgentPhysicsTests
{
    const double Dt = 1 / 120.0;

    [Fact]
    public void Create_picks_stats_by_kind()
    {
        var c = AgentCar.Create(7, AgentKind.Blocker, 100, -2, 20);
        Assert.Equal(7, c.Id);
        Assert.Same(VehicleStats.Blocker, c.Stats);
        Assert.Equal(Faction.Hostile, c.Faction);
        Assert.Equal(c.Stats.Armor, c.Armor);
        Assert.Equal(VehicleState.Driving, c.State);
        Assert.Equal(1, c.Side);
        Assert.Equal(100, c.Distance); Assert.Equal(-2, c.Lateral); Assert.Equal(20, c.Speed);
    }

    [Fact]
    public void Agent_reaches_target_lateral_within_2_seconds()
    {
        var car = AgentCar.Create(1, AgentKind.Runner, 0, 0, 30);
        var intent = new DriveIntent(30, 3);
        for (int i = 0; i < 240; i++) CarPhysics.StepAgent(car, intent, Dt);
        Assert.InRange(car.Lateral, 2.9, 3.1);
    }

    [Fact]
    public void Target_speed_is_capped_by_top_speed()
    {
        var car = AgentCar.Create(1, AgentKind.Blocker, 0, 0, 0);
        var intent = new DriveIntent(200, 0);
        for (int i = 0; i < 120 * 30; i++) CarPhysics.StepAgent(car, intent, Dt);
        Assert.Equal(VehicleStats.Blocker.TopSpeed, car.Speed, 6);
    }

    [Fact]
    public void Agent_slows_at_ten_and_advances()
    {
        var car = AgentCar.Create(1, AgentKind.Runner, 0, 0, 40);
        CarPhysics.StepAgent(car, new DriveIntent(0, 0), 1.0);
        Assert.Equal(30, car.Speed, 6);
        Assert.Equal(30, car.Distance, 6);
    }

    [Fact]
    public void Push_decays_and_adds_to_lateral_velocity()
    {
        var car = AgentCar.Create(1, AgentKind.Runner, 0, 0, 30);
        car.PushVel = 4;
        CarPhysics.StepAgent(car, new DriveIntent(30, 0), Dt);
        Assert.Equal(4, car.LateralVel, 6);
        Assert.Equal(4 * Math.Exp(-1.2 * Dt), car.PushVel, 9);
        Assert.True(car.Heading > 0);
    }

    [Fact]
    public void Spinning_car_slides_then_recovers_after_timer()
    {
        var car = AgentCar.Create(1, AgentKind.Runner, 0, 0, 30);
        car.State = VehicleState.Spinning; car.StateTimer = 1.0; car.SpinRate = 10; car.PushVel = 3;
        for (int i = 0; i < 60; i++) CarPhysics.StepSpinning(car, Dt);
        Assert.Equal(VehicleState.Spinning, car.State);
        Assert.True(car.Lateral > 1);
        Assert.True(car.SpinAngle > 4);
        Assert.True(car.Speed < 30);
        for (int i = 0; i < 70; i++) CarPhysics.StepSpinning(car, Dt);
        Assert.Equal(VehicleState.Driving, car.State);
        Assert.Equal(0, car.SpinAngle);
        Assert.Equal(0, car.PushVel);
    }

    [Fact]
    public void Wrecked_car_decelerates_at_25_and_stops()
    {
        var car = AgentCar.Create(1, AgentKind.Runner, 0, 0, 20);
        car.State = VehicleState.Wrecked; car.StateTimer = 5;
        for (int i = 0; i < 120; i++) CarPhysics.StepWrecked(car, Dt);
        Assert.Equal(0, car.Speed, 6);
        Assert.InRange(car.Distance, 7, 9);
        Assert.InRange(car.StateTimer, 3.99, 4.01);
    }
}
