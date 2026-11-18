using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class RearWeaponTests
{
    const double Dt = 1.0 / 120;
    static readonly GameAction Drop = new(ActionKind.DropRear);
    static readonly GameAction Cycle = new(ActionKind.CycleRear);

    static World W() => TestWorlds.World(track: TestWorlds.Straight());

    static string? LastText(World w) => w.Events.OfType<Message>().LastOrDefault()?.Text;

    [Fact]
    public void Only_oil_recharges()
    {
        var l = new Loadout(new LoadoutDefinition(4, 1, 1, 1, 1));
        l.Smoke.TryUse(); l.Caltrops.TryUse(); l.Missiles.TryUse();
        for (int i = 0; i < 120 * 60; i++) l.Update(Dt);
        Assert.Equal(5, l.Oil.Charges);
        Assert.Equal(0, l.Smoke.Charges);
        Assert.Equal(0, l.Caltrops.Charges);
        Assert.Equal(0, l.Missiles.Charges);
        Assert.Equal(1, l.Strike.Charges);
    }

    [Fact]
    public void Capacities_are_5_3_3_2_1_and_restock_reports_what_fits()
    {
        var l = new Loadout(new LoadoutDefinition(5, 2, 3, 1, 0));
        Assert.Equal(new[] { 5, 3, 3, 2, 1 }, new[] { l.Oil.Capacity, l.Smoke.Capacity, l.Caltrops.Capacity, l.Missiles.Capacity, l.Strike.Capacity });
        Assert.Equal(1, l.Smoke.Restock(3));
        Assert.Equal(3, l.Smoke.Charges);
        Assert.Equal(0, l.Caltrops.Restock(1));
    }

    [Fact]
    public void Starting_charges_are_clamped()
    {
        Assert.Equal(3, new SmokeLauncher(9).Charges);
        Assert.Equal(0, new MissileLauncher(-1).Charges);
    }

    [Fact]
    public void CycleRear_goes_oil_smoke_caltrops_and_back_and_says_so()
    {
        var w = W();
        w.Handle(Cycle);
        Assert.Equal(RearWeapon.Smoke, w.Loadout.SelectedRear);
        Assert.Equal("REAR WEAPON: SMOKE SCREEN (2 LEFT)", LastText(w));
        w.Handle(Cycle);
        Assert.Equal(RearWeapon.Caltrops, w.Loadout.SelectedRear);
        w.Handle(Cycle);
        Assert.Equal(RearWeapon.Oil, w.Loadout.SelectedRear);
    }

    [Fact]
    public void Smoke_drops_6_m_behind_drifting_at_half_speed()
    {
        var w = W();
        w.Player.Distance = 100; w.Player.Lateral = 1.5; w.Player.Speed = 40;
        w.Loadout.CycleRear();
        w.Handle(Drop);
        var cloud = Assert.IsType<SmokeCloud>(Assert.Single(w.Hazards));
        Assert.Equal(94, cloud.Distance, 1e-9);
        Assert.Equal(94, cloud.PrevDistance, 1e-9);
        Assert.Equal(1.5, cloud.Lateral, 1e-9);
        Assert.Equal(20, cloud.Speed, 1e-9);
        Assert.False(cloud.Hostile);
        Assert.Equal("SMOKE SCREEN", LastText(w));
        var used = Assert.Single(w.Events.OfType<WeaponUsed>());
        Assert.Equal(WeaponKind.Smoke, used.Weapon);
        Assert.Equal(0, used.OwnerId);
    }

    [Fact]
    public void Caltrops_drop_5_m_behind_on_the_nearest_lane()
    {
        var w = W();
        w.Player.Distance = 100; w.Player.Lateral = 3.6;
        w.Loadout.CycleRear(); w.Loadout.CycleRear();
        w.Handle(Drop);
        var c = Assert.IsType<Caltrops>(Assert.Single(w.Hazards));
        Assert.Equal(95, c.Distance, 1e-9);
        Assert.Equal(5, c.Lateral, 1e-9);
        Assert.Equal("CALTROPS DROPPED", LastText(w));
        Assert.Equal(WeaponKind.Caltrops, Assert.Single(w.Events.OfType<WeaponUsed>()).Weapon);
    }

    [Fact]
    public void Empty_rear_weapon_says_so_and_drops_nothing()
    {
        var w = W();
        w.Loadout = new Loadout(new LoadoutDefinition(Smoke: 0));
        w.Loadout.CycleRear();
        w.Handle(Drop);
        Assert.Empty(w.Hazards);
        Assert.Equal("NO SMOKE LEFT", LastText(w));
        Assert.Empty(w.Events.OfType<WeaponUsed>());
    }

    [Fact]
    public void Rear_weapon_in_cooldown_says_not_ready()
    {
        var w = W();
        w.Loadout.CycleRear(); w.Loadout.CycleRear();
        w.Handle(Drop);
        w.Handle(Drop);
        Assert.Equal(1, w.Loadout.Caltrops.Charges);
        Assert.Single(w.Hazards);
        Assert.Equal("CALTROPS NOT READY", LastText(w));
    }

    [Fact]
    public void Wrecked_player_drops_nothing()
    {
        foreach (var weapon in Enum.GetValues<RearWeapon>())
        {
            var w = W();
            w.Player.State = VehicleState.Wrecked;
            while (w.Loadout.SelectedRear != weapon) w.Loadout.CycleRear();
            int before = w.Loadout.SelectedRearWeapon.Charges;
            w.Handle(Drop);
            Assert.Empty(w.Hazards);
            Assert.Equal(before, w.Loadout.SelectedRearWeapon.Charges);
            Assert.Empty(w.Events.OfType<Message>());
        }
    }

    [Fact]
    public void Smoke_cloud_drifts_slows_and_clears_after_5_s()
    {
        var c = new SmokeCloud { Distance = 0, Speed = 20 };
        for (int i = 0; i < 240; i++) c.Update(Dt, 0);
        Assert.Equal(0, c.Speed);
        Assert.Equal(20, c.Distance, 0.1);
        for (int i = 240; i < 599; i++) c.Update(Dt, 0);
        Assert.False(c.Expired);
        c.Update(Dt, 0);
        Assert.True(c.Expired);
    }
}
