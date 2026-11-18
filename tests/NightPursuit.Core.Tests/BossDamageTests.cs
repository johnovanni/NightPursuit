using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

/// <summary>Every weapon and contact routes a boss through its damage table (ENM-3, Task 11).</summary>
public class BossDamageTests
{
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static BossDefinition Two => new("Test", 100, [new(AgentKind.Blocker, 0.5), new(AgentKind.Pursuer, 0)]);
    static BossDefinition Three => new("Test", 100, [new(AgentKind.Gunner, 0.66), new(AgentKind.Dropper, 0.33), new(AgentKind.Pursuer, 0)]);

    /// <summary>A boss in the world; Engaged unless stated.</summary>
    static BossCar Spawn(World w, BossDefinition def, double distance, double lateral = 0, double speed = 40, bool engaged = true)
    {
        var b = BossCar.Create(w.NextId(), def, distance, lateral, speed);
        b.Engaged = engaged;
        w.Agents.Add(b);
        return b;
    }

    /// <summary>A boss that holds its speed and line, for tests about what a blow does rather than how it drives.</summary>
    static BossCar Still(World w, BossDefinition def, double distance, double speed = 0)
    {
        var b = Spawn(w, def, distance, speed: speed);
        b.Driver = new StillDriver();
        return b;
    }

    /// <summary>One step with the player's speed held.</summary>
    static void Step(World w, double speed = 0) { w.Player.Speed = speed; w.StepOnce(ControlFrame.Empty); }

    static void Run(World w, int steps, double speed = 0) { for (int i = 0; i < steps; i++) Step(w, speed); }

    /// <summary>A player bullet 8 m behind the boss, on its line, that hits it within a few steps.</summary>
    static void Shoot(World w, BossCar b) =>
        w.Tracers.Add(new Tracer
        {
            Pose = new RoadPose(b.Distance - 8, b.Lateral, 0), PrevDistance = b.Distance - 8, PrevLateral = b.Lateral,
            Speed = 300, OwnerId = w.Player.Id,
        });

    /// <summary>A player missile 4 m behind the boss, homing on it, that hits it on the next step.</summary>
    static void MissileAt(World w, BossCar b)
    {
        var pose = new RoadPose(b.Distance - 4, b.Lateral, 0);
        w.Missiles.Add(new Missile
        {
            Pose = pose, PrevDistance = pose.Distance, PrevLateral = pose.Lateral, PrevHeading = 0, Speed = 200, TargetId = b.Id,
        });
    }

    static Wreck StoppedWreck(double distance, double lateral) =>
        new() { Distance = distance, PrevDistance = distance, Lateral = lateral, Width = 2.4, Length = 5.6, Speed = 0, Seed = 99 };

    static bool BossSpunOrDestroyed(World w, BossCar b) =>
        w.Events.Any(e => e is VehicleSpun s && s.VehicleId == b.Id || e is VehicleDestroyed d && d.VehicleId == b.Id);

    // ---- the damage table ----

    [Fact]
    public void Gunfire_takes_one_point_per_bullet()
    {
        var w = Straight();
        var b = Still(w, Two, 30);
        for (int i = 0; i < 5; i++) { Shoot(w, b); Step(w); }
        Run(w, 30);

        Assert.Equal(95, b.Armor, 9);
        Assert.Equal(5, w.Events.OfType<BossDamaged>().Count(e => e.Reason == "GUNFIRE" && Math.Abs(e.Amount - 1) < 1e-9));
        Assert.Empty(w.Tracers);
    }

    [Fact]
    public void Gunfire_does_a_quarter_in_an_Armored_phase()
    {
        var w = Straight();
        var b = Still(w, new BossDefinition("Test", 100, [new(AgentKind.Armored, 0.5), new(AgentKind.Pursuer, 0)]), 30);
        for (int i = 0; i < 4; i++) { Shoot(w, b); Step(w); }
        Run(w, 30);

        Assert.Equal(99, b.Armor, 9);
        Assert.DoesNotContain(w.Events, e => e is Message { Text: "ARMORED: USE A MISSILE, A RAM OR A HAZARD" });
    }

