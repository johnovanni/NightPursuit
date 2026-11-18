using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class OilTests
{
    const double Dt = 1.0 / 120;
    static readonly GameAction Drop = new(ActionKind.DropRear);

    [Fact]
    public void Oil_recharges_one_charge_per_8_s()
    {
        var oil = new OilDropper(3);
        for (int i = 0; i < 120 * 7; i++) oil.Update(Dt);
        Assert.Equal(3, oil.Charges);
        for (int i = 0; i < 120 * 1 + 2; i++) oil.Update(Dt);
        Assert.Equal(4, oil.Charges);
    }

    [Fact]
    public void Oil_never_exceeds_5_charges()
    {
        var oil = new OilDropper();
        for (int i = 0; i < 120 * 30; i++) oil.Update(Dt);
        Assert.Equal(5, oil.Charges);
    }

    [Fact]
    public void Drop_has_a_0_6_s_cooldown_and_uses_a_charge()
    {
        var oil = new OilDropper();
        Assert.True(oil.TryDrop());
        Assert.Equal(4, oil.Charges);
        Assert.False(oil.TryDrop());
        for (int i = 0; i < 70; i++) oil.Update(Dt);
        Assert.False(oil.TryDrop());
        for (int i = 0; i < 4; i++) oil.Update(Dt);
        Assert.True(oil.TryDrop());
    }

    [Fact]
    public void Drop_places_slick_5_m_behind_and_publishes_message()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Distance = 100; w.Player.Lateral = 2;
        w.Handle(Drop);
        var slick = Assert.IsType<OilSlick>(Assert.Single(w.Hazards));
        Assert.Equal(95, slick.Distance);
        Assert.Equal(2, slick.Lateral);
        Assert.Contains(w.Events, e => e is Message { Text: "OIL SLICK DROPPED", Tone: Tone.Info });
    }

    [Fact]
    public void Drop_with_no_charges_says_so()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        for (int n = 0; n < 5; n++)
        {
            w.Handle(Drop);
            for (int i = 0; i < 80; i++) w.StepOnce(ControlFrame.Empty);   // past the cooldown, short of a recharge
        }
        Assert.Equal(0, w.Loadout.Oil.Charges);
        int slicks = w.Hazards.Count;
        w.Handle(Drop);
        Assert.Equal(slicks, w.Hazards.Count);
        Assert.Contains(w.Events, e => e is Message { Text: "NO OIL LEFT (recharging)", Tone: Tone.Bad });
    }

    [Fact]
    public void Drop_during_cooldown_says_it_is_not_ready()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Handle(Drop);
        int slicks = w.Hazards.Count;
        w.StepOnce(ControlFrame.Empty);
        w.Handle(Drop);
        Assert.Equal(slicks, w.Hazards.Count);
        Assert.Contains(w.Events, e => e is Message { Text: "OIL NOT READY", Tone: Tone.Info });
    }

    [Fact]
    public void Wrecked_player_cannot_drop_oil()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.State = VehicleState.Wrecked;
        w.Handle(Drop);
        Assert.Empty(w.Hazards);
        Assert.Equal(5, w.Loadout.Oil.Charges);
    }

    [Fact]
    public void Slick_expires_after_its_life_or_600_m_behind()
    {
        var a = new OilSlick { Distance = 0 };
        a.Update(24.9, 0);
        Assert.False(a.Expired);
        a.Update(0.2, 0);
        Assert.True(a.Expired);
        var b = new OilSlick { Distance = 0 };
        b.Update(Dt, 601);
        Assert.True(b.Expired);
    }

    [Fact]
    public void Oil_in_front_of_pursuer_makes_it_spin()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 30;
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -20, 0, 40);   // 10 m/s faster, same lane, behind
        w.Agents.Add(car);
        w.Handle(Drop);
        bool spun = false;
        var events = new List<GameEvent>();
        for (int i = 0; i < 120 * 3 && !spun; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            events.AddRange(w.Events);
            spun = car.State == VehicleState.Spinning;
        }
        Assert.True(spun);
        Assert.Contains(events, e => e is VehicleSpun v && v.VehicleId == car.Id);
    }
}
