using NightPursuit.Core.Audio;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests.Audio;

public class SoundDirectorTests
{
    const double Frame = 0.1;   // real seconds per director update: longer than every cue's minimum gap

    static MissionDefinition Def(string id) =>
        new(id, id, "Briefing", new TrackProfile(7), 300, 5, Array.Empty<WaveDefinition>(), Bonus: 0);

    sealed class Rig
    {
        public readonly ScreenFlow Flow;
        public readonly SoundDirector Director = new(new MusicSlots(new MusicManifest()));

        public Rig(params MissionDefinition[] defs)
        {
            if (defs.Length == 0) defs = new[] { Def("alpha") };
            var feel = new FeelSettings();
            var settings = new GameSettings();
            Flow = new ScreenFlow(practice => new Game(defs, feel, settings, practice, 5), feel, settings, new HighScoreTable(), new MemoryGameStore());
        }

        public World W => Flow.Game!.Run!.World;
        public PlayerCar P => W.Player;
        public SoundFrame Update(double real = Frame) => Director.Update(Flow, real);
        public SoundCue[] Cues(double real = Frame) => Update(real).Cues.Select(c => c.Cue).ToArray();
        public void Key(MenuInput i, char letter = '\0')
        {
            var k = new MenuKey(i, letter);
            Director.NoteMenuKey(k);
            Flow.Handle(k);
        }
        public void Tick() => Flow.Update(ControlFrame.Empty, 1 / 60.0);
        public void ToPlay(bool practice = false) { Flow.StartGame(practice); Flow.Handle(new MenuKey(MenuInput.Confirm)); Update(); }
        public void PauseNow() { Flow.HandleGameAction(new GameAction(ActionKind.Pause)); Tick(); }
        public void Select(string id)
        {
            for (int i = 0; i < 12 && Flow.Menu!.Current.Id != id; i++) Flow.Handle(new MenuKey(MenuInput.Down));
            Assert.Equal(id, Flow.Menu!.Current.Id);
        }

        /// <summary>Steps the world once and publishes <paramref name="events"/> in that step, as the game would.</summary>
        public void Publish(params GameEvent[] events)
        {
            Tick();
            foreach (var e in events) W.Publish(e);
        }

        /// <summary>A car that stays exactly where it is put, <paramref name="ahead"/> m ahead of and <paramref name="right"/> m right of the player.</summary>
        public AgentCar Car(AgentKind kind, double ahead, double right = 0)
        {
            var car = new AgentCar(kind, VehicleStats.Runner)
            {
                Id = W.NextId(), Driver = new StillDriver(), Distance = P.Distance + ahead, Lateral = P.Lateral + right,
            };
            car.SnapPrev();
            W.Agents.Add(car);
            return car;
        }
    }

    static readonly Dictionary<string, GameEvent> Rows = new()
    {
        ["oil"] = new WeaponUsed(0, WeaponKind.Oil, 0),
        ["smoke"] = new WeaponUsed(0, WeaponKind.Smoke, 0),
        ["caltrops"] = new WeaponUsed(0, WeaponKind.Caltrops, 0),
        ["missile"] = new WeaponUsed(0, WeaponKind.Missile, 0),
        ["strike"] = new WeaponUsed(0, WeaponKind.Strike, 0),
        ["strike landed"] = new StrikeLanded(0, 2),
        ["destroyed"] = new VehicleDestroyed(0, 999, AgentKind.Runner, KillCause.Gunfire, 100),
        ["damaged"] = new PlayerDamaged(0, 10, "SHOT"),
        ["wrecked"] = new PlayerWrecked(0, "WRECKED"),
        ["busted"] = new PlayerBusted(0),
        ["punctured"] = new VehiclePunctured(0, 0),
        ["spun"] = new VehicleSpun(0, 0, "OIL"),
        ["supply"] = new SupplyCollected(0, SupplyItem.Oil, 2),
        ["armour"] = new SupplyCollected(0, SupplyItem.Armor, 25),
        ["warning"] = new WarningRaised(0, WarningKind.HazardAhead, "HAZARD AHEAD"),
        ["missile warning"] = new WarningRaised(0, WarningKind.MissileIncoming, "MISSILE INCOMING"),
        ["boss appears"] = new BossAppeared(0, "Boss"),
        ["boss phase"] = new BossPhaseChanged(0, 2, 3, AgentKind.Gunner),
        ["boss hit"] = new BossDamaged(0, 5, "GUNS"),
        ["enemy missile"] = new EnemyMissileLaunched(0, 7),
        ["enemy missile hit"] = new EnemyMissileEnded(0, 7, MissileResult.Hit),
        ["enemy missile dodged"] = new EnemyMissileEnded(0, 7, MissileResult.Dodged),
        ["enemy missile shot down"] = new EnemyMissileEnded(0, 7, MissileResult.ShotDown),
        ["heat up"] = new HeatLevelChanged(0, HeatLevel.Watched, HeatLevel.Patrol),
        ["heat down"] = new HeatLevelChanged(0, HeatLevel.Pursuit, HeatLevel.Patrol),
        ["speed trap"] = new SpeedTrapPassed(0, 90, 55),
        ["roadblock"] = new RoadblockHit(0),
        ["message"] = new Message(0, "HELLO", Tone.Info),
        ["heat changed"] = new HeatChanged(0, 5, 30, "SPEEDING"),
    };

