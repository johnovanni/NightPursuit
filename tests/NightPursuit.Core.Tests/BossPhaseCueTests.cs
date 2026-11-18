using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

/// <summary>A boss shows the warnings and on-car cues of the car its phase drives like, in words that name the boss.</summary>
public class BossPhaseCueTests
{
    /// <summary>A straight road with the player at 500 m doing 30 m/s.</summary>
    static World Straight()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Distance = 500;
        w.Player.Speed = 30;
        w.Player.SnapPrev();
        return w;
    }

    static BossDefinition First(AgentKind kind) => new("Test", 100, [new(kind, 0.5), new(AgentKind.Runner, 0)]);

    /// <summary>A boss in a <paramref name="phase"/> phase, <paramref name="gap"/> m from the player, held still by a test driver.</summary>
    static BossCar StillBoss(World w, AgentKind phase, double gap, double lateral = 0)
    {
        var b = BossCar.Create(w.NextId(), First(phase), w.Player.Distance + gap, lateral, w.Player.Speed);
        b.Driver = new StillDriver();
        b.Engaged = true;
        w.Agents.Add(b);
        return b;
    }

    /// <summary>A boss in a <paramref name="phase"/> phase driven by its own driver, already engaged.</summary>
    static BossCar DrivenBoss(World w, AgentKind phase, double gap, double lateral = 0)
    {
        var b = BossCar.Create(w.NextId(), First(phase), w.Player.Distance + gap, lateral, w.Player.Speed);
        b.Engaged = true;
        b.Shield = 0;
        w.Agents.Add(b);
        return b;
    }

    static AgentCar Car(World w, AgentKind kind, double gap, double lateral = 0)
    {
        var car = AgentCar.Create(w.NextId(), kind, w.Player.Distance + gap, lateral, w.Player.Speed);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    /// <summary>One step with the player held at <paramref name="speed"/> in <paramref name="lateral"/>, kept alive.</summary>
    static void Hold(World w, double speed, double lateral = 0)
    {
        var p = w.Player;
        p.Speed = speed;
        p.Lateral = lateral;
        p.LateralVel = 0;
        p.PushVel = 0;
        p.Heading = 0;
        p.Armor = 100;
        w.Advance(ControlFrame.Empty, World.Step);   // one step; Events then holds only this step's
    }

    static Warning Single(World w, WarningKind kind) => Assert.Single(Warnings.Collect(w), x => x.Kind == kind);
    static bool Has(World w, WarningKind kind) => Warnings.Collect(w).Any(x => x.Kind == kind);

    // ---- Gunner phase

    [Fact]
    public void Boss_in_a_Gunner_phase_warns_while_it_aims_in_your_lane()
    {
        var w = Straight();
        var boss = StillBoss(w, AgentKind.Gunner, 45);
        boss.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Aim, Target = 0 };
        var warning = Single(w, WarningKind.GunnerAiming);
        Assert.Equal("BOSS AIMING - LEAVE ITS LANE", warning.Text);

        w.Player.Lateral = 5;                       // out of its lane
        Assert.False(Has(w, WarningKind.GunnerAiming));
        w.Player.Lateral = 0;
        boss.Distance = w.Player.Distance + 130;    // out of range
        Assert.False(Has(w, WarningKind.GunnerAiming));
        boss.Distance = w.Player.Distance + 45;
        boss.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Hold };
        Assert.False(Has(w, WarningKind.GunnerAiming));
    }

    [Fact]
    public void Boss_aiming_beats_a_further_gunner_and_the_nearer_one_speaks()
    {
        var w = Straight();
        var boss = StillBoss(w, AgentKind.Gunner, 70);
        boss.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Burst };
        var gunner = Car(w, AgentKind.Gunner, 40);
        gunner.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Aim };
        Assert.Equal("GUNNER AIMING - LEAVE ITS LANE", Single(w, WarningKind.GunnerAiming).Text);
        gunner.Distance = w.Player.Distance + 90;
        Assert.Equal("BOSS AIMING - LEAVE ITS LANE", Single(w, WarningKind.GunnerAiming).Text);
    }

    [Fact]
    public void Boss_aim_state_in_another_phase_raises_no_gunner_warning()
    {
        var w = Straight();
        var boss = StillBoss(w, AgentKind.Dropper, 45);
        boss.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Aim };
        Assert.False(Has(w, WarningKind.GunnerAiming));
    }

    [Fact]
    public void Boss_gunner_phase_warns_on_every_aiming_step_and_its_hits_name_it()
    {
        // The review's probe: 20 s of a Gunner-phase boss with you sitting in one lane.
        var w = Straight();
        w.Player.Speed = 40;
        var boss = DrivenBoss(w, AgentKind.Gunner, 60);
        int aiming = 0, unwarned = 0, hits = 0;
        bool warnedBeforeFirstHit = false;
        for (int i = 0; i < 20 * 120; i++)
        {
            Hold(w, 40);
            if (w.Events.OfType<WarningRaised>().Any(r => r.Text == "BOSS AIMING - LEAVE ITS LANE") && hits == 0)
                warnedBeforeFirstHit = true;
            foreach (var d in w.Events.OfType<PlayerDamaged>())
            {
                Assert.Equal("SHOT BY THE BOSS", d.Reason);
                hits++;
            }
            double dz = boss.Distance - w.Player.Distance;
            if (GunnerDriver.IsAiming(boss) && dz > 0 && dz <= 120 && Math.Abs(boss.Lateral - w.Player.Lateral) < 2)
            {
                aiming++;
                if (!Warnings.Collect(w).Any(x => x is { Kind: WarningKind.GunnerAiming, Text: "BOSS AIMING - LEAVE ITS LANE" }))
                    unwarned++;
            }
        }
        Assert.True(aiming > 0, "the boss never aimed");
        Assert.True(hits > 0, "the boss never hit");
        Assert.Equal(0, unwarned);
        Assert.True(warnedBeforeFirstHit);
    }

    [Fact]
    public void A_boss_bullet_says_SHOT_BY_THE_BOSS()
    {
        var w = Straight();
        var boss = StillBoss(w, AgentKind.Gunner, 30);
        boss.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Burst };
        var shotBy = new List<string>();
        for (int i = 0; i < 60 && shotBy.Count == 0; i++)
        {
            boss.Distance = w.Player.Distance + 30;
            boss.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Burst };
            boss.Driver = new FiringDriver();
            w.Advance(ControlFrame.Empty, World.Step);
            shotBy.AddRange(w.Events.OfType<Message>().Select(m => m.Text).Where(t => t.StartsWith("SHOT BY")));
        }
        Assert.Equal(["SHOT BY THE BOSS"], shotBy);

        // An ordinary gunner's bullets still say what they always did.
        var w2 = Straight();
        var gunner = Car(w2, AgentKind.Gunner, 30);
        gunner.Driver = new FiringDriver();
        var shotBy2 = new List<string>();
        for (int i = 0; i < 60 && shotBy2.Count == 0; i++)
        {
            gunner.Distance = w2.Player.Distance + 30;
            w2.Advance(ControlFrame.Empty, World.Step);
            shotBy2.AddRange(w2.Events.OfType<Message>().Select(m => m.Text).Where(t => t.StartsWith("SHOT BY")));
        }
        Assert.Equal(["SHOT BY A GUNNER"], shotBy2);
    }

    /// <summary>Holds the car where it is with its trigger held.</summary>
    sealed class FiringDriver : IDriver
    {
        public DriveIntent Decide(AgentCar self, WorldView view, double dt) => new(self.Speed, self.Lateral, Fire: true);
    }

    // ---- Pursuer phase

    [Fact]
    public void Boss_in_a_Pursuer_phase_alongside_warns_by_side_with_its_arrow()
    {
        var w = Straight();
        var boss = StillBoss(w, AgentKind.Pursuer, -1, 3);
        var right = Single(w, WarningKind.PursuerAlongside);
        Assert.Equal("BOSS RIGHT", right.Text);
        Assert.Equal(1, right.Side);
        Assert.True(right.ShowArrow);

        boss.Lateral = -3;
        var left = Single(w, WarningKind.PursuerAlongside);
        Assert.Equal("BOSS LEFT", left.Text);
        Assert.Equal(-1, left.Side);

        boss.Distance = w.Player.Distance + 3.5;    // slightly ahead: still about to ram
        Assert.Equal("BOSS LEFT", Single(w, WarningKind.PursuerAlongside).Text);
        boss.Distance = w.Player.Distance + 4;
        Assert.False(Has(w, WarningKind.PursuerAlongside));

        boss.Distance = w.Player.Distance - 3;       // from 3 m behind, BOSS BEHIND takes over
        Assert.False(Has(w, WarningKind.PursuerAlongside));
        Assert.Equal("BOSS BEHIND LEFT 3 m", Single(w, WarningKind.BossBehind).Text);
        Assert.False(Has(w, WarningKind.PursuerBehind));
    }

    [Fact]
    public void Boss_in_a_Pursuer_phase_counts_as_a_pursuer_for_the_edge_arrows()
    {
        var w = Straight();
        StillBoss(w, AgentKind.Pursuer, -1, 3);
        Car(w, AgentKind.Pursuer, -40, -3);
        var alongside = Single(w, WarningKind.PursuerAlongside);
        Assert.Equal("BOSS RIGHT", alongside.Text);
        Assert.Equal(0, alongside.Side);             // pursuers on both sides: both edges
        var behind = Single(w, WarningKind.PursuerBehind);
        Assert.Equal("PURSUER BEHIND LEFT  40 m", behind.Text);
        Assert.Equal(0, behind.Side);

        // The boss alongside is named even when an ordinary pursuer is alongside too.
        w.Agents.Clear();
        StillBoss(w, AgentKind.Pursuer, -2, 3);
        Car(w, AgentKind.Pursuer, 1, -3);
        Assert.Equal("BOSS RIGHT", Single(w, WarningKind.PursuerAlongside).Text);
    }

    [Fact]
    public void Boss_alongside_in_another_phase_is_not_a_pursuer()
    {
        var w = Straight();
        StillBoss(w, AgentKind.Blocker, -1, 3);
        Assert.False(Has(w, WarningKind.PursuerAlongside));
        Car(w, AgentKind.Pursuer, -40, -3);
        Assert.Equal(-1, Single(w, WarningKind.PursuerBehind).Side);
    }

    [Fact]
    public void Boss_pursuer_phase_is_always_warned_when_close_behind_or_alongside()
    {
        // The review's probe: 60 s of a Pursuer-phase boss with you holding the centre lane at 40 m/s.
        var w = Straight();
        w.Player.Speed = 40;
        var boss = DrivenBoss(w, AgentKind.Pursuer, -50);
        int close = 0, unwarned = 0, contacts = 0, unwarnedContacts = 0;
        bool warnedLastStep = false;
        for (int i = 0; i < 60 * 120; i++)
        {
            Hold(w, 40);
            if (w.Events.OfType<PlayerDamaged>().Any(d => d.Reason is "RAMMED" or "SIDESWIPED" or "HIT FROM BEHIND"))
            {
                contacts++;
                if (!warnedLastStep) unwarnedContacts++;
            }
            double dz = boss.Distance - w.Player.Distance;
            bool warned = Warnings.Collect(w).Any(x => x.Text.StartsWith("BOSS "));
            if (dz > -6 && dz < 4)
            {
                close++;
                if (!warned) unwarned++;
            }
            warnedLastStep = warned;
        }
        Assert.True(close > 0, "the boss never came close");
        Assert.True(contacts > 0, "the boss never touched you");
        Assert.Equal(0, unwarned);
        Assert.Equal(0, unwarnedContacts);
    }

    // ---- Blocker phase

    [Fact]
    public void Boss_in_a_Blocker_phase_ahead_in_your_lane_warns_by_name()
    {
        var w = Straight();
        var boss = StillBoss(w, AgentKind.Blocker, 60, 1.5);
        var warning = Single(w, WarningKind.BlockerAhead);
        Assert.Equal("BOSS BLOCKING AHEAD", warning.Text);
        Assert.True(warning.ShowArrow);
        Assert.Equal(1, warning.Side);

        boss.Lateral = 4;                       // not in your lane
        Assert.False(Has(w, WarningKind.BlockerAhead));
        boss.Lateral = 0;
        Car(w, AgentKind.Blocker, 30, 0.5);     // the nearer one speaks
        Assert.Equal("BLOCKER AHEAD", Single(w, WarningKind.BlockerAhead).Text);
    }

    [Fact]
    public void Boss_ahead_in_another_phase_is_not_a_blocker()
    {
        var w = Straight();
        StillBoss(w, AgentKind.Runner, 60);
        Assert.False(Has(w, WarningKind.BlockerAhead));
    }

    // ---- Dropper phase

    [Fact]
    public void Boss_dropper_phase_arms_on_the_boss_before_each_drop_and_names_the_boss()
    {
        var w = Straight();
        w.Player.Speed = 40;
        var boss = DrivenBoss(w, AgentKind.Dropper, 150);
        int armedSteps = 0, drops = 0;
        var messages = new List<string>();
        for (int i = 0; i < 30 * 120; i++)
        {
            Hold(w, 40);
            bool dropped = w.Events.OfType<WeaponUsed>().Any(u => u.OwnerId == boss.Id);
            if (dropped)
            {
                drops++;
                // DropperDriver's arming state is on the boss itself (so the renderer can light its chute) for the
                // 0.5 s before the drop.
                Assert.True(armedSteps >= 59, $"drop {drops} came after only {armedSteps} armed steps");
                w.Hazards.RemoveAll(h => h.Hostile);      // keep the player undisturbed
            }
            messages.AddRange(w.Events.OfType<Message>().Select(m => m.Text));
            armedSteps = DropperDriver.IsArming(boss) ? armedSteps + 1 : 0;
        }
        Assert.True(drops >= 2, $"only {drops} drops");
        Assert.Contains("BOSS: OIL AHEAD", messages);
        Assert.Contains("BOSS: CALTROPS AHEAD", messages);
        Assert.DoesNotContain(messages, m => m.StartsWith("DROPPER:"));
    }

    [Fact]
    public void An_ordinary_dropper_still_says_DROPPER()
    {
        var w = Straight();
        w.Player.Speed = 40;
        var d = AgentCar.Create(w.NextId(), AgentKind.Dropper, w.Player.Distance + 150, 0, 40);
        w.Agents.Add(d);
        var messages = new List<string>();
        for (int i = 0; i < 15 * 120; i++)
        {
            Hold(w, 40);
            messages.AddRange(w.Events.OfType<Message>().Select(m => m.Text));
            w.Hazards.RemoveAll(h => h.Hostile);
        }
        Assert.Contains("DROPPER: OIL AHEAD", messages);
    }
}