    [Fact]
    public void Missile_takes_20_percent()
    {
        var w = Straight();
        var b = Still(w, Two, 150);
        w.Handle(new GameAction(ActionKind.FireMissile));
        Assert.Contains(w.Events, e => e is Message { Text: "MISSILE AWAY - LOCKED ON BOSS" });
        Run(w, 400);

        Assert.Equal(80, b.Armor, 9);
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.Contains(b, w.Agents);
        Assert.Empty(w.Missiles);
        Assert.Empty(w.Hazards.OfType<Wreck>());
        Assert.Contains(w.Events, e => e is BossDamaged { Reason: "MISSILE" });
        Assert.Contains(w.Events, e => e is Message { Text: "MISSILE HIT THE BOSS", Tone: Tone.Good });
        Assert.DoesNotContain(w.Events, e => e is Message { Text: "MISSILE HIT THE BOSS - SHIELDED" });
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
    }

    [Fact]
    public void A_blow_on_the_shield_says_it_was_shielded()
    {
        var w = Straight();
        var b = Still(w, Two, 100, speed: 30);
        b.Shield = 1;
        MissileAt(w, b);
        w.Hazards.Add(StoppedWreck(102, 1));
        Step(w);

        Assert.Equal(100, b.Armor);
        Assert.Contains(w.Events, e => e is Message { Text: "MISSILE HIT THE BOSS - SHIELDED", Tone: Tone.Info });
        Assert.Contains(w.Events, e => e is Message { Text: "BOSS HIT A WRECK - SHIELDED", Tone: Tone.Info });
        Assert.DoesNotContain(w.Events, e => e is Message { Text: "MISSILE HIT THE BOSS" or "BOSS HIT A WRECK" });
        Assert.Equal(-4, b.PushVel, 9);                         // the shield stops the damage, not the swerve
    }

    [Fact]
    public void Strike_takes_35_percent()
    {
        var w = Straight();
        var b = Still(w, new BossDefinition("Test", 100, [new(AgentKind.Blocker, 0.1), new(AgentKind.Pursuer, 0)]), 100);
        w.Loadout.Strike.Restock(1);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.True(w.Loadout.Strike.Targeting);                // the boss alone is in strike range
        Run(w, 181);

        Assert.Equal(65, b.Armor, 9);
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.Contains(b, w.Agents);
        Assert.Equal(0, Assert.Single(w.Events.OfType<StrikeLanded>()).Destroyed);   // counted only if the blow destroys it
        Assert.Contains(w.Events, e => e is BossDamaged { Reason: "SATELLITE STRIKE" });
        Assert.Empty(w.Hazards.OfType<Wreck>());
    }

    [Fact]
    public void Strike_absorbed_by_the_shield_says_so()
    {
        var w = Straight();
        var b = Still(w, new BossDefinition("Test", 100, [new(AgentKind.Blocker, 0.1), new(AgentKind.Pursuer, 0)]), 100);
        b.Shield = 5;
        w.Loadout.Strike.Restock(1);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Run(w, 181);

        Assert.Equal(100, b.Armor);
        Assert.Contains(w.Events, e => e is Message { Text: "SATELLITE STRIKE: 0 DESTROYED - SHIELDED", Tone: Tone.Info });
        Assert.DoesNotContain(w.Events, e => e is Message { Text: "SATELLITE STRIKE: 0 DESTROYED" });
        Assert.Equal("BOSS SHIELDED", w.Announcement?.Detail);
    }

