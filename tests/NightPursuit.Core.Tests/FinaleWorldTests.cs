using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class FinaleWorldTests
{
    static BossDefinition Def => new("Test", 100, [new(AgentKind.Runner, 0)]);

    /// <summary>A straight road, the player at 500 m doing <paramref name="playerSpeed"/>, the boss <paramref name="ahead"/> m ahead.</summary>
    static (World W, BossCar Boss) Setup(double ahead = 30, double lateral = 0, double bossSpeed = 60, double playerSpeed = 60,
        FinaleChoice? choice = null)
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Distance = 500;
        w.Player.Speed = playerSpeed;
        w.Player.SnapPrev();
        var boss = BossCar.Create(w.NextId(), Def, 500 + ahead, lateral, bossSpeed);
        boss.Driver = new StillDriver();
        w.Agents.Add(boss);
        if (choice is { } c) w.FinaleSource = () => c;
        return (w, boss);
    }

    static void Steps(World w, double seconds, ControlFrame? input = null)
    {
        int n = (int)Math.Round(seconds / World.Step);
        for (int i = 0; i < n; i++) w.StepOnce(input ?? ControlFrame.Empty);
    }

    public static TheoryData<KillCause> Causes() => new() { KillCause.Missile, KillCause.Gunfire, KillCause.Satellite, KillCause.Rammed };

    [Theory]
    [MemberData(nameof(Causes))]
    public void Destroying_the_boss_starts_a_finale_and_leaves_no_road_wreck(KillCause cause)
    {
        var choice = new FinaleChoice(FinaleKind.Flip, -1, 2);
        var (w, boss) = Setup(choice: choice);
        int before = w.Score;
        Assert.Null(w.Finale);
        Assert.False(w.InFinale);

        w.Destroy(boss, cause);

        Assert.True(w.InFinale);
        Assert.Equal(boss.Id, w.Finale!.BossId);
        Assert.Equal(choice, w.Finale.Choice);
        Assert.Equal(VehicleState.Wrecked, boss.State);
        Assert.Empty(w.Hazards.OfType<Wreck>());
        Assert.Equal(before + 5000, w.Score);
        Assert.Contains(w.Events, e => e is Message { Text: "TEST DESTROYED  +5000", Tone: Tone.Good });
        var started = Assert.Single(w.Events.OfType<BossFinaleStarted>());
        Assert.Equal((boss.Id, FinaleKind.Flip, -1), (started.VehicleId, started.Kind, started.Side));

        w.StepOnce(ControlFrame.Empty);
        Assert.Contains(boss, w.Agents);                      // a gunfire, missile or strike kill no longer removes it
        Assert.Empty(w.Hazards.OfType<Wreck>());
    }

    [Fact]
    public void The_finale_starts_at_least_as_fast_as_the_player()
    {
        var (w, boss) = Setup(bossSpeed: 40, playerSpeed: 60);
        w.Destroy(boss, KillCause.Missile);
        Assert.Equal(60, w.Finale!.Speed);
    }

    [Fact]
    public void The_boss_explodes_once_onto_a_burning_verge_wreck_and_is_removed()
    {
        var (w, boss) = Setup(choice: new FinaleChoice(FinaleKind.Roll, 1, 1));
        w.Destroy(boss, KillCause.Missile);
        var events = new List<GameEvent>();
        while (!w.Finale!.Exploded)
        {
            w.StepOnce(ControlFrame.Empty);
            Assert.True(w.Finale.Elapsed < 3, "it should have exploded at 2.5 s");
        }
        var boom = Assert.Single(w.Events.OfType<BossExploded>());
        Assert.Equal((w.Finale.Distance, w.Finale.Lateral), (boom.Distance, boom.Lateral));
        Assert.DoesNotContain(boss, w.Agents);
        var wreck = Assert.Single(w.Hazards.OfType<Wreck>());
        Assert.Equal((boom.Distance, boom.Lateral, 0.0), (wreck.Distance, wreck.Lateral, wreck.Speed));
        Assert.True(Math.Abs(wreck.Lateral) > Track.RoadHalfWidth);

        Steps(w, 2);
        Assert.Single(w.Events.OfType<BossExploded>());
        Assert.True(w.Finale.Done);
    }

    [Fact]
    public void Nothing_hurts_the_player_during_the_finale()
    {
        var (w, boss) = Setup();
        w.StartPolice(30);
        w.Destroy(boss, KillCause.Missile);
        var p = w.Player;

        w.DamagePlayer(50, "X");
        w.WreckPlayer("X");
        w.BustPlayer();
        w.AddHeat(40, "X");
        w.SetHeat(90, "X");
        Assert.Equal((100.0, VehicleState.Driving, false), (p.Armor, p.State, w.BustedPending));
        Assert.Equal(30, w.Heat.Value);

        var civ = AgentCar.Create(w.NextId(), AgentKind.Civilian, p.Distance, p.Lateral, p.Speed);
        w.Agents.Add(civ);
        w.Hazards.Add(new OilSlick { Distance = p.Distance + 3, Lateral = p.Lateral, Hostile = true });
        w.Hazards.Add(new Caltrops { Distance = p.Distance + 6, Lateral = p.Lateral, Hostile = true });
        for (int i = 0; i < 120; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            Assert.Equal(100, p.Armor);
            Assert.Equal(VehicleState.Driving, p.State);
            Assert.Equal(0, p.GripLoss);
            Assert.Equal(0, p.PunctureLeft);
        }
        Assert.Equal(30, w.Heat.Value);
    }

    [Fact]
    public void Nothing_scores_or_raises_heat_during_the_finale()
    {
        var (w, boss) = Setup();
        w.StartPolice(30);
        w.Destroy(boss, KillCause.Missile);
        int score = w.Score;
        var p = w.Player;

        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, p.Distance + 60, -5, 20);
        var civ = AgentCar.Create(w.NextId(), AgentKind.Civilian, p.Distance + 70, 5, 20);
        w.Agents.Add(runner);
        w.Agents.Add(civ);
        w.Hazards.Add(new Caltrops { Distance = runner.Distance + 1, Lateral = runner.Lateral });   // the player's caltrops
        w.StepOnce(ControlFrame.Empty);
        Assert.True(runner.Punctured);
        w.BlamePlayer(civ);
        w.Destroy(runner, KillCause.Gunfire);
        w.Destroy(civ, KillCause.Rammed);
        Steps(w, 2);

        Assert.Equal(score, w.Score);
        Assert.Equal(30, w.Heat.Value);
    }

    [Fact]
    public void Agents_do_not_fire_and_fire_in_flight_is_cleared()
    {
        var (w, boss) = Setup();
        var p = w.Player;
        w.EnemyMissiles.Add(new EnemyMissile { Id = 1, Distance = p.Distance + 40, Lateral = p.Lateral, Dir = -1, LaunchTime = 0 });
        w.Tracers.Add(new Tracer { Pose = new RoadPose(p.Distance + 30, p.Lateral, Math.PI), OwnerId = boss.Id, Hostile = true });
        var gunner = AgentCar.Create(w.NextId(), AgentKind.Gunner, p.Distance + 25, p.Lateral, p.Speed);
        w.Agents.Add(gunner);

        w.Destroy(boss, KillCause.Missile);
        Assert.Empty(w.EnemyMissiles);
        Assert.DoesNotContain(w.Tracers, t => t.Hostile);
        for (int i = 0; i < 240; i++)
        {
            w.StepOnce(ControlFrame.Empty);
            Assert.DoesNotContain(w.Tracers, t => t.Hostile);
            Assert.Empty(w.EnemyMissiles);
        }
    }

    [Fact]
    public void The_finale_boss_touches_nothing()
    {
        var (w, boss) = Setup(ahead: 30, lateral: 0, choice: new FinaleChoice(FinaleKind.SpinOff, 1, 0));
        var civ = AgentCar.Create(w.NextId(), AgentKind.Civilian, boss.Distance + 20, 3, 20);
        civ.Driver = new StillDriver();
        w.Agents.Add(civ);
        w.Destroy(boss, KillCause.Missile);
        while (!w.Finale!.Exploded)
        {
            w.StepOnce(ControlFrame.Empty);
            Assert.Equal(VehicleState.Driving, civ.State);
            Assert.Equal(civ.Stats.Armor, civ.Armor);
        }
    }

    public static TheoryData<double, double, double, FinaleKind> Starts()
    {
        var data = new TheoryData<double, double, double, FinaleKind>();
        foreach (var kind in Enum.GetValues<FinaleKind>())
        {
            data.Add(5, 60, 60, kind);      // right in front: guns range
            data.Add(30, 60, 60, kind);
            data.Add(120, 60, 60, kind);    // a missile kill
            data.Add(5, 69, 69, kind);      // flat out
            data.Add(30, 40, 60, kind);     // a slow boss
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Starts))]
    public void The_player_drives_itself_and_keeps_the_crash_in_view(double ahead, double bossSpeed, double playerSpeed, FinaleKind kind)
    {
        var (w, boss) = Setup(ahead, 0, bossSpeed, playerSpeed, new FinaleChoice(kind, 1, 1));
        var p = w.Player;
        w.Destroy(boss, KillCause.Missile);
        var hands = new ControlFrame { SteerDir = -1, Fire = true, Gas = true };   // ignored throughout
        double boomAt = double.NaN;
        while (!w.Finale!.Done)
        {
            w.StepOnce(hands);
            Assert.DoesNotContain(w.Tracers, t => t.OwnerId == p.Id);
            Assert.InRange(p.Lateral, -0.5, 0.5);
            if (w.Finale.Exploded && double.IsNaN(boomAt))
            {
                boomAt = w.Finale.Distance;
                Assert.InRange(boomAt - p.Distance, 40, 150);
            }
            if (!double.IsNaN(boomAt)) Assert.True(p.Distance <= boomAt - 30, $"the player passed the crash: {boomAt - p.Distance:0.0} m");
        }
    }

    [Fact]
    public void Same_choice_same_finale()
    {
        var choice = new FinaleChoice(FinaleKind.Roll, -1, 2);
        var (a, ba) = Setup(choice: choice);
        var (b, bb) = Setup(choice: choice);
        a.Destroy(ba, KillCause.Missile);
        b.Destroy(bb, KillCause.Missile);
        for (int i = 0; i < 500; i++)
        {
            a.StepOnce(ControlFrame.Empty);
            b.StepOnce(ControlFrame.Empty);
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    [Fact]
    public void The_finale_changes_the_state_hash()
    {
        var (a, ba) = Setup(choice: new FinaleChoice(FinaleKind.Roll, 1, 1));
        var (b, bb) = Setup(choice: new FinaleChoice(FinaleKind.Flip, 1, 1));
        a.Destroy(ba, KillCause.Missile);
        b.Destroy(bb, KillCause.Missile);
        Steps(a, 1.6);
        Steps(b, 1.6);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
