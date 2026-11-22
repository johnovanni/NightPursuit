using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class BossTests
{
    const double Dt = 1.0 / 120;
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static BossDefinition Two => new("Test", 100, [new(AgentKind.Blocker, 0.5), new(AgentKind.Pursuer, 0)]);
    static BossDefinition Three => new("Test", 100, [new(AgentKind.Gunner, 0.66), new(AgentKind.Dropper, 0.33), new(AgentKind.Pursuer, 0)]);
    static BossDefinition First(AgentKind kind) => new("Test", 100, [new(kind, 0.5), new(AgentKind.Blocker, 0)]);

    /// <summary>A boss in the world; Engaged unless stated.</summary>
    static BossCar Spawn(World w, BossDefinition def, double distance, double lateral = 0, double speed = 40, bool engaged = true)
    {
        var b = BossCar.Create(w.NextId(), def, distance, lateral, speed);
        b.Engaged = engaged;
        w.Agents.Add(b);
        return b;
    }

    /// <summary>One step with the player's speed held.</summary>
    static void Step(World w, double speed) { w.Player.Speed = speed; w.StepOnce(ControlFrame.Empty); }

    /// <summary>One step with the player held at <paramref name="speed"/> and <paramref name="lateral"/> (no shove carries over).</summary>
    static void Hold(World w, double speed, double lateral = 0)
    {
        var p = w.Player;
        p.Speed = speed;
        p.Lateral = lateral;
        p.LateralVel = 0;
        p.PushVel = 0;
        p.Heading = 0;
        w.StepOnce(ControlFrame.Empty);
    }

    static double Gap(World w, AgentCar b) => b.Distance - w.Player.Distance;

    // ---- the car and its numbers ----

    [Theory]
    [InlineData(BossLook.Fin)]
    [InlineData(BossLook.Twin)]
    [InlineData(BossLook.Dome)]
    public void BossCar_takes_its_look_from_the_definition(BossLook look)
    {
        var def = Two with { Look = look };
        Assert.Equal(look, BossCar.Create(3, def, 300, 0, 40).Look);
        Assert.Equal(BossLook.Fin, BossCar.Create(4, Two, 300, 0, 40).Look);
    }

    [Fact]
    public void Boss_car_is_built_from_its_definition()
    {
        Assert.Equal(new VehicleStats(2.4, 5.6, 1.5, 88, 9, 5.5, 180, 5000), VehicleStats.Boss);
        Assert.Equal(0.3, DriverHelpers.ReactionDelay(AgentKind.Boss));

        var def = Three;
        var b = BossCar.Create(7, def, 300, 0, 40);
        Assert.Equal(AgentKind.Boss, b.Kind);
        Assert.Equal(7, b.Id);
        Assert.Equal(VehicleStats.Boss with { Armor = 100 }, b.Stats);
        Assert.Equal(100, b.Armor);
        Assert.Equal(100, b.MaxHealth);
        Assert.Equal(1.0, b.HealthFraction);
        Assert.Same(def, b.Definition);
        Assert.Equal(0, b.PhaseIndex);
        Assert.Equal(AgentKind.Gunner, b.Phase.Behaviour);
        Assert.Equal(52, b.CruiseSpeed);
        Assert.Equal(40, b.Speed);
        Assert.Same(BossDriver.Instance, b.Driver);
        Assert.False(b.Engaged);
        Assert.NotNull(b.Arms.RearGuns);
        Assert.Equal(5, b.Arms.Oil!.Charges);
        Assert.Equal(3, b.Arms.Caltrops!.Charges);
        Assert.Equal(300, b.PrevDistance);
        Assert.Equal(0, b.Shield);
        Assert.Equal(0, b.SlowLeft);
        Assert.False(b.ArmorOpen);
        Assert.Null(b.LastWreck);
    }

    [Fact]
    public void Each_behaviour_has_one_shared_driver()
    {
        Assert.IsType<RunnerDriver>(AgentCar.DriverFor(AgentKind.Runner));
        Assert.IsType<BlockerDriver>(AgentCar.DriverFor(AgentKind.Blocker));
        Assert.IsType<PursuerDriver>(AgentCar.DriverFor(AgentKind.Pursuer));
        Assert.IsType<GunnerDriver>(AgentCar.DriverFor(AgentKind.Gunner));
        Assert.IsType<DropperDriver>(AgentCar.DriverFor(AgentKind.Dropper));
        Assert.IsType<ArmoredDriver>(AgentCar.DriverFor(AgentKind.Armored));
        foreach (var kind in new[] { AgentKind.Runner, AgentKind.Blocker, AgentKind.Pursuer, AgentKind.Gunner, AgentKind.Dropper, AgentKind.Armored })
        {
            Assert.Same(AgentCar.DriverFor(kind), AgentCar.DriverFor(kind));
            Assert.Same(AgentCar.DriverFor(kind), AgentCar.Create(1, kind, 0, 0, 30).Driver);
        }
        Assert.NotSame(RunnerDriver.Weaving, AgentCar.DriverFor(AgentKind.Runner));
    }

    // ---- phases and damage ----

    [Fact]
    public void Phase_ends_below_its_threshold_and_swaps_behaviour()
    {
        var w = Straight();
        var b = Spawn(w, Two, 50);
        Step(w, 40);
        Assert.NotNull(b.DriverState);

        w.DamageBoss(b, 51, KillCause.Gunfire, "TEST");

        Assert.Equal(1, b.PhaseIndex);
        Assert.Equal(AgentKind.Pursuer, b.Phase.Behaviour);
        Assert.Null(b.DriverState);
        Assert.Equal(50, b.Armor, 9);
        Assert.Equal(0.5, b.HealthFraction, 9);
        Assert.Equal(BossDamage.PhaseShieldSeconds, b.Shield);
        Assert.Contains(w.Events, e => e is BossDamaged { Reason: "TEST" });
        Assert.Contains(w.Events, e => e is BossPhaseChanged { Phase: 2, PhaseCount: 2, Behaviour: AgentKind.Pursuer });
        Assert.Contains(w.Events, e => e is Message { Text: "BOSS PHASE 2 OF 2: PURSUER", Tone: Tone.Info });

        Step(w, 40);
        Assert.IsType<BossPursuitDriver.State>(b.DriverState);
    }

    [Fact]
    public void One_blow_never_skips_a_phase()
    {
        var w = Straight();
        var b = Spawn(w, Three, 50);
        b.Armor = 67;

        w.DamageBoss(b, 35, KillCause.Satellite, "STRIKE");
        Assert.Equal(66.0, b.Armor);
        Assert.Equal(1, b.PhaseIndex);

        w.DamageBoss(b, 20, KillCause.Missile, "MISSILE");     // the same step: shielded
        Assert.Equal(66.0, b.Armor);
        Assert.Equal(1, b.PhaseIndex);
        Assert.Single(w.Events.OfType<BossPhaseChanged>());

        for (int i = 0; i < 180; i++) Step(w, 40);              // 1.5 s
        Assert.Equal(0, b.Shield);
        w.DamageBoss(b, 20, KillCause.Missile, "MISSILE");
        Assert.Equal(46, b.Armor, 9);
        Assert.Equal(1, b.PhaseIndex);
    }

    [Fact]
    public void A_blow_never_heals_a_boss_already_below_its_floor()
    {
        var w = Straight();
        var b = Spawn(w, Two, 50);
        b.Armor = 40;                                           // below phase 1's floor of 50

        w.DamageBoss(b, 5, KillCause.Gunfire, "TEST");

        Assert.Equal(40, b.Armor);
        Assert.Equal(1, b.PhaseIndex);
        Assert.True(Assert.Single(w.Events.OfType<BossDamaged>()).Amount >= 0);
    }

    [Fact]
    public void Boss_dies_only_when_the_last_phase_reaches_zero()
    {
        var w = Straight();
        var b = Spawn(w, Two, 50);
        var phaseAtEachBlow = new List<int>();
        for (int i = 0; i < 20 && !w.Events.OfType<VehicleDestroyed>().Any(); i++)
        {
            phaseAtEachBlow.Add(b.PhaseIndex);
            b.Shield = 0;
            w.DamageBoss(b, 20, KillCause.Gunfire, "TEST");
        }

        var destroyed = Assert.Single(w.Events.OfType<VehicleDestroyed>());
        Assert.Equal(AgentKind.Boss, destroyed.Kind);
        Assert.Equal(b.Id, destroyed.VehicleId);
        Assert.Equal(1, phaseAtEachBlow[^1]);                   // the killing blow landed in the last phase
        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1 }, phaseAtEachBlow);   // 80, 60, 50 (phase 2), 30, 10, 0
        Assert.Equal(0, b.Armor);
        Assert.Equal(VehicleState.Wrecked, b.State);

        Step(w, 40);
        Assert.Contains(b, w.Agents);                           // kept for its crash (the finale)...
        while (!w.Finale!.Exploded) Step(w, 1);
        Assert.DoesNotContain(b, w.Agents);                     // ...and removed when it blows up
    }

    [Fact]
    public void Boss_never_runs_off_the_road()
    {
        var w = Straight();
        var b = Spawn(w, Two, 30, lateral: 9);
        b.PushVel = 7;
        b.State = VehicleState.Spinning;
        b.StateTimer = 1.5;
        b.SpinRate = 7;

        Step(w, 40);
        Assert.Equal(6.3, b.Lateral, 9);
        Assert.True(b.PushVel <= 0, $"PushVel {b.PushVel}");
        Assert.Contains(b, w.Agents);
        Assert.NotEqual(VehicleState.Wrecked, b.State);

        // Shoved past the edge between steps (a ram), the off-road rule still leaves it alone.
        b.State = VehicleState.Driving;
        b.Lateral = -9;
        b.PushVel = -7;
        Collisions.Resolve(w);
        Assert.NotEqual(VehicleState.Wrecked, b.State);

        for (int i = 0; i < 240; i++)
        {
            Step(w, 40);
            Assert.True(Math.Abs(b.Lateral) <= 6.3 + 1e-9, $"Lateral {b.Lateral}");
        }
        Assert.Contains(b, w.Agents);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
    }

    // ---- never lost ----

    [Fact]
    public void Boss_is_never_removed_for_distance_and_catches_up()
    {
        var w = Straight();
        var far = Spawn(w, Two, -700, speed: 50);
        var ahead = Spawn(w, Two, 1700, speed: 50);
        Step(w, 50);
        Assert.Contains(far, w.Agents);
        Assert.Contains(ahead, w.Agents);

        var w2 = Straight();
        var b = Spawn(w2, Two, -300, speed: 50);
        double? caughtAt = null;
        for (int i = 0; i < 10 * 120 && caughtAt is null; i++)
        {
            Step(w2, 50);
            Assert.Contains(b, w2.Agents);
            if (Gap(w2, b) > -120) caughtAt = w2.Time;
        }
        Assert.NotNull(caughtAt);
    }

    [Fact]
    public void Boss_far_ahead_waits_for_you()
    {
        var w = Straight();
        var b = Spawn(w, Two, 700, speed: 52);
        for (int i = 0; i < 3 * 120; i++) Step(w, 50);
        Assert.Equal(35, b.Speed, 1);                           // your speed - 15
    }

    [Theory]
    [InlineData(AgentKind.Blocker, 0, 0)]
    [InlineData(AgentKind.Armored, 0, 0)]
    [InlineData(AgentKind.Blocker, 1, 2.5)]
    [InlineData(AgentKind.Blocker, -1, -2.5)]
    [InlineData(AgentKind.Blocker, 1.5, 3)]
    [InlineData(AgentKind.Blocker, 1, 0)]
    [InlineData(AgentKind.Armored, 1, 2.5)]
    [InlineData(AgentKind.Armored, -1, -2.5)]
    [InlineData(AgentKind.Blocker, 5, 5.5)]                     // outer lane, boss on the outer side: no line clears you
    [InlineData(AgentKind.Blocker, 4.5, 6.0)]
    [InlineData(AgentKind.Armored, -5, -5.5)]
    [InlineData(AgentKind.Blocker, 5, 6.3)]
    public void Boss_left_behind_in_a_phase_that_never_overtakes_passes_you_clear_of_your_lane(AgentKind behaviour, double playerLateral, double bossLateral)
    {
        var w = Straight();
        var b = Spawn(w, First(behaviour), -40, lateral: bossLateral, speed: 60);
        bool reached = false;
        for (int i = 0; i < 15 * 120 && !reached; i++)
        {
            Hold(w, 60, playerLateral);
            double dD = Gap(w, b);
            if (Math.Abs(dD) < (VehicleStats.Boss.Length + VehicleStats.Player.Length) / 2 + 2)
                Assert.True(Math.Abs(b.Lateral - w.Player.Lateral) >= 2.5, $"alongside {b.Lateral - w.Player.Lateral:F2} m off at ΔD {dD:F1}");
            reached = dD >= 60;
        }
        Assert.True(reached, $"only {Gap(w, b):F1} m ahead after 15 s");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    // ---- arrival ----

    [Fact]
    public void Boss_eases_in_at_your_speed_minus_10_until_within_140_m_or_hit()
    {
        var w = Straight();
        w.Player.Lateral = 5;
        var b = Spawn(w, Two, 300, lateral: 0, speed: 40, engaged: false);
        double? engagedAt = null;
        for (int i = 0; i < 20 * 120 && engagedAt is null; i++)
        {
            Step(w, 50);
            if (!b.Engaged)
            {
                Assert.True(Gap(w, b) > 140, $"not engaged at ΔD {Gap(w, b)}");
                Assert.Equal(40, b.Speed, 0.5);
                Assert.Equal(0, b.Lateral, 9);
            }
            else
            {
                engagedAt = w.Time;
                Assert.True(Gap(w, b) <= 140 + 40 * Dt, $"engaged at ΔD {Gap(w, b)}");
            }
        }
        Assert.NotNull(engagedAt);
        Assert.InRange(engagedAt!.Value, 15.5, 16.5);
        for (int i = 0; i < 3 * 120; i++) Step(w, 50);
        Assert.True(b.Lateral > 2.5, $"Blocker phase should move toward your lane: Lateral {b.Lateral}");

        // A hit of any kind engages it at once, even a shielded one.
        var w2 = Straight();
        var b2 = Spawn(w2, Two, 300, speed: 40, engaged: false);
        Step(w2, 50);
        Assert.False(b2.Engaged);
        w2.DamageBoss(b2, 1, KillCause.Gunfire, "TEST");
        Assert.True(b2.Engaged);

        var w3 = Straight();
        var b3 = Spawn(w3, Two, 300, speed: 40, engaged: false);
        Step(w3, 50);
        w3.SpinBoss(b3);
        Assert.True(b3.Engaged);
        Assert.Equal(VehicleState.Spinning, b3.State);

        var w4 = Straight();
        var b4 = Spawn(w4, Two, 300, speed: 40, engaged: false);
        b4.Shield = 1;
        w4.DamageBoss(b4, 5, KillCause.Gunfire, "TEST");
        Assert.True(b4.Engaged);
        Assert.Equal(100, b4.Armor);

        var w5 = Straight();
        var b5 = Spawn(w5, Two, 300, speed: 40, engaged: false);
        b5.SlowLeft = BossDamage.CaltropSlowSeconds;            // the caltrops slow
        Step(w5, 50);
        Assert.True(b5.Engaged);
    }

    // ---- spin, slow and shield ----

    [Fact]
    public void Spin_opens_its_armor_for_1_5_s()
    {
        var w = Straight();
        var b = Spawn(w, Two, 200, speed: 40);
        b.PushVel = 3;
        w.SpinBoss(b);
        Assert.Equal(VehicleState.Spinning, b.State);
        Assert.True(b.ArmorOpen);
        Assert.Equal(BossDamage.OilSpinSeconds, b.StateTimer);
        Assert.Equal(7, Math.Abs(b.SpinRate));
        Assert.Equal(0, b.PushVel);
        Assert.Equal(34, b.Speed, 9);
        Assert.Contains(w.Events, e => e is Message { Text: "BOSS SPUN OUT - ARMOR OPEN", Tone: Tone.Good });
    }

    [Fact]
    public void Caltrops_slow_cuts_its_target_speed_and_counts_down()
    {
        var w = Straight();
        var b = Spawn(w, Two, 200, speed: 52);                  // Blocker phase beyond 140 m: cruises at 52
        b.SlowLeft = BossDamage.CaltropSlowSeconds;
        for (int i = 0; i < 3 * 120; i++) Step(w, 40);
        Assert.True(b.Speed <= BossDamage.CaltropSlowFactor * 52 + 1, $"Speed {b.Speed}");
        for (int i = 0; i < 121; i++) Step(w, 40);              // 4.01 s
        Assert.Equal(0, b.SlowLeft);
    }

    // ---- boss drivers ----

    static BossPursuitDriver.State Pursuit(BossCar b) => Assert.IsType<BossPursuitDriver.State>(b.DriverState);

    [Theory]
    [InlineData(0, 2.7)]
    [InlineData(1, 2.7)]
    [InlineData(-1, 2.7)]
    [InlineData(1.5, 2.7)]
    [InlineData(3, 5.7)]                                        // starts on the outer side, where no lane is 2.5 m from you
    [InlineData(3.5, 6.2)]
    [InlineData(-3, -5.7)]
    [InlineData(-3.5, -6.2)]
    public void Pursuit_boss_rams_then_pulls_ahead_into_gun_range(double playerLateral, double bossLateral)
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Pursuer), -30, lateral: bossLateral, speed: 40);
        int Swipes() => w.Events.OfType<PlayerDamaged>().Count(e => e.Reason == "SIDESWIPED");
        int Hits() => w.Events.OfType<PlayerDamaged>().Count();

        double? swipe = null;
        for (int i = 0; i < 15 * 120 && swipe is null; i++)
        {
            Hold(w, 40, playerLateral);
            if (Swipes() > 0) swipe = w.Time;
        }
        Assert.True(swipe is not null, "no sideswipe within 15 s");
        int hitsAtRam = Hits();
        double halfWidths = (VehicleStats.Boss.Width + VehicleStats.Player.Width) / 2;
        double lengths = VehicleStats.Boss.Length + VehicleStats.Player.Length;

        // It pulls 60 m ahead within 10 s, clear of your lane while it passes, and never touches you on the way.
        double? ahead60 = null;
        double aheadSeconds = 0;
        bool leftAttack = false, backInAttack = false;
        while (w.Time < swipe + 40 && !backInAttack)
        {
            Hold(w, 40, playerLateral);
            double dD = Gap(w, b);
            var phase = Pursuit(b).Phase;
            if (ahead60 is null)
            {
                double off = Math.Abs(b.Lateral - w.Player.Lateral);
                Assert.True(Hits() == hitsAtRam, $"touched you again while passing ({phase}, ΔD {dD:F1}, {w.Time - swipe:F2} s after the ram)");
                if (dD >= 60) ahead60 = w.Time;
                else if (w.Time > swipe + 0.5)
                    Assert.True(off >= 2.5, $"{phase} {b.Lateral - w.Player.Lateral:F2} m off at ΔD {dD:F1}, {w.Time - swipe:F2} s after the ram");
                // From the end of the ram's contact cooldown (Hold puts you back where you were, undoing the push apart).
                if (w.Time > swipe + 0.25 && Math.Abs(dD) < lengths)
                    Assert.True(off >= halfWidths, $"alongside {off:F2} m off at ΔD {dD:F1}");
            }
            if (dD >= 10) aheadSeconds += Dt;
            if (phase != BossPursuitPhase.Attack) leftAttack = true;
            else if (leftAttack) backInAttack = true;
        }
        Assert.True(ahead60 is not null && ahead60 - swipe <= 10, $"60 m ahead at {ahead60 - swipe} s after the ram");
        Assert.True(backInAttack, "never came back to attack");
        Assert.True(aheadSeconds >= 6, $"only {aheadSeconds:F1} s at least 10 m ahead in one cycle");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(3.5)]
    [InlineData(-3)]
    [InlineData(-3.5)]
    public void Overtaking_boss_with_no_lane_on_its_side_holds_its_line_until_ahead(double playerLateral)
    {
        var w = Straight();
        int side = Math.Sign(playerLateral);                    // alongside you on the outer side
        var b = Spawn(w, First(AgentKind.Pursuer), 0, lateral: Math.Clamp(playerLateral + side * 2.7, -6.3, 6.3), speed: 40);
        b.Side = side;
        b.DriverState = new BossPursuitDriver.State { Phase = BossPursuitPhase.Overtake, Target = b.Lateral };
        double halfWidths = (VehicleStats.Boss.Width + VehicleStats.Player.Width) / 2;
        double lengths = VehicleStats.Boss.Length + VehicleStats.Player.Length;
        bool crossedOver = false;
        for (int i = 0; i < 15 * 120 && Gap(w, b) < 60; i++)
        {
            Hold(w, 40, playerLateral);
            double dD = Gap(w, b), off = Math.Abs(b.Lateral - w.Player.Lateral);
            // Until it is ahead (its rear 2 m past your front), when it may cross in front of you.
            if (Math.Abs(dD) < lengths && dD < lengths / 2 + 2) Assert.True(off >= halfWidths, $"alongside {off:F2} m off at ΔD {dD:F1}");
            if (Math.Sign(b.Lateral - w.Player.Lateral) != side) crossedOver = true;
        }
        Assert.True(Gap(w, b) >= 60, $"only {Gap(w, b):F1} m ahead");
        Assert.True(crossedOver, "never moved to the far lane once ahead");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public void Overtaking_boss_with_no_way_past_drops_back_and_crosses_behind(double playerLateral)
    {
        var w = Straight();
        int side = Math.Sign(playerLateral);                    // just behind you on the outer side, where no line clears you
        var b = Spawn(w, First(AgentKind.Pursuer), -6, lateral: side * 6.3, speed: 40);
        b.Side = side;
        b.DriverState = new BossPursuitDriver.State { Phase = BossPursuitPhase.Overtake, Target = b.Lateral };
        double halfWidths = (VehicleStats.Boss.Width + VehicleStats.Player.Width) / 2;
        double halfLengths = (VehicleStats.Boss.Length + VehicleStats.Player.Length) / 2;
        double furthestBack = 0;
        for (int i = 0; i < 15 * 120 && Gap(w, b) < 60; i++)
        {
            Hold(w, 40, playerLateral);
            double dD = Gap(w, b), off = Math.Abs(b.Lateral - w.Player.Lateral);
            if (Math.Abs(dD) < halfLengths) Assert.True(off >= halfWidths, $"alongside {off:F2} m off at ΔD {dD:F1}");
            furthestBack = Math.Min(furthestBack, dD);
        }
        Assert.True(furthestBack <= -(halfLengths + 2) + 0.5, $"only dropped back to ΔD {furthestBack:F1}");
        Assert.True(Gap(w, b) >= 60, $"only {Gap(w, b):F1} m ahead");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    /// <summary>
    /// Player lateral, speed, and the boss's ΔD with its rear 0.1-1.5 m past your front (at 5.1 and 5.3 it reads as
    /// short of your front when it decides, because you move first).
    /// </summary>
    public static TheoryData<double, double, double> JustPastYourFront()
    {
        var data = new TheoryData<double, double, double>();
        foreach (double lateral in new[] { 5, -5, 4.5 })
            foreach (double speed in new[] { 40.0, 60.0, 80.0 })
                foreach (double dD in new[] { 5.1, 5.3, 5.5, 6.0, 6.5 })
                    data.Add(lateral, speed, dD);
        return data;
    }

    [Theory]
    [MemberData(nameof(JustPastYourFront))]
    public void Overtaking_boss_just_past_your_front_pulls_clear_instead_of_braking_into_you(double playerLateral, double speed, double startGap)
    {
        var w = Straight();
        int side = Math.Sign(playerLateral);                    // on your side, where no line clears you
        var b = Spawn(w, First(AgentKind.Pursuer), startGap, lateral: side * 6.3, speed: speed);
        b.Side = side;
        b.DriverState = new BossPursuitDriver.State { Phase = BossPursuitPhase.Overtake, Target = b.Lateral };
        double halfWidths = (VehicleStats.Boss.Width + VehicleStats.Player.Width) / 2;
        double halfLengths = (VehicleStats.Boss.Length + VehicleStats.Player.Length) / 2;
        for (int i = 0; i < 10 * 120 && Gap(w, b) < 60; i++)
        {
            Hold(w, speed, playerLateral);
            double dD = Gap(w, b), off = Math.Abs(b.Lateral - w.Player.Lateral);
            if (Math.Abs(dD) < halfLengths) Assert.True(off >= halfWidths, $"alongside {off:F2} m off at ΔD {dD:F2}");
        }
        Assert.True(Gap(w, b) >= 60, $"only {Gap(w, b):F1} m ahead after 10 s");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    [Theory]
    [InlineData(5, 60)]
    [InlineData(-5, 60)]
    [InlineData(4.5, 60)]
    [InlineData(-4.5, 60)]
    [InlineData(5, 80)]
    [InlineData(-5, 80)]
    [InlineData(4.5, 80)]
    [InlineData(-4.5, 80)]
    public void Overtaking_boss_crossing_behind_you_waits_until_it_is_out_of_your_line(double playerLateral, double speed)
    {
        var w = Straight();
        int side = Math.Sign(playerLateral);                    // 1.5 m behind your rear bumper, on your side, at your speed
        var b = Spawn(w, First(AgentKind.Pursuer), -6.5, lateral: side * 6.3, speed: speed);
        b.Side = side;
        b.DriverState = new BossPursuitDriver.State { Phase = BossPursuitPhase.Overtake, Target = b.Lateral };
        double halfWidths = (VehicleStats.Boss.Width + VehicleStats.Player.Width) / 2;
        double halfLengths = (VehicleStats.Boss.Length + VehicleStats.Player.Length) / 2;
        for (int i = 0; i < 15 * 120 && Gap(w, b) < 60; i++)
        {
            Hold(w, speed, playerLateral);
            double dD = Gap(w, b), off = Math.Abs(b.Lateral - w.Player.Lateral);
            if (Math.Abs(dD) < halfLengths) Assert.True(off >= halfWidths, $"alongside {off:F2} m off at ΔD {dD:F2}");
        }
        Assert.True(Gap(w, b) >= 60, $"only {Gap(w, b):F1} m ahead after 15 s");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    [Fact]
    public void Passing_lane_holds_the_line_until_the_rear_is_2_m_past_your_front()
    {
        var w = Straight();
        w.Player.Lateral = 3;
        w.Player.Speed = 40;
        var b = Spawn(w, First(AgentKind.Pursuer), 0, lateral: 5);
        double Lane(double? current = null) => DriverHelpers.PassingLane(b, w.Player, current).Lateral;
        Assert.Equal(5.45, Lane(), 9);                          // pushed to half widths + 0.3 off you
        Assert.Equal(double.PositiveInfinity, DriverHelpers.PassingLane(b, w.Player).MaxSpeed);
        b.Lateral = 6;
        Assert.Equal(6, Lane(), 9);                             // already clear: holds its line
        b.Distance = (5.6 + 4.4) / 2 + 2 - 0.01;
        Assert.Equal(6, Lane(), 9);
        b.Distance = (5.6 + 4.4) / 2 + 2;
        Assert.Equal(-5, Lane(), 9);                            // rear 2 m past your front: farthest lane
        Assert.Equal(-5, Lane(current: -5), 9);
        w.Player.Lateral = 1;
        b.Distance = 0;
        Assert.Equal(5, Lane(current: 6), 9);                   // a held line is not kept as a lane
    }

    [Fact]
    public void Passing_lane_with_no_line_that_clears_you_waits_then_crosses_behind()
    {
        var w = Straight();
        w.Player.Lateral = 5;
        w.Player.Speed = 60;
        var b = Spawn(w, First(AgentKind.Blocker), -3, lateral: 6.3);   // overlapping you lengthwise, on the outer side

        var line = DriverHelpers.PassingLane(b, w.Player);
        Assert.Equal(6.3, line.Lateral, 9);                     // no crossing, no pushing in
        Assert.Equal(57, line.MaxSpeed, 9);                     // waits at your speed - 3

        b.Distance = -((5.6 + 4.4) / 2 + 2) + 0.01;
        Assert.Equal(57, DriverHelpers.PassingLane(b, w.Player).MaxSpeed, 9);
        b.Distance = -((5.6 + 4.4) / 2 + 2);                    // front 2 m short of your rear: crosses behind
        line = DriverHelpers.PassingLane(b, w.Player);
        Assert.Equal(-5, line.Lateral, 9);
        Assert.Equal(57, line.MaxSpeed, 9);                     // ... still no faster than you - 3 while in your line
        Assert.Equal(57, DriverHelpers.PassingLane(b, w.Player, current: -5).MaxSpeed, 9);
        b.Lateral = 2.55;                                       // half widths + 0.3 off you: out of your line
        Assert.Equal(double.PositiveInfinity, DriverHelpers.PassingLane(b, w.Player, current: -5).MaxSpeed);
    }

    [Fact]
    public void Passing_lane_lets_a_car_ahead_of_you_by_centres_pull_clear()
    {
        var w = Straight();
        w.Player.Lateral = 5;
        w.Player.Speed = 60;
        var b = Spawn(w, First(AgentKind.Pursuer), 0, lateral: 6.3);
        double halfLengths = (5.6 + 4.4) / 2;
        // Level by centres, just ahead, rear just short of your front (as seen after you move first), and up to 2 m past it.
        foreach (double dD in new[] { 0, 0.5, halfLengths - 0.5, halfLengths, halfLengths + 1.2, halfLengths + 2 - 0.01 })
        {
            b.Distance = dD;
            var line = DriverHelpers.PassingLane(b, w.Player);
            Assert.Equal(6.3, line.Lateral, 9);                 // holds its line, never crosses yet
            Assert.Equal(double.PositiveInfinity, line.MaxSpeed);   // and never brakes back into you
        }
        b.Distance = -0.01;                                     // behind you by centres: waits
        Assert.Equal(57, DriverHelpers.PassingLane(b, w.Player).MaxSpeed, 9);
    }

    [Theory]
    [InlineData(4.1, 40, 0.5)]
    [InlineData(-4.1, 60, 0.5)]
    [InlineData(4.1, 80, 1.0)]                                  // at 80 m/s you move 0.67 m before it decides
    public void Overtaking_boss_just_ahead_of_you_by_centres_pulls_clear(double playerLateral, double speed, double startGap)
    {
        var w = Straight();
        int side = Math.Sign(playerLateral);                    // just ahead by centres, 2.2 m off on your side
        var b = Spawn(w, First(AgentKind.Pursuer), startGap, lateral: side * 6.3, speed: speed);
        b.Side = side;
        b.DriverState = new BossPursuitDriver.State { Phase = BossPursuitPhase.Overtake, Target = b.Lateral };
        double halfWidths = (VehicleStats.Boss.Width + VehicleStats.Player.Width) / 2;
        double halfLengths = (VehicleStats.Boss.Length + VehicleStats.Player.Length) / 2;
        for (int i = 0; i < 10 * 120 && Gap(w, b) < 60; i++)
        {
            Hold(w, speed, playerLateral);
            double dD = Gap(w, b), off = Math.Abs(b.Lateral - w.Player.Lateral);
            if (Math.Abs(dD) < halfLengths) Assert.True(off >= halfWidths, $"alongside {off:F2} m off at ΔD {dD:F2}");
        }
        Assert.True(Gap(w, b) >= 60, $"only {Gap(w, b):F1} m ahead after 10 s");
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    [Theory]
    [InlineData(5, 6.3)]
    [InlineData(-5, -6.3)]
    public void Pursuit_boss_attacks_a_player_in_an_outer_lane_from_the_side_with_room(double playerLateral, double bossLateral)
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Pursuer), -30, lateral: bossLateral, speed: 40);   // starts on the side with no room
        for (int i = 0; i < 60 * 120; i++) Hold(w, 40, playerLateral);
        var reasons = w.Events.OfType<PlayerDamaged>().Select(e => e.Reason).ToList();
        Assert.Contains("SIDESWIPED", reasons);
        Assert.DoesNotContain("RAMMED", reasons);
        Assert.DoesNotContain("HIT FROM BEHIND", reasons);
        Assert.Equal(-Math.Sign(playerLateral), b.Side);
    }

    [Fact]
    public void Pursuit_boss_holds_alongside_until_level_before_the_attack_clock_starts()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Pursuer), -30, lateral: 2.7, speed: 40);
        Hold(w, 40);
        var s = Pursuit(b);
        Assert.Equal(BossPursuitPhase.Attack, s.Phase);
        while (!s.Alongside && w.Time < 10)
        {
            Assert.Equal(2.7, s.Target, 9);                     // holds Side * 2.7 while closing
            Assert.True(Math.Abs(Gap(w, b)) >= 2.5, $"ΔD {Gap(w, b)} but the clock has not started");
            Hold(w, 40);
        }
        Assert.True(s.Alongside, "never came level");
        Assert.True(Math.Abs(Gap(w, b)) < 3.5, $"clock started at ΔD {Gap(w, b)}");
        Assert.Equal(DriverHelpers.AttackSeconds, s.Timer, 9);
        Assert.Equal(2.7, s.Target, 9);
        while (s.Timer >= DriverHelpers.RamSeconds && s.Phase == BossPursuitPhase.Attack) Hold(w, 40);
        Assert.Equal(BossPursuitPhase.Attack, s.Phase);
        Assert.Equal(0.8, s.Target, 9);                         // the ram: Side * 0.8
    }

    [Fact]
    public void Pursuit_boss_far_ahead_starts_by_dropping_back()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Pursuer), 100, lateral: 0, speed: 40);
        Hold(w, 40);
        Assert.Equal(BossPursuitPhase.DropBack, Pursuit(b).Phase);
    }

    [Fact]
    public void Runner_boss_weaves_and_stays_ahead()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, lateral: 0, speed: 50);
        int changes = 0;
        double? last = null;
        for (int i = 0; i < 20 * 120; i++)
        {
            Step(w, 50);
            var s = Assert.IsType<RunnerDriver.State>(b.DriverState);
            if (last is double l && Math.Abs(s.Target - l) > 1e-9) changes++;
            last = s.Target;
            double dD = Gap(w, b);
            Assert.InRange(dD, 30, 120);
        }
        Assert.True(changes >= 10, $"only {changes} lane changes in 20 s");
    }

    [Fact]
    public void Ordinary_pursuers_keep_their_plan_1_retreat()
    {
        Assert.IsType<PursuerDriver>(AgentCar.DriverFor(AgentKind.Pursuer));

        var w = Straight();
        var car = AgentCar.Create(w.NextId(), AgentKind.Pursuer, -5, 2.7, 40);
        w.Agents.Add(car);
        Assert.IsType<PursuerDriver>(car.Driver);
        car.ContactCooldown = 1;                                // it has just touched you
        Step(w, 40);                                            // Approach -> Attack
        Step(w, 40);                                            // Attack -> Retreat
        Assert.Equal(PursuerPhase.Retreat, Assert.IsType<PursuerDriver.State>(car.DriverState).Phase);
    }
}
