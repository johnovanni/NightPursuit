using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class SupplyTests
{
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static SupplyCrate Crate(World w, SupplyItem item, int amount, double ahead = 2, double lateral = 0)
    {
        var c = new SupplyCrate(item, amount) { Distance = w.Player.Distance + ahead, Lateral = lateral };
        w.Hazards.Add(c);
        return c;
    }

    static void Step(World w) { w.Player.Speed = 0; w.StepOnce(ControlFrame.Empty); }

    [Fact]
    public void Driving_through_a_crate_restocks_and_removes_it()
    {
        var w = Straight();
        Assert.Equal(1, w.Loadout.Missiles.Charges);
        var c = Crate(w, SupplyItem.Missile, 1);
        Step(w);
        Assert.Equal(2, w.Loadout.Missiles.Charges);
        Assert.DoesNotContain(c, w.Hazards);
        Assert.Contains(w.Events, e => e is SupplyCollected { Item: SupplyItem.Missile, Added: 1 });
        Assert.Contains(w.Events, e => e is Message { Text: "SUPPLY: +1 MISSILE", Tone: Tone.Good });
    }

    [Fact]
    public void Full_weapon_crate_is_used_up_and_says_so()
    {
        var w = Straight();
        var c = Crate(w, SupplyItem.Oil, 1);
        Step(w);
        Assert.Equal(5, w.Loadout.Oil.Charges);
        Assert.DoesNotContain(c, w.Hazards);
        Assert.Contains(w.Events, e => e is Message { Text: "SUPPLY: OIL ALREADY FULL", Tone: Tone.Info });
    }

    [Fact]
    public void Armor_crate_repairs_25_each_up_to_100()
    {
        var w = Straight();
        w.Player.Armor = 60;
        Crate(w, SupplyItem.Armor, 2);
        Step(w);
        Assert.Equal(100, w.Player.Armor);
        Assert.Contains(w.Events, e => e is SupplyCollected { Item: SupplyItem.Armor, Added: 40 });
        Assert.Contains(w.Events, e => e is Message { Text: "SUPPLY: +40 ARMOR" });
    }

    [Fact]
    public void Agents_and_bullets_pass_through_crates()
    {
        var w = Straight();
        var c = Crate(w, SupplyItem.Smoke, 1, ahead: 60);
        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, w.Player.Distance + 60, 0, 0);
        runner.Driver = new StillDriver();
        w.Agents.Add(runner);
        w.Tracers.Add(new Tracer { Pose = new RoadPose(c.Distance - 3, 0, 0), Speed = 200, OwnerId = w.Player.Id });
        for (int i = 0; i < 30; i++) Step(w);
        Assert.Contains(c, w.Hazards);
        Assert.False(c.Expired);
        Assert.Equal(2, w.Loadout.Smoke.Charges);
    }

    [Fact]
    public void Crate_label_names_item_and_amount()
    {
        Assert.Equal("OIL +2", new SupplyCrate(SupplyItem.Oil, 2).Label);
        Assert.Equal("CALTROPS +1", new SupplyCrate(SupplyItem.Caltrops, 1).Label);
        Assert.Equal("MISSILE +1", new SupplyCrate(SupplyItem.Missile, 1).Label);
        Assert.Equal("SMOKE +1", new SupplyCrate(SupplyItem.Smoke, 1).Label);
        Assert.Equal("STRIKE +1", new SupplyCrate(SupplyItem.Strike, 1).Label);
        Assert.Equal("ARMOR +25", new SupplyCrate(SupplyItem.Armor, 1).Label);
        Assert.Equal("ARMOR +50", new SupplyCrate(SupplyItem.Armor, 2).Label);
    }

    [Fact]
    public void Crate_expires_600_m_behind_and_armor_is_not_a_weapon_restock()
    {
        var w = Straight();
        var c = new SupplyCrate(SupplyItem.Oil, 1) { Distance = 100 };
        c.Update(0.01, 700);
        Assert.False(c.Expired);
        c.Update(0.01, 701);
        Assert.True(c.Expired);
        Assert.Equal(0, w.Loadout.Restock(SupplyItem.Armor, 3));
    }

    // ---- loader

    const string Valid = """
        { "id": "m", "title": "T", "briefing": "B", "road": { "seed": 7 }, "approachLength": 1000, "startingOil": 5,
          "waves": [ { "atDistance": 100, "kind": "Runner", "count": 2, "spawnOffset": 400 } ] }
        """;

    static string With(string json, string section) => json.Replace("\"waves\"", section + ", \"waves\"");

    [Fact]
    public void Loadout_section_loads()
    {
        var json = With(Valid.Replace("\"startingOil\": 5,", ""),
            "\"loadout\": { \"oil\": 3, \"smoke\": 1, \"caltrops\": 0, \"missiles\": 2, \"strikes\": 1 }");
        var def = MissionLoader.Load(json, "m.json");
        Assert.Equal(new LoadoutDefinition(3, 1, 0, 2, 1), def.Loadout);
        Assert.Equal(3, def.StartingOil);
    }

    [Fact]
    public void Missing_loadout_uses_starting_oil_and_defaults()
    {
        var def = MissionLoader.Load(Valid, "m.json");
        Assert.Equal(LoadoutDefinition.Default with { Oil = 5 }, def.Loadout);
        Assert.Empty(def.Crates ?? []);
    }

    [Fact]
    public void Crates_load()
    {
        var json = With(Valid, "\"crates\": [ { \"atDistance\": 0, \"lane\": -1, \"item\": \"armor\", \"amount\": 2 }, { \"atDistance\": 50, \"lane\": 1, \"item\": \"Oil\", \"amount\": 1 } ]");
        var def = MissionLoader.Load(json, "m.json");
        Assert.Equal(new CrateDefinition(0, -1, SupplyItem.Armor, 2), def.Crates![0]);
        Assert.Equal(new CrateDefinition(50, 1, SupplyItem.Oil, 1), def.Crates[1]);
    }

    [Theory]
    [InlineData("\"loadout\": { \"smoke\": 4 }", "loadout.smoke must be from 0 to 3 (got 4)", true)]
    [InlineData("\"loadout\": { \"missles\": 1 }", "loadout.missles is not a known weapon", true)]
    [InlineData("\"crates\": [ { \"atDistance\": 100, \"lane\": 2, \"item\": \"Oil\", \"amount\": 1 } ]", "crates[0].lane must be -1, 0 or 1", false)]
    [InlineData("\"crates\": [ { \"atDistance\": 100, \"lane\": 0, \"item\": \"Rocket\", \"amount\": 1 } ]", "crates[0].item 'Rocket' is not a known supply item", false)]
    [InlineData("\"crates\": [ { \"atDistance\": 100, \"lane\": 0, \"item\": \"Oil\", \"amount\": 0 } ]", "crates[0].amount must be from 1 to 3 (got 0)", false)]
    public void Bad_loadout_and_crates_name_the_field(string section, string message, bool dropStartingOil)
    {
        var baseJson = dropStartingOil ? Valid.Replace("\"startingOil\": 5,", "") : Valid;
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(With(baseJson, section), "m.json"));
        Assert.Contains("m.json", ex.Message);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Crates_out_of_order_are_rejected()
    {
        var json = With(Valid, "\"crates\": [ { \"atDistance\": 100, \"lane\": 0, \"item\": \"Oil\", \"amount\": 1 }, { \"atDistance\": 50, \"lane\": 0, \"item\": \"Oil\", \"amount\": 1 } ]");
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(json, "m.json"));
        Assert.StartsWith("m.json: crates[1].atDistance", ex.Message);
    }

    [Fact]
    public void Starting_oil_and_loadout_together_are_rejected()
    {
        var ex = Assert.Throws<MissionFormatException>(() => MissionLoader.Load(With(Valid, "\"loadout\": { \"oil\": 3 }"), "m.json"));
        Assert.Contains("use loadout.oil instead of startingOil", ex.Message);
    }

    // ---- director and run

    static MissionDefinition Def(params CrateDefinition[] crates) =>
        new("t", "T", "B", new TrackProfile(7), 6000, 5, [], null, crates);

    [Fact]
    public void Crate_appears_800_m_before_its_distance_once()
    {
        var w = Straight();
        var dir = new MissionDirector(Def(new CrateDefinition(2000, 1, SupplyItem.Missile, 1)));
        w.Player.Distance = 1199;
        dir.Update(w);
        Assert.DoesNotContain(w.Hazards, h => h is SupplyCrate);
        w.Player.Distance = 1200;
        dir.Update(w);
        var c = Assert.Single(w.Hazards.OfType<SupplyCrate>());
        Assert.Equal(2000, c.Distance);
        Assert.Equal(5, c.Lateral);
        Assert.Equal(2000, c.PrevDistance);
        dir.Update(w);
        w.Player.Distance = 1500;
        dir.Update(w);
        Assert.Single(w.Hazards.OfType<SupplyCrate>());
    }

    [Fact]
    public void Mission_loadout_sets_every_weapon()
    {
        var def = Def() with { StartingLoadout = new LoadoutDefinition(5, 1, 3, 2, 1) };
        var run = new MissionRun(def, new FeelSettings(), 3);
        var l = run.World.Loadout;
        Assert.Equal([5, 1, 3, 2, 1], new[] { l.Oil.Charges, l.Smoke.Charges, l.Caltrops.Charges, l.Missiles.Charges, l.Strike.Charges });
    }
}
