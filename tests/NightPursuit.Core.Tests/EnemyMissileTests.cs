using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class EnemyMissileTests
{
    const double Dt = World.Step;
    const int StepsPerSecond = 120;

    /// <summary>A boss whose first phase drives as a pursuer and fires missiles.</summary>
    static BossDefinition MissileBoss => new("Test", 100, [new(AgentKind.Pursuer, 0.5, FiresMissiles: true), new(AgentKind.Pursuer, 0)]);

    /// <summary>A straight road with the player at 500 m doing <paramref name="speed"/>.</summary>
    static World Straight(double speed = 30)
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Distance = 500;
        w.Player.Speed = speed;
        w.Player.SnapPrev();
        return w;
    }

    /// <summary>The missile boss in phase 0, engaged, <paramref name="gap"/> metres from the player at the player's speed.</summary>
    static BossCar AddBoss(World w, double gap)
    {
        var b = BossCar.Create(w.NextId(), MissileBoss, w.Player.Distance + gap, 0, w.Player.Speed);
        b.Engaged = true;
        b.PhaseIndex = 0;
        w.Agents.Add(b);
        return b;
    }

    /// <summary>
    /// One step with the player's speed held at <paramref name="speed"/>; a <paramref name="pinned"/> boss is put
    /// <paramref name="gap"/> metres from the player first, at the player's speed.
    /// </summary>
    static void Step(World w, double speed, ControlFrame? input = null, BossCar? pinned = null, double gap = 0)
    {
        w.Player.Speed = speed;
        if (pinned is not null)
        {
            pinned.Distance = w.Player.Distance + gap;
            pinned.Speed = speed;
        }
        w.StepOnce(input ?? ControlFrame.Empty);
    }

    /// <summary>A missile placed by hand (not launched), already in flight.</summary>
    static EnemyMissile Place(World w, double gap, int dir, double lateral)
    {
        var m = new EnemyMissile
        {
            Id = 99, Distance = w.Player.Distance + gap, Lateral = lateral, TargetLateral = lateral,
            LaunchTime = w.Time, Dir = dir,
        };
        m.PrevDistance = m.Distance;
        m.PrevLateral = m.Lateral;
        w.EnemyMissiles.Add(m);
        return m;
    }

    // StepOnce never clears World.Events (only Advance does), so these see everything since the world was made.
    static List<EnemyMissileLaunched> Launches(World w) => w.Events.OfType<EnemyMissileLaunched>().ToList();
    static List<EnemyMissileEnded> Ends(World w) => w.Events.OfType<EnemyMissileEnded>().ToList();
    static bool Said(World w, string text, Tone tone) => w.Events.Any(e => e is Message m && m.Text == text && m.Tone == tone);

    /// <summary>Steps (boss pinned) until the boss launches; fails after 10 s.</summary>
    static EnemyMissile WaitForLaunch(World w, BossCar boss, double gap, double speed)
    {
        for (int i = 0; i < 10 * StepsPerSecond && w.EnemyMissiles.Count == 0; i++) Step(w, speed, pinned: boss, gap: gap);
        return Assert.Single(w.EnemyMissiles);
    }

    /// <summary>Steps with <paramref name="inputFor"/> until a missile ends; fails after 10 s.</summary>
    static EnemyMissileEnded WaitForEnd(World w, double speed, Func<ControlFrame>? inputFor = null)
    {
        for (int i = 0; i < 10 * StepsPerSecond && Ends(w).Count == 0; i++) Step(w, speed, inputFor?.Invoke());
        return Assert.Single(Ends(w));
    }

    [Fact]
    public void Launch_is_refused_closer_than_60_m_or_beyond_160_m()
    {
        var w = Straight();
        var boss = AddBoss(w, 50);
        for (int i = 0; i < 10 * StepsPerSecond; i++) Step(w, 30, pinned: boss, gap: 50);
        Assert.Empty(Launches(w));
        for (int i = 0; i < 10 * StepsPerSecond; i++) Step(w, 30, pinned: boss, gap: 170);
        Assert.Empty(Launches(w));
        Assert.True(boss.Arms.Missiles!.Ready);

        // 3 s at 100 m: one launch, and the rack is then cooling down.
        for (int i = 0; i < 3 * StepsPerSecond; i++) Step(w, 30, pinned: boss, gap: 100);
        Assert.Single(Launches(w));
        Assert.False(boss.Arms.Missiles.Ready);
    }

    /// <summary>A driver that always asks for a missile, to test the world's own checks apart from the boss driver's.</summary>
    sealed class AlwaysFiresDriver : IDriver
    {
        public DriveIntent Decide(AgentCar self, WorldView view, double dt) => new(self.Speed, self.Lateral, FireMissile: true);
    }

    [Fact]
    public void World_refuses_a_launch_out_of_range_or_with_one_in_flight_and_keeps_the_rack_ready()
    {
        var w = Straight();
        var boss = AddBoss(w, 50);
        boss.Driver = new AlwaysFiresDriver();
        for (int i = 0; i < 60; i++) Step(w, 30, pinned: boss, gap: 50);
        for (int i = 0; i < 60; i++) Step(w, 30, pinned: boss, gap: -170);
        var flying = Place(w, 150, 1, 4);
        for (int i = 0; i < 60; i++) Step(w, 30, pinned: boss, gap: 100);
        Assert.Empty(Launches(w));
        Assert.True(boss.Arms.Missiles!.Ready);

        w.EnemyMissiles.Remove(flying);
        Step(w, 30, pinned: boss, gap: 100);
        Assert.Single(Launches(w));
    }

    [Fact]
    public void Every_missile_is_announced_at_least_2_s_before_it_arrives()
    {
        // The real boss driver, against a player who speeds up, brakes hard to a stop and changes lane.
        var w = Straight(20);
        AddBoss(w, 100);
        double[] lanes = Track.LaneCentres;
        for (int i = 0; i < 90 * StepsPerSecond; i++)
        {
            double t = i * Dt, inCycle = t % 20;
            double speed = inCycle switch
            {
                < 5 => 20,
                < 10 => 45,
                < 15 => 69,
                _ => Math.Max(0, 69 - w.Feel.BrakeDecel * (inCycle - 15)),   // hard brake to a stop
            };
            var p = w.Player;
            if (i % (3 * StepsPerSecond) == 0)
            {
                p.Lateral = lanes[(i / (3 * StepsPerSecond) + 1) % 3];
                p.Heading = 0;
                p.LateralVel = 0;
                p.PushVel = 0;
            }
            p.Armor = 100;                     // rams and missiles must not wreck it: this test is about the warning
            Step(w, speed);
        }

        var launches = Launches(w);
        var ends = Ends(w).ToDictionary(e => e.MissileId);
        Assert.True(launches.Count >= 3, $"only {launches.Count} launches");
        foreach (var l in launches)
        {
            if (!ends.TryGetValue(l.MissileId, out var end)) continue;   // the last one may still be in flight
            Assert.True(end.Time - l.Time >= 2.0 - 1e-9, $"missile {l.MissileId}: launched {l.Time:0.000}, ended {end.Time:0.000}");
        }
        Assert.True(ends.Count >= 3, $"only {ends.Count} missiles arrived");
    }

    [Fact]
    public void Staying_in_lane_takes_25()
    {
        var w = Straight();
        var boss = AddBoss(w, 100);
        var m = WaitForLaunch(w, boss, 100, 30);
        Assert.Equal(1, m.Dir);
        Assert.Equal(boss.Distance - boss.Stats.Length / 2, m.Distance, 1e-9);   // the boss's end nearest you
        Assert.Equal(boss.Lateral, m.Lateral);
        Assert.True(Said(w, "MISSILE INCOMING", Tone.Bad));

        var end = WaitForEnd(w, 30);
        Assert.Equal(MissileResult.Hit, end.Result);
        Assert.Equal(m.Id, end.MissileId);
        Assert.Equal(75, w.Player.Armor, 1e-9);
        Assert.True(Said(w, "MISSILE HIT", Tone.Bad));
        Assert.Empty(w.EnemyMissiles);
    }

    [Fact]
    public void Lane_change_in_the_last_second_dodges()
    {
        var w = Straight();
        var boss = AddBoss(w, 100);
        var m = WaitForLaunch(w, boss, 100, 30);
        w.Agents.Remove(boss);                       // nothing else on the road
        bool steering = false;
        var end = WaitForEnd(w, 30, () =>
        {
            steering |= m.Locked;
            return steering ? new ControlFrame { SteerDir = 1 } : ControlFrame.Empty;
        });
        Assert.True(steering);
        Assert.Equal(MissileResult.Dodged, end.Result);
        Assert.Equal(100, w.Player.Armor);
        Assert.True(Said(w, "MISSILE DODGED", Tone.Good));
    }

    [Fact]
    public void Early_lane_change_is_followed_and_hits()
    {
        var w = Straight();
        var boss = AddBoss(w, 100);
        WaitForLaunch(w, boss, 100, 30);
        w.Agents.Remove(boss);
        double before = w.Player.Lateral;
        int step = 0;
        var end = WaitForEnd(w, 30, () => step++ < 0.8 * StepsPerSecond ? new ControlFrame { SteerDir = 1 } : ControlFrame.Empty);
        Assert.True(w.Player.Lateral - before > 2.5, $"moved only {w.Player.Lateral - before:0.00} m");
        Assert.Equal(MissileResult.Hit, end.Result);
        Assert.Equal(75, w.Player.Armor, 1e-9);
    }

    [Fact]
    public void Guns_shoot_it_down_for_50()
    {
        var w = Straight();
        // Dead centre: between the two gun lines, 0.7 m either side of your centre line.
        Place(w, 80, 1, w.Player.Lateral);
        var end = WaitForEnd(w, 30, () => new ControlFrame { Fire = true });
        Assert.Equal(MissileResult.ShotDown, end.Result);
        Assert.Equal(50, w.Score);
        Assert.Equal(100, w.Player.Armor);
        Assert.True(Said(w, "MISSILE SHOT DOWN +50", Tone.Good));
        Assert.Empty(w.EnemyMissiles);
    }

    [Fact]
    public void Closing_speed_is_25_even_while_steering_hard()
    {
        var w = Straight(20);
        double t0 = w.Time;
        Place(w, -60, -1, w.Player.Lateral);
        double maxHeading = 0;
        int step = 0;
        var end = WaitForEnd(w, 20, () =>
        {
            maxHeading = Math.Max(maxHeading, Math.Abs(w.Player.Heading));
            return new ControlFrame { SteerDir = (step++ / 60) % 2 == 0 ? 1 : -1 };   // weave: 0.5 s each way
        });
        Assert.True(maxHeading > 5 * Math.PI / 180, $"nose only reached {maxHeading * 180 / Math.PI:0.0} degrees");
        Assert.Equal((60 - EnemyMissile.ContactGap) / EnemyMissile.ClosingSpeed, end.Time - t0, Dt);
    }

    static MissionDefinition BossOnly =>
        new("test", "Test", "Briefing", new TrackProfile(7), 500, 5, Array.Empty<WaveDefinition>(),
            StartingLoadout: new LoadoutDefinition(5, 2, 2, 2, 1), Boss: MissileBoss);

    [Fact]
    public void Respawn_clears_missiles_and_no_launch_while_wrecked()
    {
        var run = new MissionRun(BossOnly, new FeelSettings(), 3);
        var w = run.World;
        var p = w.Player;
        p.Distance = 500;
        p.Speed = 40;
        run.Director.Update(w);
        var boss = run.Director.Boss!;
        boss.Engaged = true;

        void Pin()
        {
            boss.Distance = p.Distance + 100;
            boss.Speed = p.Speed;
            boss.Engaged = true;
        }

        var launched = new List<EnemyMissileLaunched>();
        Pin();
        p.Armor = 0;
        run.Update(ControlFrame.Empty, 1 / 60.0);
        launched.AddRange(w.Events.OfType<EnemyMissileLaunched>());
        Assert.Equal(RunState.Respawning, run.State);

        bool placed = false;
        for (int i = 0; i < 400 && run.State == RunState.Respawning; i++)
        {
            if (!placed && run.StateTimer < 1.0)
            {
                Place(w, 150, 1, p.Lateral);      // in flight when the new car starts: it must go
                w.Missiles.Add(new Missile { Pose = new RoadPose(p.Distance + 50, 0, 0), Speed = 1 });
                placed = true;
            }
            Pin();
            run.Update(ControlFrame.Empty, 1 / 60.0);
            launched.AddRange(w.Events.OfType<EnemyMissileLaunched>());
        }
        Assert.True(placed);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Empty(launched);
        Assert.Empty(w.EnemyMissiles);
        Assert.Empty(w.Missiles);

        // The same boss, in range with you driving again, does launch: the check above was not vacuous.
        Pin();
        run.Update(ControlFrame.Empty, 1 / 60.0);
        Assert.Single(w.Events.OfType<EnemyMissileLaunched>());
    }

    [Fact]
    public void Missile_arriving_while_wrecked_misses()
    {
        // Preflight F19: DamagePlayer ignores a wrecked car, so the missile ends as Dodged and says so.
        var w = Straight();
        Place(w, 20, 1, w.Player.Lateral);
        w.Player.Armor = 0;                    // wrecked at the start of the next step, before the missile moves
        var end = WaitForEnd(w, 0);
        Assert.Equal(VehicleState.Wrecked, w.Player.State);
        Assert.Equal(MissileResult.Dodged, end.Result);
        Assert.True(Said(w, "MISSILE MISSED", Tone.Info));
        Assert.Empty(w.Events.OfType<PlayerDamaged>());
    }

    [Fact]
    public void A_missile_in_flight_at_the_respawn_ends_as_missed()
    {
        // Every launch gets exactly one outcome, even when the respawn clears it before it arrives.
        var run = new MissionRun(BossOnly, new FeelSettings(), 3);
        var w = run.World;
        var p = w.Player;
        p.Distance = 500;
        p.Speed = 40;
        run.Director.Update(w);
        var boss = run.Director.Boss!;
        var events = new List<GameEvent>();
        void Frame()
        {
            boss.Distance = p.Distance + 150;     // about 5.8 s of flight: still in the air when the 3 s respawn ends
            boss.Speed = p.Speed;
            boss.Engaged = true;
            run.Update(ControlFrame.Empty, 1 / 60.0);
            events.AddRange(w.Events);
        }

        for (int i = 0; i < 120 && w.EnemyMissiles.Count == 0; i++) Frame();
        Assert.Single(w.EnemyMissiles);
        p.Armor = 0;
        Frame();
        Assert.Equal(RunState.Respawning, run.State);
        for (int i = 0; i < 400 && run.State == RunState.Respawning; i++) Frame();
        Assert.Equal(RunState.Playing, run.State);
        Assert.Empty(w.EnemyMissiles);

        var launch = Assert.Single(events.OfType<EnemyMissileLaunched>());
        var end = Assert.Single(events.OfType<EnemyMissileEnded>());
        Assert.Equal(launch.MissileId, end.MissileId);
        Assert.Equal(MissileResult.Dodged, end.Result);
        Assert.Contains(events, e => e is Message { Text: "MISSILE MISSED", Tone: Tone.Info } m && m.Time == end.Time);
        Assert.DoesNotContain(events, e => e is PlayerDamaged { Reason: "MISSILE HIT" });
    }

    /// <summary>
    /// The boss just over 60 m behind (the closest it may launch from; the world rechecks the gap after the boss has
    /// moved, so exactly 60.000 could go either way by rounding), the player at 30 m/s.
    /// </summary>
    const double CloseBehind = -60.05;

    [Fact]
    public void Close_launch_from_behind_is_announced_at_least_2_s_before_it_arrives()
    {
        var w = Straight();
        var boss = AddBoss(w, CloseBehind);
        var m = WaitForLaunch(w, boss, CloseBehind, 30);
        Assert.Equal(-1, m.Dir);
        w.Agents.Remove(boss);
        var end = WaitForEnd(w, 30);
        var launch = Assert.Single(Launches(w));
        Assert.True(end.Time - launch.Time >= 2.0, $"warning was {end.Time - launch.Time:0.000} s");
    }

    [Fact]
    public void Close_launch_from_behind_still_gives_2_s_when_you_hit_a_wreck_in_the_launch_step()
    {
        // Hitting a wreck holds you back (and 1 m further on the first touch) after the boss has launched, in the same
        // step. The missile must follow that push too, so it still closes at exactly 25 m/s from launch.
        var w = Straight();
        var p = w.Player;
        var boss = AddBoss(w, CloseBehind);
        double contact = (VehicleStats.Boss.Length + p.Stats.Length) / 2;
        w.Hazards.Add(new Wreck
        {
            Distance = p.Distance + contact, PrevDistance = p.Distance + contact, Lateral = p.Lateral,
            Width = VehicleStats.Boss.Width, Length = VehicleStats.Boss.Length,
        });
        double before = p.Distance;
        Step(w, 30, pinned: boss, gap: CloseBehind);
        var m = Assert.Single(w.EnemyMissiles);                          // launched in that very step
        Assert.True(Said(w, "HIT A WRECK", Tone.Bad));
        Assert.True(p.Distance < before, "the wreck did not push the player back");
        double gapAtLaunch = Math.Abs(m.Distance - m.PlayerDistanceAtLaunch!.Value);
        w.Agents.Remove(boss);

        var end = WaitForEnd(w, 30);
        var launch = Assert.Single(Launches(w));
        Assert.True(end.Time - launch.Time >= 2.0, $"warning was {end.Time - launch.Time:0.000} s");
        Assert.Equal((gapAtLaunch - EnemyMissile.ContactGap) / EnemyMissile.ClosingSpeed, end.Time - launch.Time, Dt);
    }

    [Fact]
    public void Only_one_flies_at_a_time_and_at_most_every_6_s()
    {
        // From 158 m a missile flies about 6.1 s, so the rack is ready again while one is still in flight.
        var w = Straight();
        var boss = AddBoss(w, 158);
        for (int i = 0; i < 40 * StepsPerSecond; i++)
        {
            w.Player.Armor = 100;
            Step(w, 30, pinned: boss, gap: 158);
            Assert.True(w.EnemyMissiles.Count <= 1);
        }
        var times = Launches(w).Select(l => l.Time).ToList();
        Assert.True(times.Count >= 5, $"only {times.Count} launches");
        for (int i = 1; i < times.Count; i++)
            Assert.True(times[i] - times[i - 1] >= EnemyMissileRack.Interval - 1e-9, $"launches {times[i - 1]:0.000} and {times[i]:0.000}");
        Assert.Contains(times.Zip(times.Skip(1), (a, b) => b - a), gap => gap > EnemyMissileRack.Interval + 0.05);   // waited for the one in flight
    }

    [Fact]
    public void Missile_from_behind_can_be_dodged_too()
    {
        var w = Straight();
        var boss = AddBoss(w, -100);
        var m = WaitForLaunch(w, boss, -100, 30);
        Assert.Equal(-1, m.Dir);
        Assert.Equal(boss.Distance + boss.Stats.Length / 2, m.Distance, 1e-9);
        w.Agents.Remove(boss);
        bool steering = false;
        var end = WaitForEnd(w, 30, () =>
        {
            steering |= m.Locked;
            return steering ? new ControlFrame { SteerDir = -1 } : ControlFrame.Empty;
        });
        Assert.Equal(MissileResult.Dodged, end.Result);
        Assert.Equal(100, w.Player.Armor);
    }

    // ---- the proving ground, with every weapon

    static string ProvingGroundPath => Path.Combine(AppContext.BaseDirectory, "content", "missions", "proving-ground.json");

    const int ScheduleSteps = 60;   // inputs and actions change only on multiples of 60 steps (preflight F3)

    /// <summary>
    /// The boss appears about 80 s in. Missiles and strikes are saved for it (from 85 s), so it reaches its missile
    /// phase within the 120 s; missiles stop at 92 s so they can't destroy it before it has fired back. Tuned by
    /// playing the run: if a change to the game moves the fight, move these.
    /// </summary>
    const int BossFrom = 85 * StepsPerSecond, MissilesUntil = 92 * StepsPerSecond;

    /// <summary>Every 2 s, 0.5 s apart: drop the rear weapon, cycle it, fire a missile (85-92 s), call a strike (from 85 s).</summary>
    internal static ActionKind? ActionAt(int step)
    {
        if (step % ScheduleSteps != 0) return null;
        return (step / ScheduleSteps % 4) switch
        {
            0 => ActionKind.DropRear,
            1 => ActionKind.CycleRear,
            2 => step >= BossFrom && step < MissilesUntil ? ActionKind.FireMissile : null,
            _ => step >= BossFrom ? ActionKind.FireStrike : null,
        };
    }

    /// <summary>
    /// Steers as the App's diagnostics autopilot does (follows the bend and drifts toward a lane), but heads for the
    /// nearest lane with no wreck or car just ahead, and when nearly stopped (no turning power) crabs toward it, so a
    /// wreck in its lane can't hold it up for the rest of the run.
    /// </summary>
    internal static double AutopilotSteer(World w, double lookAhead = 0)
    {
        var p = w.Player;
        var feel = w.Feel;
        double lane = ClearLane(w);
        if (p.Speed < 3) return Math.Abs(lane - p.Lateral) > 0.3 ? Math.Sign(lane - p.Lateral) : 0;
        double rate = feel.TurnRateAt(p.Speed);
        double wantHeading = Math.Clamp(0.04 * (lane - p.Lateral), -0.08, 0.08);
        double needed = w.Track.CurvatureAt(p.Distance + lookAhead) * p.Speed + (wantHeading - p.Heading) * 4;
        if (feel.AssistWhileSteering) needed += p.Heading * feel.Straighten;
        return Math.Clamp(needed / rate, -1, 1);
    }

    /// <summary>
    /// A lane with no wreck within 90 m ahead and no car within 60 m ahead: the boss's lane when it is 60-400 m ahead
    /// (to keep it in front of the guns), otherwise the lane nearest the player (the centre one on a tie).
    /// </summary>
    static double ClearLane(World w)
    {
        var p = w.Player;
        bool Clear(double lane) =>
            !w.Hazards.Any(h => h is Wreck && h.Distance - p.Distance is > -3 and < 90 && Math.Abs(h.Lateral - lane) < 3)
            && !w.Agents.Any(a => a.Distance - p.Distance is > -3 and < 60 && Math.Abs(a.Lateral - lane) < 3);
        var boss = w.Agents.OfType<BossCar>().FirstOrDefault(b => b.Distance - p.Distance is > 60 and < 400);
        double? bossLane = boss is null ? null : DriverHelpers.NearestLane(boss.Lateral);
        foreach (var lane in Track.LaneCentres.OrderBy(l => l == bossLane ? 0 : 1)
                     .ThenBy(l => Math.Abs(l - p.Lateral)).ThenBy(l => Math.Abs(l)))
            if (Clear(lane)) return lane;
        return 0;
    }

    static int StepCount(MissionRun run) => (int)Math.Round(run.World.Time / World.Step);

    static (ulong Hash, RunState State, double Time, int Score, int Spares) Result(MissionRun run) =>
        (run.World.StateHash(), run.State, run.World.Time, run.World.Score, run.World.Player.SpareCars);

    /// <summary>What a proving-ground run did besides its end state: the weapons the player used, the boss's phases, enemy missiles.</summary>
    sealed class RunLog
    {
        public HashSet<WeaponKind> Used { get; } = new();
        public int EnemyMissiles { get; private set; }
        /// <summary>The boss's latest phase (1-based), at what time; 0 before it appears.</summary>
        public (int Phase, double Time) Boss { get; private set; }

        /// <summary>Reads the events from index <paramref name="from"/> on (the list is cleared when each Update begins).</summary>
        public void Read(World w, int from = 0)
        {
            foreach (var e in w.Events.Skip(from))
                if (e is WeaponUsed { OwnerId: 0 } u) Used.Add(u.Weapon);
                else if (e is EnemyMissileLaunched) EnemyMissiles++;
                else if (e is BossAppeared) Boss = (1, e.Time);
                else if (e is BossPhaseChanged c) Boss = (c.Phase, e.Time);
        }
    }

    /// <summary>120 s of the proving ground at 1/60 s: autopilot steering, gas and guns held, the scheduled actions.</summary>
    static (ulong, RunState, double, int, int) PlayWithAutopilot(RunLog log)
    {
        var run = new MissionRun(MissionLoader.LoadFile(ProvingGroundPath), new FeelSettings(), 9);
        int lastAction = -1;
        for (int frame = 0; frame < 120 * 60; frame++)
        {
            int step = StepCount(run);
            for (int s = lastAction + 1; s <= step; s++)
                if (ActionAt(s) is ActionKind a)
                {
                    int before = run.World.Events.Count;
                    run.Handle(new GameAction(a));
                    log.Read(run.World, before);   // the next Update clears what the action published
                }
            lastAction = step;
            run.Update(new ControlFrame { Gas = true, Fire = true, PadConnected = true, PadSteer = AutopilotSteer(run.World) }, 1 / 60.0);
            log.Read(run.World);
        }
        return Result(run);
    }

    [Fact]
    public void Proving_ground_is_deterministic_with_every_weapon_used()
    {
        Assert.Equal(PlayWithAutopilot(new RunLog()), PlayWithAutopilot(new RunLog()));
    }

    [Fact]
    public void Proving_ground_boss_reaches_its_missile_phase()
    {
        // Coverage for the determinism tests: the scripted run really uses every weapon and meets enemy missiles. The
        // margin is thin (phase 3 at about 116 s of 120); if this fails after a gameplay change, move the schedule.
        var log = new RunLog();
        PlayWithAutopilot(log);
        Assert.Equal(Enum.GetValues<WeaponKind>().OrderBy(k => k), log.Used.OrderBy(k => k));
        Assert.True(log.EnemyMissiles >= 1, $"no enemy missile: the boss reached phase {log.Boss.Phase} at {log.Boss.Time:0.0} s");
    }

    /// <summary>
    /// Held input for the frame-split test: a pure function of the step count that changes only on multiples of 60
    /// steps and never reads the world (preflight F3). The steering per 60-step block comes from <paramref name="steer"/>.
    /// </summary>
    static ControlFrame ScriptedInput(int step, IReadOnlyList<double> steer)
    {
        int block = step / ScheduleSteps;
        return new ControlFrame
        {
            Gas = true, Fire = true, PadConnected = true,
            PadSteer = block < steer.Count ? steer[block] : 0,
        };
    }

    /// <summary>
    /// Plays the proving ground with frames of <paramref name="frames"/>, handing each scheduled input change and action to
    /// the run before the first Update that starts at or after its step, and records the step at which each really went in.
    /// </summary>
    static ((ulong, RunState, double, int, int) Result, List<(int Step, string What)> Applied) PlaySplit(
        IEnumerable<double> frames, IReadOnlyList<double> steer)
    {
        var run = new MissionRun(MissionLoader.LoadFile(ProvingGroundPath), new FeelSettings(), 9);
        var applied = new List<(int, string)>();
        int lastSeen = -1, lastBlock = -1;
        foreach (var dt in frames)
        {
            int step = StepCount(run);
            for (int s = lastSeen + 1; s <= step; s++)
                if (ActionAt(s) is ActionKind a)
                {
                    run.Handle(new GameAction(a));
                    applied.Add((step, a.ToString()));
                }
            lastSeen = step;
            int block = step / ScheduleSteps;
            if (block != lastBlock)
            {
                applied.Add((step, "input " + block));
                lastBlock = block;
            }
            run.Update(ScriptedInput(step, steer), dt);
        }
        return (Result(run), applied);
    }

    /// <summary>
    /// The steering table for the frame-split test, worked out once before the three runs by playing the scripted run
    /// at 1/120 s and reading the autopilot at the start of each 60-step block (looking a quarter block ahead, since it
    /// is then held for the block). The three runs only read the finished table.
    /// </summary>
    static List<double> SteeringTable()
    {
        var run = new MissionRun(MissionLoader.LoadFile(ProvingGroundPath), new FeelSettings(), 9);
        var steer = new List<double>();
        for (int step = 0; step < 120 * StepsPerSecond; step++)
        {
            if (step % ScheduleSteps == 0)
            {
                steer.Add(AutopilotSteer(run.World, run.World.Player.Speed * ScheduleSteps * World.Step / 4));
                if (ActionAt(step) is ActionKind a) run.Handle(new GameAction(a));
            }
            run.Update(ScriptedInput(step, steer), World.Step);
            if (StepCount(run) != step + 1) break;   // the run ended
        }
        return steer;
    }

    [Fact]
    public void Proving_ground_is_the_same_however_the_frames_are_split()
    {
        var steer = SteeringTable();
        var at120 = PlaySplit(Enumerable.Repeat(1 / 120.0, 120 * 120), steer);
        var at60 = PlaySplit(Enumerable.Repeat(1 / 60.0, 120 * 60), steer);
        var uneven = PlaySplit(Enumerable.Range(0, 120 * 120).Select(i => i % 2 == 0 ? 1 / 240.0 : 3 / 240.0), steer);

        // Each change went in at the same step in every split; otherwise the schedule, not the simulation, differs.
        Assert.Equal(at120.Applied, at60.Applied);
        Assert.Equal(at120.Applied, uneven.Applied);
        Assert.Equal(at120.Result, at60.Result);
        Assert.Equal(at120.Result, uneven.Result);
    }
}