    [Theory]
    [InlineData("oil", SoundCue.OilDrop)]
    [InlineData("smoke", SoundCue.SmokeDrop)]
    [InlineData("caltrops", SoundCue.CaltropsDrop)]
    [InlineData("missile", SoundCue.MissileLaunch)]
    [InlineData("strike", SoundCue.StrikeCall)]
    [InlineData("strike landed", SoundCue.StrikeBoom)]
    [InlineData("destroyed", SoundCue.Explosion)]
    [InlineData("damaged", SoundCue.PlayerHit)]
    [InlineData("wrecked", SoundCue.PlayerWrecked)]
    [InlineData("busted", SoundCue.PlayerBusted)]
    [InlineData("punctured", SoundCue.Puncture)]
    [InlineData("spun", SoundCue.Spin)]
    [InlineData("supply", SoundCue.Pickup)]
    [InlineData("armour", SoundCue.PickupArmor)]
    [InlineData("warning", SoundCue.Warning)]
    [InlineData("missile warning", SoundCue.MissileAlarm)]
    [InlineData("boss appears", SoundCue.BossAppears)]
    [InlineData("boss phase", SoundCue.BossPhase)]
    [InlineData("boss hit", SoundCue.BossHit)]
    [InlineData("enemy missile", SoundCue.EnemyMissileLaunch)]
    [InlineData("enemy missile hit", SoundCue.Explosion)]
    [InlineData("enemy missile dodged", SoundCue.EnemyMissileDodged)]
    [InlineData("enemy missile shot down", SoundCue.EnemyMissileShotDown)]
    [InlineData("heat up", SoundCue.HeatUp)]
    [InlineData("heat down", SoundCue.HeatDown)]
    [InlineData("speed trap", SoundCue.SpeedTrap)]
    [InlineData("roadblock", SoundCue.RoadblockCrash)]
    [InlineData("message", null)]
    [InlineData("heat changed", null)]
    public void Each_event_in_the_table_gives_its_cue(string row, SoundCue? expected)
    {
        var r = new Rig();
        r.ToPlay();
        r.Publish(Rows[row]);
        var cues = r.Update().Cues;
        if (expected is null) Assert.Empty(cues);
        else
        {
            var play = Assert.Single(cues);
            Assert.Equal(expected, play.Cue);
            Assert.Equal(0, play.Pan);   // the player's own events, and events with no car, are centred
        }
    }

    [Fact]
    public void Paused_frames_do_not_replay_the_last_events()
    {
        var r = new Rig();
        r.ToPlay();
        r.Publish(new SpeedTrapPassed(0, 90, 55));
        Assert.Equal(new[] { SoundCue.SpeedTrap }, r.Cues());
        Assert.Empty(r.Cues());   // a frame with no step: the same events are still in the list

        r.PauseNow();             // paused: the world is not stepped, so Time stays the same
        r.Update();
        r.W.Publish(new RoadblockHit(r.W.Time));   // published while paused: same Time, so not read
        for (int i = 0; i < 5; i++) Assert.Empty(r.Cues());
    }

