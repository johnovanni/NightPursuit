using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class StrikeTests
{
    static World Straight(int strikes = 1)
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 0;
        w.Loadout.Strike.Restock(strikes);
        return w;
    }

    static AgentCar Car(World w, double distance, double lateral, AgentKind kind = AgentKind.Runner)
    {
        var car = AgentCar.Create(w.NextId(), kind, distance, lateral, 0);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    static int Count(World w, string text) => w.Events.OfType<Message>().Count(m => m.Text == text);

    static void Run(World w, int steps) { for (int i = 0; i < steps; i++) w.StepOnce(ControlFrame.Empty); }

    [Fact]
    public void Strike_lands_after_1_5_s_on_hostiles_from_150_behind_to_400_ahead()
    {
        var w = Straight();
        double[] ds = { -151, -149, 20, 399, 401 };
        double[] ls = { -5, 0, 5, 0, 5 };
        var cars = ds.Select((d, i) => Car(w, d, ls[i])).ToArray();
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.Equal(1, Count(w, "SATELLITE STRIKE: TARGETING"));
        Assert.Contains(w.Events, e => e is WeaponUsed { Weapon: WeaponKind.Strike, OwnerId: 0 });

        Run(w, 179);   // 1.49 s
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
        Run(w, 2);     // 1.51 s
        Assert.Equal(3, w.Events.Count(e => e is VehicleDestroyed { Cause: KillCause.Satellite }));
        var wrecks = w.Hazards.OfType<Wreck>().OrderBy(x => x.Distance).ToArray();
        Assert.Equal(3, wrecks.Length);
        Assert.Equal(new[] { -149.0, 20, 399 }, wrecks.Select(x => x.Distance).ToArray());
        Assert.Equal(new[] { 0.0, 5, 0 }, wrecks.Select(x => x.Lateral).ToArray());
        Assert.Equal(VehicleState.Driving, cars[0].State);
        Assert.Equal(VehicleState.Driving, cars[4].State);
        Assert.Contains(w.Events, e => e is StrikeLanded { Destroyed: 3 });
        Assert.Equal(1, Count(w, "SATELLITE STRIKE: 3 DESTROYED"));
    }

    [Fact]
    public void Strike_with_no_targets_is_kept()
    {
        var w = Straight();
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.Equal(1, Count(w, "NO TARGETS IN STRIKE RANGE"));
        Assert.Equal(1, w.Loadout.Strike.Charges);
        Assert.False(w.Loadout.Strike.Targeting);
    }

    [Fact]
    public void Strike_with_none_held_says_so()
    {
        var w = Straight(0);
        Car(w, 50, 0);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.Equal(1, Count(w, "NO SATELLITE STRIKE"));
        Assert.False(w.Loadout.Strike.Targeting);
    }

    [Fact]
    public void Second_press_during_sweep_does_nothing()
    {
        var w = Straight();
        Car(w, 50, 0);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Run(w, 60);
        double left = w.Loadout.Strike.SweepLeft;
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.Equal(1, Count(w, "STRIKE ALREADY TARGETING"));
        Assert.Equal(left, w.Loadout.Strike.SweepLeft);
        Run(w, 130);
        Assert.Equal(1, w.Events.Count(e => e is StrikeLanded));
    }

    [Fact]
    public void Strike_lands_even_if_the_player_is_wrecked_during_the_sweep()
    {
        var w = Straight();
        var car = Car(w, 50, 0);
        w.Handle(new GameAction(ActionKind.FireStrike));
        w.Player.Armor = 0;
        Run(w, 1);
        Assert.Equal(VehicleState.Wrecked, w.Player.State);
        Run(w, 185);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Cause: KillCause.Satellite });
        Assert.Equal(VehicleState.Wrecked, car.State);
    }

    [Fact]
    public void Wrecked_player_cannot_begin_a_strike()
    {
        var w = Straight();
        Car(w, 50, 0);
        w.Player.Armor = 0;
        Run(w, 1);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.Equal(1, w.Loadout.Strike.Charges);
    }

    [Fact]
    public void Only_one_strike_can_be_held()
    {
        var a = new Loadout(new LoadoutDefinition(Strikes: 1));
        Assert.Equal(0, a.Strike.Restock(2));
        var b = new Loadout(new LoadoutDefinition(Strikes: 0));
        Assert.Equal(1, b.Strike.Restock(2));
    }

    [Fact]
    public void Sweep_returns_true_exactly_once_on_the_step_it_ends()
    {
        var s = new SatelliteStrike(1);
        Assert.False(s.AdvanceSweep(World.Step));   // not targeting
        Assert.True(s.TryBegin());
        int trues = 0, endedOn = 0;
        for (int i = 1; i <= 400; i++)
            if (s.AdvanceSweep(World.Step)) { trues++; endedOn = i; }
        Assert.Equal(1, trues);
        Assert.Equal(180, endedOn);   // 1.5 s / (1/120 s), with the 1e-9 snap
        Assert.False(s.Targeting);
        Assert.Equal(0, s.SweepLeft);
    }
}
