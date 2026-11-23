using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

/// <summary>Plan 8: the boss's own pace (a 90 mph floor), its cunning, and its tactics when you hang back.</summary>
public class BossTacticsTests
{
    static World Straight(int seed = 1) => TestWorlds.World(seed, TestWorlds.Straight());

    static BossDefinition First(AgentKind kind, int cunning = 1) =>
        new("Test", 100, [new(kind, 0.5), new(AgentKind.Blocker, 0)]) { Cunning = cunning };

    static BossCar Spawn(World w, BossDefinition def, double distance, double speed = 40, bool engaged = true, double lateral = 0)
    {
        var b = BossCar.Create(w.NextId(), def, distance, lateral, speed);
        b.Engaged = engaged;
        w.Agents.Add(b);
        return b;
    }

    /// <summary>One step with the player held at <paramref name="speed"/> in the centre lane.</summary>
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

    // ---- cunning in the mission file ----

    const string Head = """{ "id": "m", "title": "T", "briefing": "B", "road": { "seed": 7 }, "approachLength": 6000, """;
    const string Waves = """ "waves": [ { "atDistance": 100, "kind": "Runner", "count": 1, "spawnOffset": 400 } ] """;

    static string WithCunning(string? cunning) =>
        Head + "\"boss\": { \"name\": \"Test\", \"health\": 100, "
        + (cunning is null ? "" : $"\"cunning\": {cunning}, ")
        + "\"phases\": [ { \"behaviour\": \"Blocker\", \"endsBelow\": 0.5 }, { \"behaviour\": \"Pursuer\", \"endsBelow\": 0 } ] }, "
        + Waves + " }";

    static string MissionPath(string id) => Path.Combine(AppContext.BaseDirectory, "content", "missions", id + ".json");

    [Fact]
    public void Cunning_is_read_and_checked()
    {
        Assert.Equal(4, MissionLoader.Load(WithCunning("4"), "m.json").Boss!.Cunning);
        Assert.Equal(1, MissionLoader.Load(WithCunning(null), "m.json").Boss!.Cunning);
        foreach (var bad in new[] { "0", "7", "2.5", "\"3\"" })
        {
            var ex = Assert.ThrowsAny<Exception>(() => MissionLoader.Load(WithCunning(bad), "m.json"));
            Assert.Contains("boss.cunning must be a whole number from 1 to 6", ex.Message);
        }

        for (int i = 1; i <= 6; i++)
            Assert.Equal(i, MissionLoader.LoadFile(MissionPath($"mission-0{i}")).Boss!.Cunning);
        Assert.Equal(1, MissionLoader.LoadFile(MissionPath("patrol-ground")).Boss!.Cunning);
        Assert.Equal(1, MissionLoader.LoadFile(MissionPath("proving-ground")).Boss!.Cunning);
    }

    [Theory]
    [InlineData(1, 3.0, 56)]
    [InlineData(2, 2.5, 57)]
    [InlineData(3, 2.0, 59)]
    [InlineData(4, 1.5, 60)]
    [InlineData(5, 1.0, 62)]
    [InlineData(6, 1.0, 63)]
    public void Cunning_sets_reaction_time_and_running_pace(int cunning, double react, double pace)
    {
        Assert.Equal(react, BossCunning.ReactSeconds(cunning));
        Assert.Equal(pace, BossCunning.RunPace(cunning));
    }

    // ---- the floor ----

    [Theory]
    [InlineData(AgentKind.Runner)]
    [InlineData(AgentKind.Blocker)]
    [InlineData(AgentKind.Pursuer)]
    [InlineData(AgentKind.Gunner)]
    [InlineData(AgentKind.Dropper)]
    [InlineData(AgentKind.Armored)]
    public void An_engaged_boss_never_follows_you_below_90_mph(AgentKind phase)
    {
        Assert.Equal(40, BossMind.FloorSpeed);
        var w = Straight();
        var b = Spawn(w, First(phase), 60, speed: 45);
        double? outOfRange = null;
        for (int i = 0; i < 15 * 120; i++)
        {
            Hold(w, 0);
            if (i < 5 * 120) Assert.True(b.Speed >= BossMind.FloorSpeed - 1e-9, $"{phase}: {b.Speed:0.00} m/s at step {i}");
            if (outOfRange is null && Gap(w, b) > 380) outOfRange = w.Time;
        }
        Assert.NotNull(outOfRange);
    }

