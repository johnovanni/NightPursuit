using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class GunnerTests
{
    const double Dt = 1.0 / 120;
    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static AgentCar Gunner(World w, double ahead, double lateral)
    {
        var g = AgentCar.Create(w.NextId(), AgentKind.Gunner, w.Player.Distance + ahead, lateral, 30);
        w.Agents.Add(g);
        return g;
    }

    static void Step(World w) { w.Player.Speed = 30; w.StepOnce(ControlFrame.Empty); }

    static GunnerDriver.State StateOf(AgentCar g) => (GunnerDriver.State)g.DriverState!;

    /// <summary>A gunner ahead of a lane-0 player, forced into Hold with its timer spent, so the next step starts the Aim.</summary>
    static AgentCar ReadyGunner(World w, double ahead = 45, double lateral = 0)
    {
        var g = Gunner(w, ahead, lateral);
        Step(w);                                   // lets the driver create its state
        var s = StateOf(g);
        s.Phase = GunnerPhase.Hold; s.Timer = 0;
        return g;
    }

    static void StepUntil(World w, Func<bool> done, int max = 2000)
    {
        for (int i = 0; i < max && !done(); i++) Step(w);
        Assert.True(done(), "condition never reached");
    }

    [Fact]
    public void Gunner_gets_ahead_and_holds_35_to_55_m()
    {
        var w = Straight();
        var g = Gunner(w, -40, 5);
        for (int i = 0; i < 15 * 120; i++) Step(w);
        double dd = g.Distance - w.Player.Distance;
        Assert.InRange(dd, 35, 55);
    }

    [Fact]
    public void Gunner_aims_0_6_s_before_its_first_shot()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        StepUntil(w, () => GunnerDriver.IsAiming(g));
        Assert.Equal(GunnerPhase.Aim, StateOf(g).Phase);
        int steps = 0;
        while (w.Tracers.Count(t => t.Hostile) == 0 && steps < 500) { Step(w); steps++; }
        Assert.True(steps * Dt >= 0.6 - 1e-9, $"shot after {steps * Dt} s");
        Assert.True(steps * Dt <= 0.6 + 0.05);
    }

    [Fact]
    public void Gunner_burst_hits_the_player_in_its_lane()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        StepUntil(w, () => StateOf(g).Phase == GunnerPhase.Hold && w.Tracers.Count == 0 && w.Player.Armor < 100, 3000);
        Assert.Equal(88, w.Player.Armor, 9);
    }

    [Fact]
    public void Gunner_burst_misses_one_lane_over()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        StepUntil(w, () => StateOf(g).Phase == GunnerPhase.Burst);
        w.Player.Lateral = 5; w.Player.SnapPrev();
        int hold = 0;
        for (int i = 0; i < 250; i++)      // this burst and its flight, before the gunner can line up on lane 5
        {
            w.Player.Lateral = 5; w.Player.SnapPrev();
            Step(w);
            if (StateOf(g).Phase == GunnerPhase.Hold) hold++;
        }
        Assert.Equal(100, w.Player.Armor);
        Assert.True(hold > 0);
    }

    [Fact]
    public void Gunner_stops_following_you_once_it_aims()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        StepUntil(w, () => GunnerDriver.IsAiming(g));
        while (GunnerDriver.IsAiming(g))
        {
            w.Player.Lateral = 5; w.Player.SnapPrev();
            Step(w);
            Assert.True(Math.Abs(g.Lateral) < 0.3, $"gunner drifted to {g.Lateral}");
        }
        Assert.Equal(100, w.Player.Armor);
    }

    [Fact]
    public void Blinded_gunner_does_not_fire()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        for (int i = 0; i < 360; i++)
        {
            g.Blinded = 3;
            Step(w);
            Assert.DoesNotContain(w.Tracers, t => t.Hostile);
        }
        Assert.Equal(100, w.Player.Armor);
    }

    [Fact]
    public void Enemy_tracers_never_hit_agents()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        var runner = AgentCar.Create(w.NextId(), AgentKind.Runner, w.Player.Distance + 20, 0, 30);
        runner.Driver = new StillDriver();
        w.Agents.Add(runner);
        bool crossed = false;
        for (int i = 0; i < 400; i++)
        {
            runner.Lateral = 0; runner.LateralVel = 0;
            Step(w);
            foreach (var t in w.Tracers)
                if (t.Hostile && World.SweptHit(t.PrevDistance, t.PrevLateral, t.Pose.Distance, t.Pose.Lateral, runner, 0)) crossed = true;
        }
        Assert.True(crossed, "no hostile tracer ever crossed the runner");
        Assert.Equal(6, runner.Armor, 9);
    }

    [Fact]
    public void Gunner_hits_message_at_most_once_per_second()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        for (int i = 0; i < 400; i++) Step(w);   // one burst; the next aim starts after 2.5 s of Hold
        // StepOnce keeps events until the next Advance, so they can be counted at the end.
        Assert.Equal(6, w.Events.OfType<PlayerDamaged>().Count());
        Assert.Equal(1, w.Events.OfType<Message>().Count(m => m.Text == "SHOT BY A GUNNER"));
    }

    [Fact]
    public void Hostile_tracers_are_created_at_their_spawn_pose()
    {
        var w = Straight();
        var g = ReadyGunner(w);
        StepUntil(w, () => w.Tracers.Any(t => t.Hostile));
        var t = w.Tracers.First(x => x.Hostile);
        Assert.Equal(t.Pose.Distance, t.PrevDistance, 9);
        Assert.Equal(t.Pose.Lateral, t.PrevLateral, 9);
        Assert.Equal(Math.PI, t.PrevHeading, 9);
        Assert.Equal(160, t.MuzzleSpeed);
        Assert.Equal(120, t.Range);
        Assert.Equal(g.Id, t.OwnerId);
    }
}