    [Fact]
    public void A_new_world_resets_seen_tracers_and_stops_live_sounds()
    {
        var r = new Rig();
        r.ToPlay(practice: true);
        var old = r.W;
        var shot = new Tracer { Pose = new RoadPose(r.P.Distance + 5, r.P.Lateral, 0), OwnerId = r.P.Id };
        r.Tick();
        old.Tracers.Add(shot);
        r.P.GripLoss = 2;
        old.SetHeat(80, "TEST");
        r.Car(AgentKind.PolicePatrol, 50);
        var before = r.Update();
        Assert.Contains(SoundCue.GunPlayer, before.Cues.Select(c => c.Cue));
        Assert.Equal(1, before.Live.Skid01);
        Assert.True(before.Live.Siren01 > 0);

        r.PauseNow();
        r.Key(MenuInput.Letter, 'R');   // practice restart: a new World
        Assert.NotSame(old, r.W);
        r.W.Tracers.Add(shot);          // the same tracer object in the new world counts as a new shot
        var after = r.Update();
        Assert.Contains(SoundCue.GunPlayer, after.Cues.Select(c => c.Cue));
        Assert.Equal(0, after.Live.Skid01);
        Assert.Equal(0, after.Live.Siren01);
        Assert.True(after.Live.EngineOn);
    }

    [Fact]
    public void Busted_plays_once_without_the_wreck_sound()
    {
        var r = new Rig();
        r.ToPlay();
        r.Tick();
        r.W.BustPlayer();   // publishes PlayerBusted and PlayerWrecked in the same step
        Assert.Equal(new[] { SoundCue.PlayerBusted }, r.Cues());
        r.Tick();
        Assert.DoesNotContain(SoundCue.PlayerWrecked, r.Cues());

        var w = new Rig();
        w.ToPlay();
        w.Publish(new PlayerWrecked(0, "WRECKED"));
        Assert.Equal(new[] { SoundCue.PlayerWrecked }, w.Cues());
    }

    [Fact]
    public void A_repeated_warning_within_1_5_s_is_silent()
    {
        var r = new Rig();
        r.ToPlay();
        r.Publish(new WarningRaised(0, WarningKind.HazardAhead, "HAZARD AHEAD"));
        Assert.Equal(new[] { SoundCue.Warning }, r.Cues(0.1));
        r.Publish(new WarningRaised(0, WarningKind.HazardAhead, "HAZARD AHEAD"));
        Assert.Empty(r.Cues(1.2));   // 1.2 s later: still within 1.5 s
        r.Publish(new WarningRaised(0, WarningKind.WreckAhead, "WRECK AHEAD"));
        Assert.Equal(new[] { SoundCue.Warning }, r.Cues(0.1));   // a different warning is heard
        r.Publish(new WarningRaised(0, WarningKind.HazardAhead, "HAZARD AHEAD"));
        Assert.Equal(new[] { SoundCue.Warning }, r.Cues(0.3));   // 1.6 s after the first
    }

    [Fact]
    public void Damage_crunches_are_spaced_and_louder_for_bigger_hits()
    {
        var r = new Rig();
        r.ToPlay();
        r.Publish(new PlayerDamaged(0, 4, "SHOT"));
        Assert.Equal(0.6, Assert.Single(r.Update(0.1).Cues).Volume, 9);
        r.Publish(new PlayerDamaged(0, 100, "RAMMED"));
        Assert.Empty(r.Update(0.1).Cues);   // 0.1 s after the last crunch: under the 0.25 s gap
        r.Publish(new PlayerDamaged(0, 100, "RAMMED"));
        Assert.Equal(1.0, Assert.Single(r.Update(0.2).Cues).Volume, 9);
        r.Publish(new PlayerDamaged(0, 0, "SCRAPE"));
        Assert.Equal(0.5, Assert.Single(r.Update(0.3).Cues).Volume, 9);
    }