    [Fact]
    public void Strike_that_destroys_a_last_phase_boss_counts_it()
    {
        var w = Straight();
        var b = Still(w, Two, 100);
        b.PhaseIndex = 1;
        b.Armor = 30;
        w.Loadout.Strike.Restock(1);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Run(w, 181);

        Assert.Equal(VehicleState.Wrecked, b.State);
        Assert.Equal(1, Assert.Single(w.Events.OfType<StrikeLanded>()).Destroyed);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Kind: AgentKind.Boss, Cause: KillCause.Satellite });
    }

    [Fact]
    public void Strike_and_missile_in_one_step_never_skip_a_phase()
    {
        var w = Straight();
        var b = Still(w, Three, 100);
        b.Armor = 67;
        w.Loadout.Strike.Restock(1);
        w.Handle(new GameAction(ActionKind.FireStrike));
        while (w.Loadout.Strike.SweepLeft >= World.Step + 1e-9) Step(w);   // the strike lands on the next step
        MissileAt(w, b);
        int before = w.Events.Count;

        Step(w);

        var step = w.Events.Skip(before).ToList();
        Assert.Contains(step, e => e is StrikeLanded { Destroyed: 0 });
        Assert.Empty(w.Missiles);                               // the missile hit on the same step
        Assert.Equal(1, Assert.Single(step.OfType<BossDamaged>()).Amount, 9);   // the missile; the strike met the shield
        Assert.Equal(66, b.Armor, 9);
        Assert.Equal(1, b.PhaseIndex);
        Assert.Equal(2, Assert.Single(w.Events.OfType<BossPhaseChanged>()).Phase);
        // Raised to 1.5 s on this step, then counted down once at the end of it.
        Assert.Equal(BossDamage.PhaseShieldSeconds - World.Step, b.Shield, 9);
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
    }

    [Fact]
    public void Oil_spins_it_1_5_s_in_place_and_doubles_gunfire()
    {
        var w = Straight();
        var b = Spawn(w, Two, 30, lateral: 0, speed: 40);
        w.Hazards.Add(new OilSlick { Distance = 31, Lateral = 0 });   // the player's oil
        Step(w);

        Assert.Equal(VehicleState.Spinning, b.State);
        Assert.Equal(BossDamage.OilSpinSeconds, b.StateTimer, 9);
        Assert.Contains(w.Events, e => e is Message { Text: "BOSS SPUN OUT - ARMOR OPEN" });
        Assert.DoesNotContain(w.Events, e => e is Message { Text: "BOSS HIT THE OIL" });
        double lateral = b.Lateral;

        Shoot(w, b);
        int after = 0;                                          // steps since the one that spun it
        for (; after < 5; after++) Step(w);
        Assert.Equal(98, b.Armor, 9);                           // one hit while spinning: 2 points

        for (; after < 181; after++)                            // 1.5 s + 1 step (preflight F6)
        {
            Assert.Equal(VehicleState.Spinning, b.State);
            Step(w);
        }
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.Equal(lateral, b.Lateral, 9);
        Assert.False(BossSpunOrDestroyed(w, b));
    }

    [Fact]
    public void Caltrops_slow_it_for_4_s_without_a_puncture()
    {
        var w = Straight();
        var b = Spawn(w, Two, 200, speed: 52, engaged: false);  // beyond 140 m and not yet engaged
        w.Hazards.Add(new Caltrops { Distance = 201, Lateral = 0 });
        Step(w, 40);

        Assert.False(b.Punctured);
        Assert.Equal(BossDamage.CaltropSlowSeconds, b.SlowLeft);
        Assert.True(b.Engaged);                                 // the slow engages it
        Assert.DoesNotContain(w.Events, e => e is VehiclePunctured);
        Assert.Contains(w.Events, e => e is Message { Text: "BOSS SLOWED BY CALTROPS", Tone: Tone.Good });

        Step(w, 40);                                            // still over the same caltrops: one slow only (preflight F7)
        Assert.Equal(BossDamage.CaltropSlowSeconds - World.Step, b.SlowLeft, 9);

        Run(w, 359, 40);                                        // 3 s after first contact
        Assert.True(b.Speed <= BossDamage.CaltropSlowFactor * 52 + 1, $"Speed {b.Speed}");
        Assert.False(b.Punctured);
        Run(w, 121, 40);                                        // 4.01 s
        Assert.Equal(0, b.SlowLeft);
        Assert.Single(w.Events.OfType<Message>(), m => m.Text == "BOSS SLOWED BY CALTROPS");
    }

    [Fact]
    public void Ramming_pushes_it_aside_but_never_spins_or_wrecks_it()
    {
        var w = Straight();
        var b = Spawn(w, Two, 50, lateral: 5.5, speed: 40);   // near the right edge, so the rams push it against the clamp
        var p = w.Player;
        p.Speed = 40;
        double widest = 0;
        for (int ram = 0; ram < 10; ram++)
        {
            p.Armor = 100;                                      // keep the player in the fight for all ten rams
            p.Distance = b.Distance;
            p.Lateral = b.Lateral - 1.8;
            p.LateralVel = 8;
            p.PushVel = 0;
            Collisions.Resolve(w);                              // a step would overwrite LateralVel
            Assert.Equal(VehicleState.Driving, b.State);
            Assert.Equal(4, b.PushVel, 9);                      // 3 + 1.3 * 8 = 13.4, capped at 4
            for (int i = 0; i < 36; i++)                        // 0.3 s
            {
                Step(w, 40);
                Assert.Equal(VehicleState.Driving, b.State);
                Assert.True(Math.Abs(b.PushVel) <= 4 + 1e-9, $"PushVel {b.PushVel} after ram {ram}");
                widest = Math.Max(widest, Math.Abs(b.Lateral));
            }
            Assert.True(Math.Abs(b.Lateral) <= 6.3 + 1e-9, $"Lateral {b.Lateral} after ram {ram}");   // preflight F7
        }
        Assert.Equal(6.3, widest, 9);                           // the rams really did push it to the edge
        Assert.Equal(100, b.Armor);
        Assert.DoesNotContain(w.Events, e => e is BossDamaged);
        Assert.False(BossSpunOrDestroyed(w, b));
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "SIDESWIPED" });
    }

    [Fact]
    public void Hitting_a_wreck_costs_10_percent_once_and_it_swerves()
    {
        var w = Straight();
        var b = Still(w, Two, 40, speed: 30);
        var wreck = StoppedWreck(42, 1);                        // more room on the left of it
        w.Hazards.Add(wreck);
        Step(w);

        Assert.Equal(90, b.Armor, 9);
        Assert.Equal(-4, b.PushVel, 9);
        Assert.Equal(30 * 0.7, b.Speed, 9);
        Assert.Same(wreck, b.LastWreck);
        Assert.Contains(w.Events, e => e is BossDamaged { Reason: "HIT A WRECK" });
        Assert.Contains(w.Events, e => e is Message { Text: "BOSS HIT A WRECK", Tone: Tone.Good });

        for (int i = 0; i < 120; i++)                           // overlapping it for 1 s
        {
            wreck.Distance = b.Distance;
            wreck.Lateral = b.Lateral;
            Step(w);
        }
        Assert.Equal(90, b.Armor, 9);
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.Contains(b, w.Agents);
        Assert.False(BossSpunOrDestroyed(w, b));

        var other = StoppedWreck(b.Distance + 2, b.Lateral - 1);   // a different wreck hurts again; more room on its right
        w.Hazards.Add(other);
        Step(w);
        Assert.Equal(80, b.Armor, 9);
        Assert.Equal(4, b.PushVel, 9);
        Assert.Same(other, b.LastWreck);
    }

    [Fact]
    public void Smoke_blinds_the_boss_like_any_car()
    {
        var w = Straight();
        var b = Spawn(w, Two, 60, speed: 40);
        w.Hazards.Add(new SmokeCloud { Distance = 60, PrevDistance = 60, Lateral = 0, Speed = 0 });
        Step(w, 40);
        Assert.True(b.Blinded > 0);
    }

    // ---- each hazard acts on the boss once, ever, even when several overlap (fix round 1) ----

    static int Count(World w, string text) => w.Events.OfType<Message>().Count(m => m.Text == text);

    [Fact]
    public void Two_overlapping_wrecks_each_hurt_it_once()
    {
        var w = Straight();
        var b = Spawn(w, Three, 40, lateral: 0, speed: 30);
        var p = w.Player;
        p.Lateral = -5;                                         // passing in the left lane
        w.Hazards.Add(StoppedWreck(44, 0.5));
        w.Hazards.Add(StoppedWreck(44, 1.5));
        for (int i = 0; i < 5 * 120; i++)
        {
            p.Lateral = -5;
            p.LateralVel = p.PushVel = p.Heading = 0;
            Step(w, 30);
            Assert.True(b.Armor >= 80 - 1e-9, $"Armor {b.Armor} at {w.Time:F2} s");
        }

        Assert.NotEqual(VehicleState.Wrecked, b.State);
        Assert.Contains(b, w.Agents);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
        Assert.InRange(Count(w, "BOSS HIT A WRECK"), 1, 2);
        Assert.InRange(w.Events.OfType<BossDamaged>().Count(e => e.Reason == "HIT A WRECK"), 1, 2);
    }

    [Fact]
    public void Two_overlapping_caltrops_sets_each_slow_it_once()
    {
        var w = Straight();
        var b = Spawn(w, Two, 200, speed: 52);
        w.Hazards.Add(new Caltrops { Distance = 202, Lateral = 0 });
        w.Hazards.Add(new Caltrops { Distance = 203, Lateral = 0.5 });
        int rearmed = 0;
        double last = 0;
        for (int i = 0; i < 2 * 120; i++)
        {
            Step(w, 40);
            if (b.SlowLeft > last) rearmed++;
            last = b.SlowLeft;
        }

        Assert.InRange(rearmed, 1, 2);                          // once per set at most
        Assert.InRange(Count(w, "BOSS SLOWED BY CALTROPS"), 1, 2);
        Assert.True(b.SlowLeft > 0 && b.SlowLeft < BossDamage.CaltropSlowSeconds - 1.5, $"SlowLeft {b.SlowLeft}");
        Assert.False(b.Punctured);
    }

    [Fact]
    public void A_slow_boss_spins_once_on_a_slick_and_drives_off_it()
    {
        var w = Straight();
        var b = Spawn(w, new BossDefinition("Test", 100, [new(AgentKind.Runner, 0.5), new(AgentKind.Blocker, 0)]), 40, speed: 8);
        var slick = new OilSlick { Distance = 41, Lateral = 0 };
        w.Hazards.Add(slick);
        bool Over() => Math.Abs(b.Distance - slick.Distance) < OilSlick.Radius + b.Stats.Length / 2
                       && Math.Abs(b.Lateral - slick.Lateral) < OilSlick.Radius + b.Stats.Width / 2;
        double? offAt = null;
        int spinningSteps = 0;
        for (int i = 0; i < 10 * 120; i++)
        {
            Step(w);
            if (b.State == VehicleState.Spinning) spinningSteps++;
            if (offAt is null && !Over()) offAt = w.Time;
        }

        Assert.Equal(1, Count(w, "BOSS SPUN OUT - ARMOR OPEN"));
        Assert.True(spinningSteps <= 181, $"spinning {spinningSteps} steps");   // 1.5 s + 1 step (preflight F6)
        Assert.True(offAt is not null && offAt <= 4, $"still on the slick at {offAt}");
        Assert.False(Over());
        Assert.Equal(VehicleState.Driving, b.State);
    }

    // ---- no path destroys a boss outside its last phase, or skips a phase (Task 10 review) ----

    /// <summary>
    /// A Three boss in phase 1 of 3 (floor 66) takes one blow from <paramref name="source"/>: from 67 it stops at the
    /// floor, and from far below it (1) it keeps its health; either way it moves on exactly one phase and is not destroyed.
    /// </summary>
    [Theory]
    [InlineData("gunfire", 67)]
    [InlineData("gunfire", 1)]
    [InlineData("missile", 67)]
    [InlineData("missile", 1)]
    [InlineData("strike", 67)]
    [InlineData("strike", 1)]
    [InlineData("wreck", 67)]
    [InlineData("wreck", 1)]
    public void A_blow_in_a_non_last_phase_ends_only_that_phase(string source, double armor)
    {
        var w = Straight();
        var b = Still(w, Three, 100);
        b.Armor = armor;
        switch (source)
        {
            case "gunfire":
                Shoot(w, b);
                break;
            case "missile":
                MissileAt(w, b);
                break;
            case "strike":
                w.Loadout.Strike.Restock(1);
                w.Handle(new GameAction(ActionKind.FireStrike));
                break;
            case "wreck":
                w.Hazards.Add(StoppedWreck(102, 0));
                break;
        }
        Run(w, 200);

        Assert.Equal(Math.Min(armor, 66), b.Armor, 9);
        Assert.Equal(1, b.PhaseIndex);
        Assert.Equal(2, Assert.Single(w.Events.OfType<BossPhaseChanged>()).Phase);
        Assert.NotEqual(VehicleState.Wrecked, b.State);
        Assert.Contains(b, w.Agents);
        Assert.DoesNotContain(w.Events, e => e is VehicleDestroyed);
    }

    /// <summary>
    /// Ramming and car contact never hurt the boss (call 19): not the player's rear-ends or sideswipes, and not
    /// another enemy car's. Tried in the first phase just above its floor and in the last phase with 0.25 left.
    /// </summary>
    [Theory]
    [InlineData("player rear-ends it", 0)]
    [InlineData("player rear-ends it", 2)]
    [InlineData("it rear-ends the player", 0)]
    [InlineData("it rear-ends the player", 2)]
    [InlineData("player sideswipes it", 0)]
    [InlineData("player sideswipes it", 2)]
    [InlineData("a car rear-ends it", 0)]
    [InlineData("a car rear-ends it", 2)]
    [InlineData("it rear-ends a car", 0)]
    [InlineData("it rear-ends a car", 2)]
    [InlineData("a car sideswipes it", 0)]
    [InlineData("a car sideswipes it", 2)]
    public void Contact_never_hurts_the_boss(string contact, int phase)
    {
        var w = Straight();
        var b = Still(w, Three, 100, speed: 40);
        b.PhaseIndex = phase;
        double armor = phase == 0 ? 66.25 : 0.25;
        b.Armor = armor;
        var p = w.Player;
        p.Distance = -200;                                      // out of the way unless placed
        p.Speed = 40;
        AgentCar? other = null;
        AgentCar Car(double distance, double lateral, double lateralVel = 0)
        {
            var c = AgentCar.Create(w.NextId(), AgentKind.Blocker, distance, lateral, 40);
            c.Driver = new StillDriver();
            c.LateralVel = lateralVel;
            w.Agents.Add(c);
            return c;
        }
        double otherHalf = VehicleStats.Blocker.Length / 2, bossHalf = VehicleStats.Boss.Length / 2;
        switch (contact)
        {
            case "player rear-ends it":
                p.Distance = b.Distance - (bossHalf + VehicleStats.Player.Length / 2) + 0.1;
                p.Speed = 60;
                break;
            case "it rear-ends the player":
                p.Distance = b.Distance + (bossHalf + VehicleStats.Player.Length / 2) - 0.1;
                p.Speed = 20;
                break;
            case "player sideswipes it":
                p.Distance = b.Distance;
                p.Lateral = -1.8;
                p.LateralVel = 8;
                break;
            case "a car rear-ends it":
                other = Car(b.Distance - (bossHalf + otherHalf) + 0.1, 0);
                break;
            case "it rear-ends a car":
                other = Car(b.Distance + (bossHalf + otherHalf) - 0.1, 0);
                break;
            case "a car sideswipes it":
                other = Car(b.Distance - 0.01, -2.1, lateralVel: 8);   // closing at 8 m/s: a spin for an ordinary car
                break;
        }
        double otherArmor = other?.Armor ?? 0;

        Collisions.Resolve(w);

        Assert.Equal(armor, b.Armor);
        Assert.Equal(phase, b.PhaseIndex);
        Assert.Equal(VehicleState.Driving, b.State);
        Assert.True(Math.Abs(b.PushVel) <= 4 + 1e-9, $"PushVel {b.PushVel}");
        Assert.False(BossSpunOrDestroyed(w, b));
        Assert.DoesNotContain(w.Events, e => e is BossDamaged or BossPhaseChanged);
        if (contact.Contains("rear-ends a car") || contact == "a car rear-ends it")
            Assert.Equal(otherArmor - 0.5, other!.Armor, 9);    // the other car still takes its knock
        if (contact == "a car sideswipes it")
            Assert.Equal(VehicleState.Spinning, other!.State);  // ... and still spins

        Run(w, 60, p.Speed);                                    // nothing later finishes it off
        Assert.Equal(armor, b.Armor);
        Assert.Contains(b, w.Agents);
        Assert.False(BossSpunOrDestroyed(w, b));
    }
}
