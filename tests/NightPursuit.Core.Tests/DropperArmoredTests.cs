using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class DropperArmoredTests
{
    const double Dt = 1.0 / 120;
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static AgentCar Spawn(World w, AgentKind kind, double distance, double lateral, double speed)
    {
        var car = AgentCar.Create(w.NextId(), kind, distance, lateral, speed);
        w.Agents.Add(car);
        return car;
    }

    /// <summary>One step with the player held at <paramref name="speed"/>.</summary>
    static void Step(World w, double speed) { w.Player.Speed = speed; w.StepOnce(ControlFrame.Empty); }

    static double Half(Hazard h) => h is OilSlick ? OilSlick.Radius : Caltrops.HalfLength;

    /// <summary>
    /// Steps for <paramref name="seconds"/> and returns each hostile hazard at the step it appeared (with the player's
    /// state at that step, and how long the dropper had been arming). The hazards are then removed so the player is not disturbed.
    /// </summary>
    static List<(Hazard Hazard, double Gap, double PlayerSpeed, double Armed)> Drops(World w, AgentCar d, double speed, double seconds, int stopAfter = int.MaxValue)
    {
        var drops = new List<(Hazard, double, double, double)>();
        int armedSteps = 0;
        for (int i = 0; i < (int)(seconds * 120) && drops.Count < stopAfter; i++)
        {
            Step(w, speed);
            foreach (var h in w.Hazards.Where(h => h.Hostile).ToList())
            {
                double gap = (h.Distance - Half(h)) - (w.Player.Distance + VehicleStats.Player.Length / 2);
                drops.Add((h, gap, w.Player.Speed, armedSteps * Dt));
                w.Hazards.Remove(h);
            }
            armedSteps = DropperDriver.IsArming(d) ? armedSteps + 1 : 0;
        }
        return drops;
    }

    [Theory][InlineData(20)][InlineData(45)][InlineData(69)]
    public void Dropper_only_drops_where_the_player_has_1_3_s_to_react(double v)
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 120, 0, v);
        var drops = Drops(w, d, v, 40);
        Assert.True(drops.Count >= 2, $"only {drops.Count} drops");
        foreach (var (h, gap, _, _) in drops)
            Assert.True(gap >= Math.Max(60, 1.3 * v) - 1e-6, $"{h.GetType().Name}: gap {gap} at {v} m/s");
    }

    [Fact]
    public void Dropper_arms_for_0_5_s_before_each_drop()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 120, 0, 30);
        var drops = Drops(w, d, 30, 30);
        Assert.True(drops.Count >= 2);
        foreach (var drop in drops) Assert.True(drop.Armed >= 0.5 - 1e-9, $"armed {drop.Armed} s");
    }

    [Fact]
    public void Dropper_alternates_oil_and_caltrops_marked_hostile()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 120, 0, 30);
        var drops = Drops(w, d, 30, 60, stopAfter: 4);
        Assert.Equal(4, drops.Count);
        Assert.IsType<OilSlick>(drops[0].Hazard);
        Assert.IsType<Caltrops>(drops[1].Hazard);
        Assert.IsType<OilSlick>(drops[2].Hazard);
        Assert.IsType<Caltrops>(drops[3].Hazard);
        Assert.All(drops, x => Assert.True(x.Hazard.Hostile));
        foreach (var c in drops.Select(x => x.Hazard).OfType<Caltrops>())
            Assert.Contains(Track.LaneCentres, l => Math.Abs(l - c.Lateral) < 1e-9);
        var used = w.Events.OfType<WeaponUsed>().Where(e => e.OwnerId == d.Id).Select(e => e.Weapon).ToList();
        Assert.Equal(new[] { WeaponKind.Oil, WeaponKind.Caltrops, WeaponKind.Oil, WeaponKind.Caltrops }, used);
        Assert.Equal(2, w.Events.OfType<Message>().Count(m => m is { Text: "DROPPER: OIL AHEAD", Tone: Tone.Bad }));
        Assert.Equal(2, w.Events.OfType<Message>().Count(m => m is { Text: "DROPPER: CALTROPS AHEAD", Tone: Tone.Bad }));
    }

    [Fact]
    public void Dropper_hazard_sits_with_its_near_edge_1_5_m_behind_the_dropper()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 120, 0, 30);
        double rear = 0;
        for (int i = 0; i < 3000 && !w.Hazards.Any(h => h.Hostile); i++)
        {
            Step(w, 30);
            rear = d.Distance - d.Stats.Length / 2;
        }
        var oil = Assert.IsType<OilSlick>(Assert.Single(w.Hazards, h => h.Hostile));
        Assert.Equal(rear - 1.5 - OilSlick.Radius, oil.Distance, 9);
        Assert.Equal(d.Lateral, oil.Lateral, 9);
    }

    [Fact]
    public void Dropper_out_of_caltrops_drops_oil_instead()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 120, 0, 30);
        for (int i = 0; i < 3; i++) { Assert.True(d.Arms.Caltrops!.TryUse()); d.Arms.Caltrops.Update(1); }
        Assert.Equal(0, d.Arms.Caltrops!.Charges);
        d.Arms.NextDropIsOil = false;
        var drops = Drops(w, d, 30, 30, stopAfter: 1);
        Assert.IsType<OilSlick>(Assert.Single(drops).Hazard);
        Assert.Contains(w.Events, e => e is Message { Text: "DROPPER: OIL AHEAD" });
        Assert.Contains(w.Events, e => e is WeaponUsed { Weapon: WeaponKind.Oil } u && u.OwnerId == d.Id);
    }

    /// <summary>Asks to drop every step, whatever the weapons or the gap.</summary>
    sealed class AlwaysDropDriver : IDriver
    {
        public DriveIntent Decide(AgentCar self, WorldView view, double dt) => new(self.Speed, self.Lateral, DropRear: true);
    }

    [Fact]
    public void Dropper_with_nothing_to_drop_drops_and_publishes_nothing()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 200, 0, 30);
        d.Driver = new AlwaysDropDriver();
        for (int i = 0; i < 3; i++) { d.Arms.Caltrops!.TryUse(); d.Arms.Caltrops.Update(1); }
        for (int i = 0; i < 5; i++) { d.Arms.Oil!.TryUse(); d.Arms.Oil.Update(0.61); }
        for (int i = 0; i < 100; i++) Step(w, 30);        // oil recharges only every 8 s
        Assert.DoesNotContain(w.Hazards, h => h.Hostile);
        Assert.DoesNotContain(w.Events, e => e is WeaponUsed);
        Assert.DoesNotContain(w.Events, e => e is Message { Tone: Tone.Bad });
    }

    [Fact]
    public void Dropper_does_not_drop_where_the_player_has_too_little_time()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 40, 0, 30);   // only about 30 m from the player's bumper
        d.Driver = new AlwaysDropDriver();
        for (int i = 0; i < 10; i++) Step(w, 30);
        Assert.DoesNotContain(w.Hazards, h => h.Hostile);
        Assert.DoesNotContain(w.Events, e => e is WeaponUsed);
        Assert.Equal(5, d.Arms.Oil!.Charges);             // and no charge is used up
        Assert.True(d.Arms.LastDropSkipped);              // the driver is told
    }

    [Fact]
    public void Dropper_retries_within_about_1_s_after_a_skipped_drop()
    {
        var w = Straight();
        var d = Spawn(w, AgentKind.Dropper, 120, 0, 30);
        Step(w, 30);                                      // the driver makes its state
        var s = (DropperDriver.State)d.DriverState!;
        s.Timer = 3.5;                                    // as just after a drop attempt
        d.Arms.LastDropSkipped = true;                    // which the world refused
        var drops = Drops(w, d, 30, 3.0, stopAfter: 1);
        Assert.Single(drops);
        Assert.False(d.Arms.LastDropSkipped);
        Assert.True(w.Time <= 1.2 + 1.0, $"dropped at {w.Time}");
    }

    [Fact]
    public void Armored_takes_0_15_per_bullet_and_hints_once()
    {
        var w = Straight();
        var a = Spawn(w, AgentKind.Armored, 30, 0, 0);
        a.Driver = new StillDriver();
        for (int i = 0; i < 10; i++)
        {
            w.Tracers.Add(new Tracer { Pose = new RoadPose(a.Distance - 8, 0, 0), PrevDistance = a.Distance - 8, Speed = 300, OwnerId = w.Player.Id });
            w.StepOnce(ControlFrame.Empty);
        }
        for (int i = 0; i < 30; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Equal(10.5, a.Armor, 9);
        Assert.Equal(1, w.Events.OfType<Message>().Count(m => m is { Text: "ARMORED: USE A MISSILE, A RAM OR A HAZARD", Tone: Tone.Info }));
        Assert.True(a.HintShown);
    }

    [Fact]
    public void Missile_destroys_an_armored_car_outright()
    {
        var w = Straight();
        var a = Spawn(w, AgentKind.Armored, 150, 0, 0);
        w.Handle(new GameAction(ActionKind.FireMissile));
        for (int i = 0; i < 400; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Kind: AgentKind.Armored, Cause: KillCause.Missile });
        Assert.Single(w.Hazards.OfType<Wreck>());
    }

    [Fact]
    public void Armored_rams_from_behind_then_backs_off()
    {
        var w = Straight();
        var a = Spawn(w, AgentKind.Armored, -40, 0, 30);
        int hit = -1;
        for (int i = 0; i < 1200 && hit < 0; i++)
        {
            Step(w, 30);
            if (w.Events.OfType<PlayerDamaged>().Any(e => e.Reason == "HIT FROM BEHIND")) hit = i;
        }
        Assert.True(hit >= 0, "never rammed");
        for (int i = 0; i < 120; i++) Step(w, 30);
        Assert.True(a.Distance - w.Player.Distance < -5, $"ΔD {a.Distance - w.Player.Distance}");
        var s = (ArmoredDriver.State)a.DriverState!;
        Assert.Equal(ArmoredPhase.BackOff, s.Phase);
        int steps = 120;
        while (s.Phase != ArmoredPhase.Chase && steps < 600) { Step(w, 30); steps++; }
        Assert.True(steps * Dt <= 2.5, $"chase again after {steps * Dt} s");
    }

    [Fact]
    public void Armored_ahead_lumbers_at_cruise_speed()
    {
        var w = Straight();
        var a = Spawn(w, AgentKind.Armored, 100, 0, 32);
        Assert.Equal(32, a.CruiseSpeed);
        int steps = 0;
        while (a.Distance - w.Player.Distance > 5 && steps++ < 1200)
        {
            Step(w, 50);
            Assert.InRange(a.Speed, 31.5, 32.5);
        }
        Assert.True(steps > 100);
    }

    [Fact]
    public void Armored_needs_a_hard_ram_to_spin()
    {
        var w = Straight();
        w.Player.Speed = 30;
        w.Player.LateralVel = 6;
        var soft = Spawn(w, AgentKind.Armored, 0, 1.8, 30);
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Driving, soft.State);
        Assert.True(w.Player.Armor < 100);

        var w2 = Straight();
        w2.Player.Speed = 30;
        w2.Player.LateralVel = 9;
        var hard = Spawn(w2, AgentKind.Armored, 0, 1.8, 30);
        Collisions.Resolve(w2);
        Assert.Equal(VehicleState.Spinning, hard.State);
    }

    [Fact]
    public void Caltrops_puncture_an_armored_car_off_the_road()
    {
        var w = Straight();
        w.Hazards.Add(new Caltrops { Distance = 100, Lateral = 0 });
        var a = Spawn(w, AgentKind.Armored, 90, 0, 30);
        for (int i = 0; i < 1200; i++) { Step(w, 0); }
        Assert.True(a.Punctured);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Kind: AgentKind.Armored, Cause: KillCause.RanOffRoad });
    }
}