    [Fact]
    public void Above_the_floor_phases_still_pace_you()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, speed: 55);
        for (int i = 0; i < 5 * 120; i++)
        {
            Hold(w, 55);
            Assert.InRange(Gap(w, b), 55, 75);
        }
    }

    /// <summary>
    /// The floor acts only while the boss is ahead of you: behind you in your line, a phase that never overtakes must
    /// still be free to wait slower than you (here, stopped) rather than be pushed into your back.
    /// </summary>
    [Theory]
    [InlineData(AgentKind.Blocker, 0, 0)]
    [InlineData(AgentKind.Armored, 0, 0)]
    [InlineData(AgentKind.Blocker, 5, 5.5)]                     // outer lane: no way past on its side, so it waits behind
    [InlineData(AgentKind.Armored, -5, -5.5)]
    public void A_boss_behind_a_stopped_player_never_drives_into_them(AgentKind phase, double playerLateral, double bossLateral)
    {
        var w = Straight();
        var b = Spawn(w, First(phase), -25, speed: 10, lateral: bossLateral);
        for (int i = 0; i < 6 * 120; i++) Hold(w, 0, playerLateral);
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged);
    }

    [Fact]
    public void Caltrops_still_take_it_below_the_floor()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, speed: 40);
        b.SlowLeft = 3;
        var intent = BossDriver.Instance.Decide(b, new WorldView(w), World.Step);
        Assert.Equal(BossMind.FloorSpeed * BossDamage.CaltropSlowFactor, intent.TargetSpeed, 9);
    }

    [Fact]
    public void A_spinning_boss_is_not_held_at_the_floor()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 70, speed: 40);
        b.State = VehicleState.Spinning;
        b.StateTimer = 2;
        for (int i = 0; i < 120; i++) Hold(w, 0);
        Assert.True(b.Speed < BossMind.FloorSpeed, $"{b.Speed:0.00}");
    }

    [Fact]
    public void A_boss_far_ahead_no_longer_waits_for_you()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 700, speed: 50);
        w.Player.Speed = 10;
        var intent = BossDriver.Instance.Decide(b, new WorldView(w), World.Step);
        Assert.True(intent.TargetSpeed >= BossMind.FloorSpeed, $"{intent.TargetSpeed:0.00}");
    }

    [Fact]
    public void A_boss_easing_in_still_drives_below_you()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Runner), 400, speed: 20, engaged: false);
        w.Player.Speed = 20;
        var intent = BossDriver.Instance.Decide(b, new WorldView(w), World.Step);
        Assert.Equal(20 - BossCar.ArrivalSlower, intent.TargetSpeed, 9);
    }

    // ---- tactics when you hang back (Task 3) ----

    /// <summary>Steps with the player held at <paramref name="speed"/> until the boss starts a tactic (or the time is up); the time it started, or null.</summary>
    static double? UntilTactic(World w, BossCar b, double speed, double seconds = 8)
    {
        for (int i = 0; i < seconds * 120; i++)
        {
            Hold(w, speed);
            if (b.Mind.Tactic != BossTactic.None) return w.Time;
        }
        return null;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Hanging_back_is_noticed_after_the_cunning_reaction_time(int cunning)
    {
        double react = BossCunning.ReactSeconds(cunning);
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Blocker, cunning), 30);
        double? at = UntilTactic(w, b, 10);
        Assert.NotNull(at);
        Assert.InRange(at!.Value, react - 2 * World.Step, react + 2 * World.Step);

        // Speeding back up before then starts the wait over.
        var w2 = Straight();
        var b2 = Spawn(w2, First(AgentKind.Blocker, cunning), 30);
        for (int i = 0; i < (react - 0.5) * 120; i++) Hold(w2, 10);
        Hold(w2, 25);
        double restart = w2.Time;
        double? at2 = UntilTactic(w2, b2, 10);
        Assert.InRange(at2!.Value - restart, react - 2 * World.Step, react + 2 * World.Step);
    }

    [Fact]
    public void Never_while_the_boss_is_behind_you_or_not_engaged()
    {
        var w = Straight();
        var behind = Spawn(w, First(AgentKind.Blocker, 6), -40, speed: 5);
        Assert.Null(UntilTactic(w, behind, 10, 4));

        var w2 = Straight();
        var easing = Spawn(w2, First(AgentKind.Blocker, 6), 200, speed: 5, engaged: false);
        for (int i = 0; i < 4 * 120; i++) { Hold(w2, 10); easing.Engaged = false; }
        Assert.Equal(BossTactic.None, easing.Mind.Tactic);
    }

    static readonly SeededRng Rng = new(3);

    [Fact]
    public void Cunning_1_always_runs()
    {
        foreach (var weapon in new[] { BossTactic.None, BossTactic.ShootBack, BossTactic.DropHazards })
            for (int i = 0; i < 20; i++)
                Assert.Equal(BossTactic.Run, BossMind.Pick(1, weapon, canRam: true, hurt: false, youBadlyHurt: true, BossTactic.None, Rng));
    }

    static HashSet<BossTactic> Picks(int cunning, BossTactic weapon, bool canRam = false, bool hurt = false, bool youBadlyHurt = false,
        BossTactic lastFailed = BossTactic.None)
    {
        var rng = new SeededRng(11);
        var seen = new HashSet<BossTactic>();
        for (int i = 0; i < 60; i++) seen.Add(BossMind.Pick(cunning, weapon, canRam, hurt, youBadlyHurt, lastFailed, rng));
        return seen;
    }

    [Fact]
    public void Cunning_2_uses_its_phase_weapon_or_runs()
    {
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ShootBack, BossTactic.Run }, Picks(2, BossTactic.ShootBack, canRam: true));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.DropHazards, BossTactic.Run }, Picks(2, BossTactic.DropHazards));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.Run }, Picks(2, BossTactic.None, canRam: true));
    }

    [Fact]
    public void Cunning_3_may_ram_but_never_when_hurt()
    {
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ShootBack, BossTactic.Run, BossTactic.ReverseRam }, Picks(3, BossTactic.ShootBack, canRam: true));
        Assert.DoesNotContain(BossTactic.ReverseRam, Picks(3, BossTactic.ShootBack, canRam: true, hurt: true));
        Assert.DoesNotContain(BossTactic.ReverseRam, Picks(3, BossTactic.None, canRam: false));

        // In the world: at 39 % health, or beyond 80 m, it never rams.
        foreach (var (health, gap) in new[] { (39.0, 25.0), (100.0, 70.0) })
            for (int seed = 1; seed <= 12; seed++)
            {
                var w = Straight(seed);
                var b = Spawn(w, First(AgentKind.Blocker, 3), gap);
                b.PhaseIndex = 1;
                b.Armor = health;
                UntilTactic(w, b, 15);
                Assert.NotEqual(BossTactic.ReverseRam, b.Mind.Tactic);
            }
    }

    [Fact]
    public void Cunning_4_picks_what_fits()
    {
        Assert.Equal(new HashSet<BossTactic> { BossTactic.Run }, Picks(4, BossTactic.ShootBack, canRam: true, hurt: true));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ShootBack }, Picks(4, BossTactic.ShootBack, canRam: true, youBadlyHurt: true));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ReverseRam }, Picks(4, BossTactic.DropHazards, canRam: true, youBadlyHurt: true));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.DropHazards }, Picks(4, BossTactic.DropHazards, canRam: true));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ReverseRam }, Picks(4, BossTactic.None, canRam: true));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.Run }, Picks(4, BossTactic.None, canRam: false));
    }

    [Fact]
    public void Cunning_6_never_repeats_a_tactic_that_failed()
    {
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ShootBack }, Picks(5, BossTactic.ShootBack, canRam: true, lastFailed: BossTactic.ShootBack));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.ReverseRam }, Picks(6, BossTactic.ShootBack, canRam: true, lastFailed: BossTactic.ShootBack));
        Assert.Equal(new HashSet<BossTactic> { BossTactic.Run }, Picks(6, BossTactic.None, canRam: true, lastFailed: BossTactic.ReverseRam));
    }

    [Fact]
    public void A_tactic_that_did_you_no_harm_is_remembered_as_failed()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Gunner, 4), 30);
        w.Player.Armor = 30;                                    // badly hurt: a cunning-4 Gunner shoots back
        Assert.NotNull(UntilTactic(w, b, 10));
        Assert.Equal(BossTactic.ShootBack, b.Mind.Tactic);
        for (int i = 0; i < 7 * 120 && b.Mind.Tactic == BossTactic.ShootBack; i++) { w.Player.Armor = 30; Hold(w, 10); }
        Assert.Equal(BossTactic.ShootBack, b.Mind.LastFailed);
    }

    [Fact]
    public void A_run_goes_at_its_pace_and_ends_when_you_close_in()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Blocker, 1), 30);
        Assert.NotNull(UntilTactic(w, b, 10));
        Assert.Equal(BossTactic.Run, b.Mind.Tactic);
        Assert.Equal("BOSS MAKING A RUN FOR IT", w.Announcement?.Title);
        for (int i = 0; i < 8 * 120; i++) Hold(w, 10);
        Assert.Equal(BossCunning.RunPace(1), b.Speed, 1);
        Assert.Single(w.Events.OfType<BossTacticStarted>());

        // You close in at speed: once within 200 m the run ends and the phase takes over.
        w.Player.Distance = b.Distance - 150;
        Hold(w, 60);
        Assert.Equal(BossTactic.None, b.Mind.Tactic);
    }

    [Fact]
    public void A_running_boss_is_always_caught_even_with_a_lower_top_speed()
    {
        var w = Straight();
        w.Feel.MaxSpeedMph = 100;                               // the F12 panel's lower top speed: 44.7 m/s
        var b = Spawn(w, First(AgentKind.Blocker, 1), 30);      // always runs; its pace (56 m/s) is above your 44.7
        Assert.NotNull(UntilTactic(w, b, 10));
        Assert.Equal(BossTactic.Run, b.Mind.Tactic);
        for (int i = 0; i < 10 * 120; i++) Hold(w, 10);
        Assert.Equal(w.Feel.MaxSpeed - BossMind.RunBelowYourTop, b.Speed, 1);
        Assert.True(b.Speed < w.Feel.MaxSpeed - 5);
    }

    [Fact]
    public void Shoot_back_and_drop_lift_the_floor_for_up_to_6_s()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Gunner, 4), 30);
        w.Player.Armor = 30;
        double start = UntilTactic(w, b, 10)!.Value;
        Assert.Equal(BossTactic.ShootBack, b.Mind.Tactic);
        Assert.Equal("BOSS SHOOTING BACK", w.Announcement?.Title);
        double slowest = double.MaxValue;
        while (b.Mind.Tactic == BossTactic.ShootBack && w.Time < start + 8)
        {
            Hold(w, 10);
            slowest = Math.Min(slowest, b.Speed);
        }
        Assert.True(slowest < BossMind.FloorSpeed - 5, $"slowest {slowest:0.0} m/s");
        Assert.InRange(w.Time - start, BossMind.WeaponSeconds - 0.05, BossMind.WeaponSeconds + 0.05);
        Assert.Equal(BossTactic.None, b.Mind.Tactic);           // cunning 4: no chain

        // A cunning-5 boss chains a run onto it.
        var w5 = Straight();
        var b5 = Spawn(w5, First(AgentKind.Gunner, 5), 30);
        w5.Player.Armor = 30;
        UntilTactic(w5, b5, 10);
        Assert.Equal(BossTactic.ShootBack, b5.Mind.Tactic);
        for (int i = 0; i < 7 * 120 && b5.Mind.Tactic == BossTactic.ShootBack; i++) Hold(w5, 10);
        Assert.Equal(BossTactic.Run, b5.Mind.Tactic);

        // Speeding up ends it at once and the floor comes back.
        var wf = Straight();
        var bf = Spawn(wf, First(AgentKind.Gunner, 4), 30);
        wf.Player.Armor = 30;
        UntilTactic(wf, bf, 10);
        Hold(wf, 30);
        Assert.Equal(BossTactic.None, bf.Mind.Tactic);
    }

    [Fact]
    public void Cunning_4_dropper_drops_hazards_under_the_floor()
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Dropper, 4), 30);
        UntilTactic(w, b, 10);
        Assert.Equal(BossTactic.DropHazards, b.Mind.Tactic);
    }

    /// <summary>A cunning-4 Blocker 25 m ahead of a player creeping at 15 m/s: it chooses the ram.</summary>
    static (World W, BossCar B) RamScene(double health = 100)
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Blocker, 4), 25);
        b.PhaseIndex = 1;
        b.Armor = health;
        Assert.NotNull(UntilTactic(w, b, 15));
        Assert.Equal(BossTactic.ReverseRam, b.Mind.Tactic);
        return (w, b);
    }

    [Fact]
    public void The_reverse_ram_brakes_shows_lights_reverses_and_runs()
    {
        var (w, b) = RamScene();
        var heat = w.Heat.Level;
        var stages = new List<RamStage>();
        double? lightsAt = null, reverseAt = null;
        double armorBefore = w.Player.Armor, healthBefore = b.Armor;
        for (int i = 0; i < 12 * 120 && b.Mind.Tactic == BossTactic.ReverseRam; i++)
        {
            if (stages.Count == 0 || stages[^1] != b.Mind.RamStage) stages.Add(b.Mind.RamStage);
            if (b.Mind.RamStage == RamStage.Lights) { lightsAt ??= w.Time; Assert.True(b.ReversingLights); Assert.True(b.Speed >= -1e-9); }
            if (b.Mind.RamStage == RamStage.Reverse) { reverseAt ??= w.Time; Assert.True(b.Speed >= -BossMind.ReverseSpeed - 1e-9); }
            if (b.Mind.RamStage == RamStage.Brake) Assert.False(b.ReversingLights);
            Hold(w, 15);
        }
        Assert.Equal(new[] { RamStage.Brake, RamStage.Lights, RamStage.Reverse }, stages);
        Assert.InRange(reverseAt!.Value - lightsAt!.Value, 0.95, 1.05);
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "THE BOSS BACKED INTO YOU", Amount: 10 });
        Assert.DoesNotContain(w.Events, e => e is PlayerDamaged { Reason: "RAMMED" });
        Assert.Equal(armorBefore - 10, w.Player.Armor, 6);
        Assert.Equal(healthBefore - 0.04 * b.MaxHealth, b.Armor, 6);
        Assert.Equal(heat, w.Heat.Level);
        Assert.Equal(BossTactic.Run, b.Mind.Tactic);
        Assert.Contains(w.Events, e => e is BossTacticStarted { Tactic: BossTactic.ReverseRam });
    }

    [Fact]
    public void The_reverse_ram_shows_BOSS_REVERSING_when_the_lights_come_on()
    {
        var (w, b) = RamScene();
        for (int i = 0; i < 6 * 120 && b.Mind.RamStage != RamStage.Lights; i++) Hold(w, 15);
        Hold(w, 15);
        Assert.Equal("BOSS REVERSING", w.Announcement?.Title);
    }

    [Fact]
    public void A_ram_never_destroys_the_boss()
    {
        var (w, b) = RamScene(health: 41);
        for (int i = 0; i < 12 * 120 && b.Mind.Tactic == BossTactic.ReverseRam; i++) Hold(w, 15);
        Assert.True(b.Armor >= 37 - 1e-9, $"{b.Armor:0.0}");
        Assert.NotEqual(VehicleState.Wrecked, b.State);

        // Even forced on a boss with almost nothing left, the ram's own damage stops short of the kill.
        var w2 = Straight();
        var low = Spawn(w2, First(AgentKind.Blocker, 4), 5);
        low.PhaseIndex = 1;
        low.Armor = 2;
        w2.BossBackedIntoPlayer(low);
        Assert.True(low.Armor > 0);
        Assert.NotEqual(VehicleState.Wrecked, low.State);
    }

    [Fact]
    public void Hurt_mid_ram_it_gives_up_and_runs()
    {
        var (w, b) = RamScene();
        Hold(w, 15);
        b.Armor = 39;
        Hold(w, 15);
        Assert.Equal(BossTactic.Run, b.Mind.Tactic);
    }

    [Fact]
    public void A_boss_killed_while_reversing_gets_its_finale()
    {
        var (w, b) = RamScene();
        for (int i = 0; i < 12 * 120 && !(b.Mind.Reversing && b.Speed < -5); i++) Hold(w, 2);
        Assert.True(b.Speed < -5, $"{b.Speed:0.0}");
        w.Destroy(b, KillCause.Gunfire);
        Assert.True(w.InFinale);
        Assert.True(w.Finale!.Speed >= 0, $"{w.Finale.Speed:0.0}");
        for (int i = 0; i < 6 * 120 && !w.Finale.Done; i++) w.StepOnce(ControlFrame.Empty);
        Assert.True(w.Finale.Done);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, true)]
    public void The_pursuer_gives_up_getting_behind_you(int cunning, bool runs)
    {
        var w = Straight();
        var b = Spawn(w, First(AgentKind.Pursuer, cunning), 2, speed: 15);
        b.DriverState = new BossPursuitDriver.State { Phase = BossPursuitPhase.DropBack, Target = 2.7 };
        b.Lateral = 2.7;
        double react = BossCunning.ReactSeconds(cunning);
        bool gaveUp = false;
        for (int i = 0; i < (react + 0.1) * 120; i++)
        {
            Hold(w, 15);
            if (b.Mind.Tactic != BossTactic.None || b.DriverState is BossPursuitDriver.State { Phase: BossPursuitPhase.Overtake or BossPursuitPhase.HoldAhead })
                gaveUp = true;
            if (gaveUp) break;
        }
        Assert.True(gaveUp);
        Assert.Equal(runs ? BossTactic.Run : BossTactic.None, b.Mind.Tactic);
        Assert.True(Gap(w, b) > 0);
    }

    [Fact]
    public void Tactics_are_repeatable()
    {
        ulong Play()
        {
            var w = Straight(7);
            var b = Spawn(w, First(AgentKind.Gunner, 3), 25);
            for (int i = 0; i < 12 * 120; i++) Hold(w, 12);
            return w.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