    [Fact]
    public void The_boss_explosion_sound_waits_for_the_explosion()
    {
        var r = new Rig();
        r.ToPlay();
        var boss = r.Car(AgentKind.Runner, 60);
        r.Publish(new VehicleDestroyed(0, boss.Id, AgentKind.Boss, KillCause.Missile, 5000),
                  new BossFinaleStarted(0, boss.Id, FinaleKind.Roll, 1));
        var kill = Assert.Single(r.Update().Cues);
        Assert.Equal(SoundCue.Spin, kill.Cue);
        Assert.Equal(0.5, kill.Volume, 9);                       // from the boss, 60 m ahead

        r.Publish(new BossExploded(0, r.P.Distance + 60, r.P.Lateral));
        var boom = Assert.Single(r.Update().Cues);
        Assert.Equal(SoundCue.Explosion, boom.Cue);
        Assert.Equal(0.5, boom.Volume, 9);                       // from where it blew up

        var runner = r.Car(AgentKind.Runner, 60);
        r.Publish(new VehicleDestroyed(0, runner.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        Assert.Equal(new[] { SoundCue.Explosion }, r.Cues());   // an ordinary kill still explodes at once
    }

    [Fact]
    public void Explosion_400_m_away_is_silent_and_60_m_away_is_half()
    {
        var r = new Rig();
        r.ToPlay();
        var far = r.Car(AgentKind.Runner, 400);
        var near = r.Car(AgentKind.Runner, 60);
        var behind = r.Car(AgentKind.Runner, -60);
        var close = r.Car(AgentKind.Runner, 0, right: 4);   // alongside, clear of the player

        r.Publish(new VehicleDestroyed(0, far.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        Assert.Equal(Array.Empty<SoundCue>(), r.Cues());
        r.Publish(new VehicleDestroyed(0, near.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        Assert.Equal(0.5, Assert.Single(r.Update().Cues).Volume, 9);
        r.Publish(new VehicleDestroyed(0, behind.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        Assert.Equal(0.5, Assert.Single(r.Update().Cues).Volume, 9);
        r.Publish(new VehicleDestroyed(0, close.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        Assert.Equal(1.0, Assert.Single(r.Update().Cues).Volume, 9);
    }

    [Fact]
    public void A_car_on_the_right_pans_right()
    {
        // Projector.ToScreen draws a larger Lateral further right (x = HalfWidth + offset * scale), so the same sign pans right.
        var r = new Rig();
        r.ToPlay();
        var right = r.Car(AgentKind.Runner, 30, right: 5);
        var left = r.Car(AgentKind.Runner, 30, right: -25);
        r.Publish(new VehicleSpun(0, right.Id, "OIL"));
        var spin = Assert.Single(r.Update().Cues);
        Assert.Equal(0.5, spin.Pan, 9);
        Assert.Equal(1 / (1 + 0.25), spin.Volume, 9);
        r.Publish(new VehicleSpun(0, left.Id, "OIL"));
        Assert.Equal(-1, Assert.Single(r.Update().Cues).Pan, 9);   // clamped

        // A car that has gone (destroyed and removed) is heard from where it was last seen.
        r.W.Agents.Remove(right);
        r.Publish(new VehicleDestroyed(0, right.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        Assert.Equal(0.5, Assert.Single(r.Update().Cues).Pan, 9);
        // One never seen at all plays centred, at full volume.
        r.Publish(new VehicleDestroyed(0, 12345, AgentKind.Runner, KillCause.Gunfire, 100));
        var unseen = Assert.Single(r.Update().Cues);
        Assert.Equal((0.0, 1.0), (unseen.Pan, unseen.Volume));
    }

    [Fact]
    public void New_player_tracer_gives_one_gun_cue()
    {
        var r = new Rig();
        r.ToPlay();
        var gunner = r.Car(AgentKind.Runner, 60, right: -5);   // owns the hostile tracer (a Runner, so it never fires one itself)
        r.Tick();
        var d = r.P.Distance;
        r.W.Tracers.Add(new Tracer { Pose = new RoadPose(d + 2, r.P.Lateral - 0.6, 0), OwnerId = r.P.Id });
        r.W.Tracers.Add(new Tracer { Pose = new RoadPose(d + 2, r.P.Lateral + 0.6, 0), OwnerId = r.P.Id });
        r.W.Tracers.Add(new Tracer { Pose = new RoadPose(d + 50, -6, Math.PI), OwnerId = gunner.Id, Hostile = true });
        var cues = r.Update().Cues;
        Assert.Equal(1, cues.Count(c => c.Cue == SoundCue.GunPlayer));
        var enemy = Assert.Single(cues, c => c.Cue == SoundCue.GunEnemy);
        Assert.Equal((0.5, -0.5), (enemy.Volume, enemy.Pan));   // from its owner, 60 m ahead, 5 m left

        r.Tick();   // the same tracers, moved on: no new shot
        Assert.DoesNotContain(r.Update().Cues, c => c.Cue is SoundCue.GunPlayer or SoundCue.GunEnemy);
    }

    [Fact]
    public void Siren_only_at_pursuit_with_police_near()
    {
        var r = new Rig();
        r.ToPlay();
        var police = r.Car(AgentKind.PolicePatrol, 120, right: 4);
        r.W.SetHeat(55, "TEST");   // Patrol: police drive by, no siren
        Assert.Equal(HeatLevel.Patrol, r.W.Heat.Level);
        Assert.Equal(0, r.Update().Live.Siren01);

        r.W.SetHeat(75, "TEST");   // Pursuit
        var live = r.Update().Live;
        Assert.Equal(1 / (1 + 4.0), live.Siren01, 9);
        Assert.Equal(0.4, live.SirenPan, 9);

        var nearer = r.Car(AgentKind.PoliceCruiser, -60, right: -3);
        live = r.Update().Live;
        Assert.Equal(0.5, live.Siren01, 9);
        Assert.Equal(-0.3, live.SirenPan, 9);

        r.W.Agents.Remove(nearer);
        police.State = VehicleState.Wrecked;   // a wrecked police car is not driving
        Assert.Equal(0, r.Update().Live.Siren01);
        police.State = VehicleState.Driving;
        police.Distance = r.P.Distance + 450;  // too far
        Assert.Equal(0, r.Update().Live.Siren01);
        r.Car(AgentKind.Runner, 20);           // not police
        Assert.Equal(0, r.Update().Live.Siren01);
    }

    [Fact]
    public void Missile_whine_rises_as_the_nearest_missile_closes()
    {
        var r = new Rig();
        r.ToPlay();
        r.W.EnemyMissiles.Add(new EnemyMissile { Id = 1, Distance = r.P.Distance + 200, Lateral = 0 });
        Assert.Equal(0, r.Update().Live.Whine01);
        r.W.EnemyMissiles.Add(new EnemyMissile { Id = 2, Distance = r.P.Distance - 30, Lateral = 0 });
        var live = r.Update().Live;
        Assert.Equal(1, live.Whine01);
        Assert.Equal(0.8, live.WhineCloseness01, 9);
    }

    [Fact]
    public void Engine_off_on_screens_and_while_wrecked()
    {
        var r = new Rig();
        Assert.Equal(LiveState.Silent, r.Update().Live);   // Title
        r.Flow.StartGame();
        Assert.Equal(LiveState.Silent, r.Update().Live);   // Briefing
        r.Flow.Handle(new MenuKey(MenuInput.Confirm));
        r.P.Speed = r.W.Feel.MaxSpeed / 2;
        r.P.Throttle = 0.7;
        r.P.PunctureLeft = 1;
        r.P.OffRoad = true;
        var live = r.Update().Live;
        Assert.True(live.EngineOn);
        Assert.Equal(0.5, live.Speed01, 9);
        Assert.Equal(0.7, live.Throttle01, 9);
        Assert.True(live.Punctured);
        Assert.Equal(1, live.OffRoad01);   // well over 30 m/s
        r.P.Speed = 15;
        Assert.Equal(0.5, r.Update().Live.OffRoad01, 9);

        r.PauseNow();
        Assert.Equal(Screen.Pause, r.Flow.Current);
        Assert.Equal(LiveState.Silent, r.Update().Live);
        r.Flow.Handle(new MenuKey(MenuInput.Back));   // resume
        Assert.True(r.Update().Live.EngineOn);

        r.P.State = VehicleState.Wrecked;
        Assert.False(r.Update().Live.EngineOn);
        r.Tick();
        Assert.Equal(RunState.Respawning, r.Flow.Game!.Run!.State);
        var respawning = r.Update().Live;
        Assert.False(respawning.EngineOn);
        Assert.NotEqual(LiveState.Silent, respawning);   // the rest still plays while the wreck coasts
    }

    [Fact]
    public void Menu_move_and_confirm_give_menu_cues()
    {
        var r = new Rig();
        r.Update();
        r.Key(MenuInput.Down);
        Assert.Equal(new[] { SoundCue.MenuMove }, r.Cues());
        Assert.Empty(r.Cues());

        r.Key(MenuInput.Down);   // to SETTINGS
        r.Update();
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.Settings, r.Flow.Current);
        Assert.Equal(new[] { SoundCue.MenuConfirm }, r.Cues());

        r.Select("effects");
        r.Update();
        r.Key(MenuInput.Left);   // the value changes: heard at the new level
        Assert.Equal(new[] { SoundCue.MenuConfirm }, r.Cues());
        r.Key(MenuInput.Confirm);   // Confirm on a volume row changes nothing
        Assert.Empty(r.Cues());

        r.Key(MenuInput.Back);
        Assert.Equal(Screen.Title, r.Flow.Current);
        Assert.Equal(new[] { SoundCue.MenuBack }, r.Cues());

        // A screen change with no menu key (the game pausing itself) is silent.
        r.ToPlay();
        r.PauseNow();
        Assert.Empty(r.Cues());

        // Initials: each letter typed ticks.
        r.W.Score = 500;
        r.Select("quit"); r.Key(MenuInput.Confirm);
        r.Key(MenuInput.Down); r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.GameOver, r.Flow.Current);
        r.Update();
        r.Key(MenuInput.Confirm);
        Assert.Equal(Screen.EnterInitials, r.Flow.Current);
        r.Update();
        r.Key(MenuInput.Letter, 'X');
        Assert.Equal(new[] { SoundCue.MenuLetter }, r.Cues());
        r.Key(MenuInput.Up);
        Assert.Equal(new[] { SoundCue.MenuLetter }, r.Cues());
    }

    [Fact]
    public void Music_is_passed_through()
    {
        var director = new SoundDirector(new MusicSlots(new MusicManifest { Title = new MusicTrack("title.mp3") }));
        var feel = new FeelSettings();
        var settings = new GameSettings();
        var flow = new ScreenFlow(p => new Game(new[] { Def("alpha") }, feel, settings, p, 5), feel, settings, new HighScoreTable(), new MemoryGameStore());
        var music = director.Update(flow, Frame).Music;
        Assert.Equal(MusicSlot.Title, music.Slot);
        Assert.Equal("title.mp3", music.Track!.File);
    }

    /// <summary>30 s of a shipped mission through a real flow, gas and guns held, autopilot steering.</summary>
    static (ulong Hash, double Time, int Cues) Drive(string mission, bool withDirector)
    {
        var r = new Rig(MissionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "content", "missions", mission + ".json")));
        r.Flow.StartGame();
        r.Flow.Handle(new MenuKey(MenuInput.Confirm));
        int cues = 0;
        for (int frame = 0; frame < 30 * 60; frame++)
        {
            var w = r.W;
            r.Flow.Update(new ControlFrame { Gas = true, Fire = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(w) }, 1 / 60.0);
            if (withDirector) cues += r.Director.Update(r.Flow, 1 / 60.0).Cues.Count;
        }
        return (r.W.StateHash(), r.W.Time, cues);
    }

    // Mission 01 has traffic (so a draw from TrafficRng would show); the proving ground has the boss and its missiles.
    [Theory]
    [InlineData("mission-01")]
    [InlineData("proving-ground")]
    public void Director_never_changes_the_state_hash(string mission)
    {
        var without = Drive(mission, withDirector: false);
        var with = Drive(mission, withDirector: true);
        Assert.Equal((without.Hash, without.Time), (with.Hash, with.Time));
        Assert.True(with.Cues > 10, $"the director should have had work to do (got {with.Cues} cues)");
    }

    [Fact]
    public void The_same_cue_twice_in_one_frame_plays_once_as_the_loudest_with_its_pan()
    {
        var r = new Rig();
        r.ToPlay();
        var far = r.Car(AgentKind.Runner, 120, right: -8);    // quiet, from the left
        var near = r.Car(AgentKind.Runner, 30, right: 8);     // loud, from the right
        r.Publish(
            new VehicleDestroyed(0, far.Id, AgentKind.Runner, KillCause.Gunfire, 100),
            new VehicleDestroyed(0, near.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        var play = Assert.Single(r.Update().Cues);
        Assert.Equal(SoundCue.Explosion, play.Cue);
        Assert.Equal(1 / (1 + 0.25), play.Volume, 9);   // 30 m away
        Assert.True(play.Pan > 0);

        // The loud one first gives the same answer.
        r.Publish(
            new VehicleDestroyed(0, near.Id, AgentKind.Runner, KillCause.Gunfire, 100),
            new VehicleDestroyed(0, far.Id, AgentKind.Runner, KillCause.Gunfire, 100));
        var again = Assert.Single(r.Update().Cues);
        Assert.Equal((play.Volume, play.Pan), (again.Volume, again.Pan));
    }

    [Fact]
    public void Two_player_hits_in_one_frame_play_the_harder_one()
    {
        var r = new Rig();
        r.ToPlay();
        r.Publish(new PlayerDamaged(0, 4, "SCRAPE"), new PlayerDamaged(0, 30, "RAM"));
        var play = Assert.Single(r.Update().Cues);
        Assert.Equal(1.0, play.Volume, 9);
    }
}
