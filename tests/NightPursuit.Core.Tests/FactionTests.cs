using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class FactionTests
{
    static World Straight()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 0;
        w.Player.SnapPrev();
        return w;
    }

    static AgentCar Car(World w, AgentKind kind, double distance, double lateral, double speed = 0, bool still = true)
    {
        var car = AgentCar.Create(w.NextId(), kind, distance, lateral, speed);
        if (still) car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    static void Run(World w, int steps) { for (int i = 0; i < steps; i++) w.StepOnce(ControlFrame.Empty); }

    [Fact]
    public void New_kinds_have_their_faction()
    {
        foreach (var kind in new[] { AgentKind.PolicePatrol, AgentKind.PoliceCruiser, AgentKind.Interceptor, AgentKind.ArmoredSuv })
        {
            var car = AgentCar.Create(1, kind, 100, 0, 30);
            Assert.Equal(Faction.Police, car.Faction);
            Assert.True(KindRules.IsPolice(kind));
        }
        var civilian = AgentCar.Create(2, AgentKind.Civilian, 100, 0, 30);
        Assert.Equal(Faction.Civilian, civilian.Faction);
        Assert.False(KindRules.IsPolice(AgentKind.Civilian));
        Assert.Equal(Faction.Hostile, AgentCar.Create(3, AgentKind.Runner, 100, 0, 30).Faction);
        Assert.Equal(Faction.Hostile, KindRules.FactionOf(AgentKind.Boss));
        Assert.Same(VehicleStats.ArmoredSuv, AgentCar.Create(4, AgentKind.ArmoredSuv, 100, 0, 30).Stats);
        Assert.Equal(82, VehicleStats.ArmoredSuv.TopSpeed);   // F1
        Assert.Same(CivilianDriver.Instance, AgentCar.DriverFor(AgentKind.Civilian));
        Assert.Same(PatrolDriver.Instance, AgentCar.DriverFor(AgentKind.Interceptor));
    }

    [Fact]
    public void New_kinds_come_after_boss()
    {
        Assert.Equal((int)AgentKind.Boss + 1, (int)AgentKind.Civilian);
        Assert.Equal((int)AgentKind.Civilian + 1, (int)AgentKind.PolicePatrol);
        Assert.Equal((int)AgentKind.Boss + 5, (int)AgentKind.ArmoredSuv);
    }

    [Fact]
    public void Display_names_and_reaction_delays()
    {
        Assert.Equal("CIVILIAN", KindRules.DisplayName(AgentKind.Civilian));
        Assert.Equal("POLICE", KindRules.DisplayName(AgentKind.PolicePatrol));
        Assert.Equal("POLICE", KindRules.DisplayName(AgentKind.PoliceCruiser));
        Assert.Equal("INTERCEPTOR", KindRules.DisplayName(AgentKind.Interceptor));
        Assert.Equal("POLICE SUV", KindRules.DisplayName(AgentKind.ArmoredSuv));
        Assert.Equal("RUNNER", KindRules.DisplayName(AgentKind.Runner));
        Assert.Equal(0.7, DriverHelpers.ReactionDelay(AgentKind.Civilian));
        Assert.Equal(0.5, DriverHelpers.ReactionDelay(AgentKind.PolicePatrol));
        Assert.Equal(0.4, DriverHelpers.ReactionDelay(AgentKind.PoliceCruiser));
        Assert.Equal(0.3, DriverHelpers.ReactionDelay(AgentKind.Interceptor));
        Assert.Equal(0.5, DriverHelpers.ReactionDelay(AgentKind.ArmoredSuv));
    }

    [Fact]
    public void Civilian_keeps_its_lane_at_cruise_speed()
    {
        var w = Straight();
        var car = Car(w, AgentKind.Civilian, 100, 5, 30, still: false);
        for (int i = 0; i < 20 * 120; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            if (i >= 120) Assert.InRange(Math.Abs(car.Lateral - 5), 0, 0.05);
        }
        Assert.InRange(car.Speed, 29.8, 30.2);
    }

    [Fact]
    public void Civilian_steers_round_a_wreck_in_its_lane()
    {
        var w = Straight();
        var car = Car(w, AgentKind.Civilian, 100, 5, 30, still: false);
        w.Hazards.Add(new Wreck { Distance = 250, Lateral = 5, Width = 1.9, Length = 4.4, PrevDistance = 250 });
        Run(w, 15 * 120);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed { Cause: KillCause.WreckCollision });
        Assert.Equal(VehicleState.Driving, car.State);
        Assert.True(car.Distance > 270, "it should have driven past the wreck");
    }

    [Fact]
    public void Civilian_never_runs_into_a_stopped_player()
    {
        foreach (bool blocked in new[] { false, true })
        {
            var w = Straight();
            w.Player.Distance = 300; w.Player.SnapPrev();
            var car = Car(w, AgentKind.Civilian, 100, 0, 30, still: false);
            if (blocked)
            {
                Car(w, AgentKind.Runner, 295, -5);
                Car(w, AgentKind.Runner, 295, 5);
            }
            for (int i = 0; i < 20 * 120; i++)
            {
                w.StepOnce(ControlFrame.Empty);
                Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
                Assert.True(car.Speed >= 0, "it never reverses");
            }
            if (blocked)
            {
                Assert.True(car.Distance < w.Player.Distance, "it stops behind the player");
                Assert.InRange(car.Speed, 0, 0.5);
                Assert.InRange(Math.Abs(car.Lateral), 0, 0.3);
            }
            else
                Assert.True(car.Distance > w.Player.Distance, "with a free lane it drives round the player");
        }
    }

    [Fact]
    public void Strike_spares_civilians_and_police()
    {
        var w = Straight();
        w.Loadout.Strike.Restock(1);
        var civilian = Car(w, AgentKind.Civilian, 100, -5);
        var police = Car(w, AgentKind.PolicePatrol, 100, 0);
        var runner = Car(w, AgentKind.Runner, 100, 5);
        w.BeginStrike();
        Run(w, (int)(1.6 * 120));
        Assert.Equal(VehicleState.Wrecked, runner.State);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.Satellite });
        Assert.Equal(VehicleState.Driving, civilian.State);
        Assert.Equal(VehicleState.Driving, police.State);
    }

    [Fact]
    public void Missile_never_locks_or_hits_civilians_or_police()
    {
        var w = Straight();
        w.Loadout.Missiles.Restock(1);
        var civilian = Car(w, AgentKind.Civilian, 100, 0);
        var police = Car(w, AgentKind.PoliceCruiser, 160, 0);
        Assert.Null(w.FindMissileTarget());
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Single(w.Missiles);
        Run(w, 5 * 120);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
        Assert.Equal(VehicleState.Driving, civilian.State);
        Assert.Equal(VehicleState.Driving, police.State);
    }

    [Fact]
    public void A_fast_civilian_behind_raises_no_warning()
    {
        var w = Straight();
        w.Player.Distance = 500; w.Player.Speed = 30; w.Player.SnapPrev();
        Car(w, AgentKind.Civilian, 480, 0, 40);
        Assert.DoesNotContain(Warnings.Collect(w), x => x.Kind == WarningKind.HostileBehind);
        // sanity: the same car as a hostile does raise it
        w.Agents.Clear();
        Car(w, AgentKind.Runner, 480, 0, 40);
        Assert.Contains(Warnings.Collect(w), x => x.Kind == WarningKind.HostileBehind);
    }
}
