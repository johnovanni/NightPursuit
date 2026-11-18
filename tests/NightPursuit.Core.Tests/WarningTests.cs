using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class WarningTests
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

    /// <summary>A car that stays exactly where it is put, <paramref name="gap"/> metres from the player.</summary>
    static AgentCar Car(World w, AgentKind kind, double gap, double lateral = 0, double? speed = null)
    {
        var car = AgentCar.Create(w.NextId(), kind, w.Player.Distance + gap, lateral, speed ?? w.Player.Speed);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    static EnemyMissile Missile(World w, double gap, double lateral = 0, int dir = 1)
    {
        var m = new EnemyMissile
        {
            Id = 99, Distance = w.Player.Distance + gap, Lateral = lateral, TargetLateral = lateral, Dir = dir,
            PrevDistance = w.Player.Distance + gap, PrevLateral = lateral,
        };
        w.EnemyMissiles.Add(m);
        return m;
    }

    static void Aim(AgentCar gunner) =>
        gunner.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Aim, Target = gunner.Lateral };

    static Warning Single(World w, WarningKind kind) => Assert.Single(Warnings.Collect(w), x => x.Kind == kind);

    static bool Has(World w, WarningKind kind) => Warnings.Collect(w).Any(x => x.Kind == kind);

    [Fact]
    public void Missile_warning_shows_every_step_from_launch_to_outcome()
    {
        var w = Straight();
        Missile(w, 100);
        var first = Warnings.Collect(w)[0];
        Assert.Equal(WarningKind.MissileIncoming, first.Kind);
        Assert.Equal("MISSILE INCOMING AHEAD  IMPACT 3.9 s", first.Text);
        Assert.True(first.ShowArrow);

        bool ended = false;
        for (int i = 0; i < 700 && !ended; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            ended = w.Events.Any(e => e is EnemyMissileEnded);
            if (ended) Assert.False(Has(w, WarningKind.MissileIncoming));
            else Assert.Equal(WarningKind.MissileIncoming, Warnings.Collect(w)[0].Kind);
        }
        Assert.True(ended);
    }

    [Fact]
    public void Missile_warning_names_direction_and_side()
    {
        var w = Straight();
        Missile(w, -90, lateral: -5, dir: -1);
        var warning = Single(w, WarningKind.MissileIncoming);
        Assert.StartsWith("MISSILE INCOMING BEHIND LEFT  IMPACT 3.5 s", warning.Text);
        Assert.Equal(-1, warning.Side);
    }

    [Fact]
    public void At_most_four_warnings_show_missile_first()
    {
        var w = Straight();
        Missile(w, 100);
        Car(w, AgentKind.Pursuer, -40);
        Aim(Car(w, AgentKind.Gunner, 45));
        w.Hazards.Add(new Wreck { Distance = w.Player.Distance + 70, Lateral = 0 });
        Car(w, AgentKind.Blocker, 60, 0.5);

        var list = Warnings.Collect(w);
        Assert.Equal(4, list.Count);
        Assert.Equal(
            new[] { WarningKind.MissileIncoming, WarningKind.GunnerAiming, WarningKind.WreckAhead, WarningKind.BlockerAhead },
            list.Select(x => x.Kind).ToArray());
    }

    [Fact]
    public void Gunner_aiming_warns_while_in_your_lane()
    {
        var w = Straight();
        var gunner = Car(w, AgentKind.Gunner, 45);
        Aim(gunner);
        Assert.Equal("GUNNER AIMING - LEAVE ITS LANE", Single(w, WarningKind.GunnerAiming).Text);
        w.Player.Lateral = 5;
        Assert.False(Has(w, WarningKind.GunnerAiming));
        w.Player.Lateral = 0;
        gunner.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Hold };
        Assert.False(Has(w, WarningKind.GunnerAiming));
    }

    [Fact]
    public void Gunner_blinded_by_smoke_mid_aim_raises_no_warning()
    {
        var w = Straight();
        var gunner = Car(w, AgentKind.Gunner, 45);
        Aim(gunner);
        Assert.True(Has(w, WarningKind.GunnerAiming));

        w.Hazards.Add(new SmokeCloud { Distance = gunner.Distance, PrevDistance = gunner.Distance, Lateral = gunner.Lateral, Speed = w.Player.Speed });
        w.StepOnce(new ControlFrame { Gas = true });
        Assert.True(gunner.Blinded > 0);
        Assert.IsType<GunnerDriver.State>(gunner.DriverState);   // its plan is kept for when it can see again
        Assert.False(GunnerDriver.IsAiming(gunner));
        Assert.False(Has(w, WarningKind.GunnerAiming));
        Assert.DoesNotContain(w.Events, e => e is WarningRaised { Kind: WarningKind.GunnerAiming });

        gunner.DriverState = new GunnerDriver.State { Phase = GunnerPhase.Burst };   // nor while its burst would be on
        Assert.False(Has(w, WarningKind.GunnerAiming));
    }

    [Fact]
    public void Hostile_oil_or_caltrops_ahead_warn_by_name_and_own_ones_do_not()
    {
        var w = Straight();
        w.Hazards.Add(new OilSlick { Distance = w.Player.Distance + 80, Lateral = 0, Hostile = true });
        Assert.Equal("OIL AHEAD", Single(w, WarningKind.HazardAhead).Text);
        w.Hazards.Clear();
        w.Hazards.Add(new Caltrops { Distance = w.Player.Distance + 80, Lateral = 0, Hostile = true });
        Assert.Equal("CALTROPS AHEAD", Single(w, WarningKind.HazardAhead).Text);

        w.Hazards.Clear();
        w.Hazards.Add(new OilSlick { Distance = w.Player.Distance + 80, Lateral = 0 });
        w.Hazards.Add(new Caltrops { Distance = w.Player.Distance + 80, Lateral = 0 });
        Assert.False(Has(w, WarningKind.HazardAhead));

        w.Hazards.Clear();
        w.Hazards.Add(new OilSlick { Distance = w.Player.Distance + 80, Lateral = 5, Hostile = true });
        Assert.False(Has(w, WarningKind.HazardAhead));
    }

    [Fact]
    public void Any_hostile_closing_from_behind_warns_with_distance_and_side()
    {
        var w = Straight();
        var gunner = Car(w, AgentKind.Gunner, -40, 5, w.Player.Speed + 5);
        var warning = Single(w, WarningKind.HostileBehind);
        Assert.Equal("GUNNER BEHIND RIGHT 40 m", warning.Text);
        Assert.Equal(1, warning.Side);
        Assert.True(warning.ShowArrow);

        gunner.Speed = w.Player.Speed + 2;
        Assert.False(Has(w, WarningKind.HostileBehind));
        gunner.Speed = w.Player.Speed + 5;
        gunner.Distance = w.Player.Distance - 70;
        Assert.False(Has(w, WarningKind.HostileBehind));

        w.Agents.Clear();
        Car(w, AgentKind.Runner, -20, -5, w.Player.Speed + 10);
        var runner = Single(w, WarningKind.HostileBehind);
        Assert.Equal("RUNNER BEHIND LEFT 20 m", runner.Text);
        Assert.Equal(-1, runner.Side);

        w.Agents.Clear();
        Car(w, AgentKind.Armored, -40, 3, w.Player.Speed + 8);
        Assert.Equal("ARMORED BEHIND RIGHT 40 m", Single(w, WarningKind.HostileBehind).Text);
    }

    static BossDefinition Boss => new("Test", 100, [new(AgentKind.Blocker, 0.5), new(AgentKind.Pursuer, 0)]);

    static BossCar AddBoss(World w, double gap, double lateral = 0)
    {
        var b = BossCar.Create(w.NextId(), Boss, w.Player.Distance + gap, lateral, w.Player.Speed);
        b.Driver = new StillDriver();
        w.Agents.Add(b);
        return b;
    }

    [Fact]
    public void Boss_behind_warns_within_120_m()
    {
        var w = Straight();
        var boss = AddBoss(w, -60);
        var warning = Single(w, WarningKind.BossBehind);
        Assert.Equal("BOSS BEHIND 60 m", warning.Text);
        Assert.Equal(0, warning.Side);
        Assert.True(warning.ShowArrow);
        Assert.False(Has(w, WarningKind.HostileBehind));
        boss.Distance = w.Player.Distance - 130;
        Assert.False(Has(w, WarningKind.BossBehind));
        Assert.False(Has(w, WarningKind.HostileBehind));
        Assert.False(Has(w, WarningKind.PursuerBehind));
    }

    [Fact]
    public void Blocker_ahead_now_shows_an_arrow()
    {
        var w = Straight();
        var blocker = Car(w, AgentKind.Blocker, 60, 1.5);
        var warning = Single(w, WarningKind.BlockerAhead);
        Assert.Equal("BLOCKER AHEAD", warning.Text);
        Assert.True(warning.ShowArrow);
        Assert.Equal(1, warning.Side);
        blocker.Lateral = 0.5;
        Assert.Equal(0, Single(w, WarningKind.BlockerAhead).Side);
    }

    [Fact]
    public void Plan_1_warning_texts_are_unchanged()
    {
        var w = Straight();
        var pursuer = Car(w, AgentKind.Pursuer, -40, 0);
        Assert.Equal("PURSUER BEHIND  40 m", Single(w, WarningKind.PursuerBehind).Text);
        pursuer.Lateral = 3;
        var right = Single(w, WarningKind.PursuerBehind);
        Assert.Equal("PURSUER BEHIND RIGHT  40 m", right.Text);
        Assert.Equal(1, right.Side);
        Assert.True(right.ShowArrow);

        pursuer.Distance = w.Player.Distance - 1;
        pursuer.Lateral = -3;
        var alongside = Single(w, WarningKind.PursuerAlongside);
        Assert.Equal("PURSUER LEFT", alongside.Text);
        Assert.Equal(-1, alongside.Side);

        w.Agents.Clear();
        w.Hazards.Add(new Wreck { Distance = w.Player.Distance + 100, Lateral = 1 });
        var wreck = Single(w, WarningKind.WreckAhead);
        Assert.Equal("WRECK AHEAD", wreck.Text);
        Assert.False(wreck.ShowArrow);
    }

    [Fact]
    public void Pursuers_on_both_sides_show_both_arrows_and_on_one_side_only_that_one()
    {
        var w = Straight();
        Car(w, AgentKind.Pursuer, -1, -3);
        Car(w, AgentKind.Pursuer, -1, 3);
        Assert.Equal(0, Single(w, WarningKind.PursuerAlongside).Side);

        w.Agents.Clear();
        Car(w, AgentKind.Pursuer, -1, 3);
        Car(w, AgentKind.Pursuer, -40, 3);
        var one = Single(w, WarningKind.PursuerAlongside);
        Assert.Equal(1, one.Side);
        Assert.Equal("PURSUER RIGHT", one.Text);

        w.Agents.Clear();
        Car(w, AgentKind.Pursuer, -40, 3);
        Car(w, AgentKind.Pursuer, -60, -3);
        var behind = Single(w, WarningKind.PursuerBehind);
        Assert.Equal("PURSUER BEHIND RIGHT  40 m", behind.Text);   // the nearest one's words
        Assert.Equal(0, behind.Side);                              // but both edges, as both are on the road behind
    }

    [Fact]
    public void A_strike_that_changes_the_boss_phase_leaves_the_phase_banner_showing()
    {
        var w = Straight();
        w.Loadout.Strike.Restock(2);
        var boss = BossCar.Create(w.NextId(), Boss, w.Player.Distance + 100, 0, 30);
        boss.Driver = new StillDriver();
        boss.Armor = 70;
        w.Agents.Add(boss);
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.StartsWith("SATELLITE STRIKE", w.Announcement!.Title);
        for (int i = 0; i < 200 && !w.Events.Any(e => e is StrikeLanded); i++) w.StepOnce(ControlFrame.Empty);
        Assert.Contains(w.Events, e => e is BossPhaseChanged);
        Assert.StartsWith("PHASE 2", w.Announcement!.Title);
        Assert.Contains(w.Events.OfType<Message>(), m => m.Text.StartsWith("SATELLITE STRIKE: "));

        // Pressing strike during the fresh phase banner does not replace it either (it still says so in a message).
        w.Loadout.Strike.Restock(1);
        Car(w, AgentKind.Runner, 80, -5);
        int before = w.Events.Count;
        w.Handle(new GameAction(ActionKind.FireStrike));
        Assert.StartsWith("PHASE 2", w.Announcement!.Title);
        var msgs = w.Events.Skip(before).OfType<Message>().Select(m => m.Text).ToList();
        Assert.True(msgs.Contains("SATELLITE STRIKE: TARGETING"), string.Join("|", msgs));
    }

    [Fact]
    public void A_new_warning_publishes_WarningRaised_once()
    {
        var w = Straight();
        var gunner = Car(w, AgentKind.Gunner, 45);
        w.StepOnce(ControlFrame.Empty);
        Assert.DoesNotContain(w.Events, e => e is WarningRaised);

        Aim(gunner);
        w.StepOnce(ControlFrame.Empty);
        var raised = Assert.Single(w.Events.OfType<WarningRaised>());
        Assert.Equal(WarningKind.GunnerAiming, raised.Kind);
        Assert.Equal("GUNNER AIMING - LEAVE ITS LANE", raised.Text);
        for (int i = 0; i < 5; i++) w.StepOnce(ControlFrame.Empty);
        Assert.Single(w.Events.OfType<WarningRaised>());

        gunner.DriverState = null;
        w.StepOnce(ControlFrame.Empty);
        Aim(gunner);
        w.StepOnce(ControlFrame.Empty);
        Assert.Equal(2, w.Events.OfType<WarningRaised>().Count());
    }

    [Fact]
    public void Boss_appearance_and_phase_change_are_announced()
    {
        var run = new MissionRun(
            new MissionDefinition("t", "T", "B", new TrackProfile(7), 500, 5, [], Boss: Boss), new FeelSettings(), 3);
        var w = run.World;
        w.Player.Distance = 500;
        w.Player.Speed = 40;
        Assert.Null(w.Announcement);
        run.Director.Update(w);
        var a = w.Announcement!;
        Assert.Equal("BOSS: TEST", a.Title);
        Assert.Equal("DESTROY IT TO FINISH THE MISSION", a.Detail);
        Assert.Equal(w.Time + 3, a.Until, 9);

        var boss = run.Director.Boss!;
        boss.Shield = 0;
        w.DamageBoss(boss, 60, KillCause.Gunfire, "GUNFIRE");
        var phase = w.Announcement!;
        Assert.Equal("PHASE 2 OF 2: PURSUER", phase.Title);
        Assert.Equal("LET IT PASS, THEN SHOOT", phase.Detail);
        Assert.Equal(w.Time + 2.5, phase.Until, 9);
    }

    [Fact]
    public void A_phase_that_fires_missiles_says_so()
    {
        var w = Straight();
        var def = new BossDefinition("Test", 100, [new(AgentKind.Gunner, 0.5), new(AgentKind.Armored, 0, FiresMissiles: true)]);
        var boss = BossCar.Create(w.NextId(), def, 700, 0, 30);
        w.Agents.Add(boss);
        w.DamageBoss(boss, 60, KillCause.Gunfire, "GUNFIRE");
        Assert.Equal("BULLETS DO A QUARTER - USE MISSILES AND HAZARDS  -  FIRES MISSILES", w.Announcement!.Detail);
    }

    [Fact]
    public void Strike_is_announced_while_targeting_and_after()
    {
        var w = Straight();
        w.Loadout.Strike.Restock(1);
        Car(w, AgentKind.Runner, 100, -5);
        Car(w, AgentKind.Gunner, 150, 5);
        w.Handle(new GameAction(ActionKind.FireStrike));
        var targeting = w.Announcement!;
        Assert.Equal("SATELLITE STRIKE", targeting.Title);
        Assert.Equal("TARGETING 2 HOSTILES", targeting.Detail);
        Assert.Equal(w.Time + 1.5, targeting.Until, 9);

        for (int i = 0; i < 200 && !w.Events.Any(e => e is StrikeLanded); i++) w.StepOnce(ControlFrame.Empty);
        var landed = w.Announcement!;
        Assert.Equal("STRIKE: 2 DESTROYED", landed.Title);
        Assert.Equal("", landed.Detail);
        for (int i = 0; i < 192; i++) w.StepOnce(ControlFrame.Empty);   // 1.6 s
        Assert.Null(w.Announcement);
    }

    static string ProvingGroundPath => Path.Combine(AppContext.BaseDirectory, "content", "missions", "proving-ground.json");

    [Fact]
    public void No_event_goes_unshown()
    {
        // The scripted run of EnemyMissileTests: every weapon, the boss and its missiles, wrecks and respawns.
        var run = new MissionRun(MissionLoader.LoadFile(ProvingGroundPath), new FeelSettings(), 9);
        var w = run.World;
        var seen = new HashSet<Type>();
        int raisedChecked = 0;
        var announced = new List<(double Start, string Title)>();
        for (int step = 0; step < 120 * 120; step++)
        {
            if (step % 60 == 0 && EnemyMissileTests.ActionAt(step) is ActionKind action)
            {
                int pre = w.Events.Count;
                run.Handle(new GameAction(action));
                if (w.Announcement is { } early) announced.Add((early.Start, early.Title));
                Check(w, pre, run, seen, announced, ref raisedChecked);
            }
            int before = w.Events.Count;
            run.Update(new ControlFrame { Gas = true, Fire = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(w) }, World.Step);
            if (w.Announcement is { } a) announced.Add((a.Start, a.Title));
            Check(w, before, run, seen, announced, ref raisedChecked);
        }
        Assert.True(raisedChecked > 0, "no WarningRaised in the whole run");
        Assert.Contains(typeof(EnemyMissileEnded), seen);
        Assert.Contains(typeof(BossPhaseChanged), seen);
        Assert.Contains(typeof(StrikeLanded), seen);
    }

    static void Check(World w, int from, MissionRun run, HashSet<Type> seen, List<(double Start, string Title)> announced, ref int raisedChecked)
    {
        var now = w.Events.Skip(from).ToList();
        var messageTimes = now.OfType<Message>().Select(m => m.Time).ToHashSet();
        foreach (var e in now)
        {
            seen.Add(e.GetType());
            switch (e)
            {
                case BossDamaged or Message: break;
                case PlayerDamaged:
                    Assert.True(w.Player.HitFlash > 0, $"no flash at {e.Time:0.00}");
                    break;
                case WarningRaised r:
                    Assert.Contains(Warnings.Collect(w), x => x.Kind == r.Kind);
                    raisedChecked++;
                    break;
                case BossAppeared or BossPhaseChanged or StrikeLanded:
                    string prefix = e switch { BossAppeared => "BOSS:", BossPhaseChanged => "PHASE ", _ => "STRIKE:" };
                    bool shown = announced.Any(x => x.Start == e.Time && x.Title.StartsWith(prefix, StringComparison.Ordinal));
                    // The one legitimate exception: a strike result while a boss banner holds the screen (it has its Message).
                    bool held = e is StrikeLanded && w.Announcement is { Urgent: true } && messageTimes.Contains(e.Time);
                    Assert.True(shown || held, $"{e.GetType().Name} at {e.Time:0.00} has no {prefix} announcement");
                    break;
                default:
                    Assert.True(messageTimes.Contains(e.Time), $"{e.GetType().Name} at {e.Time:0.00} has no message");
                    break;
            }
        }
    }
}
