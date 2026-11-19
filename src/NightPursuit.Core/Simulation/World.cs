using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Road;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

/// <summary>
/// Owns the whole simulation and advances it in fixed <see cref="Step"/> increments, so the same seed and
/// the same inputs always give the same result.
/// </summary>
public sealed partial class World
{
    public const double Step = 1.0 / 120;
    const double MaxFrameSeconds = 0.25;
    const double StepEpsilon = 1e-9;
    const double MaxAhead = 1500;
    const double MaxBehind = 500;
    const double BlindSeconds = 2.0;
    const int MaxRecentMessages = 4;
    const double MessageLifetime = 2.5;

    readonly List<GameEvent> _events = new();
    readonly List<AgentCar> _removals = new();
    readonly IThrottleScheme[] _schemes = { new RampPedalScheme(), new SpeedLeverScheme() };
    const double ArmoredBulletDamage = 0.15;
    readonly WorldView _view;
    readonly Dictionary<string, double> _lastDamageMessage = new();
    double _accumulator;
    int _lastId;
    Announcement? _announcement;
    int _shownWarnings;   // bit mask of the kinds shown last step

    public Track Track { get; }
    /// <summary>The road as seen from the player's latest step; rebuilt on every Advance. Drawing does not use it: the App's Projector builds its own view at the interpolated camera.</summary>
    public RoadView View { get; } = new();
    public FeelSettings Feel { get; }
    public IRng Rng { get; }
    /// <summary>Randomness for spawning civilians and police only; never drawn in a mission without them.</summary>
    public IRng TrafficRng { get; set; } = new SeededRng(7919);
    public PlayerCar Player { get; } = new() { Id = 0 };
    public List<AgentCar> Agents { get; } = new();
    public List<Tracer> Tracers { get; } = new();
    public List<Missile> Missiles { get; } = new();
    /// <summary>The boss's missiles in flight (at most one at a time).</summary>
    public List<EnemyMissile> EnemyMissiles { get; } = new();
    public List<Hazard> Hazards { get; } = new();
    /// <summary>Reused by <see cref="Collisions"/> for its agent-vs-agent pass, so a step allocates nothing; empty between steps.</summary>
    internal List<AgentCar> AgentScratch { get; } = new();
    public Loadout Loadout { get; set; } = new();
    public SteeringInput Steering { get; } = new();
    public double Time { get; private set; }
    /// <summary>True when the player's guns fired a tracer during the current step; cleared at the start of each step.</summary>
    public bool PlayerFiredThisStep { get; private set; }
    /// <summary>
    /// How far the clock has run into the next fixed step, 0..1 (leftover accumulator time over <see cref="Step"/>).
    /// The App draws each frame at Prev + (Current - Prev) * Alpha; the simulation itself never reads it.
    /// </summary>
    public double Alpha => Math.Clamp(_accumulator / Step, 0, 1);
    public int Score { get; set; }

    public int SchemeIndex { get; private set; }
    public IThrottleScheme Scheme => _schemes[SchemeIndex];

    /// <summary>Events published since the last <see cref="Advance"/> began.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>The banner on screen now (boss, phase, strike), or null once its time is up.</summary>
    public Announcement? Announcement => _announcement is { } a && Time < a.Until ? a : null;

    /// <summary>Shows a banner for <paramref name="seconds"/>, replacing any other unless that one is urgent and this one is not.</summary>
    public void Announce(string title, string detail, double seconds, bool urgent = false)
    {
        if (!urgent && Announcement is { Urgent: true }) return;   // the strike banners never hide a boss banner
        _announcement = new Announcement(title, detail, Time, Time + seconds, urgent);
    }

    /// <summary>Newest first, at most 4, each kept 2.5 s.</summary>
    public List<GameEvent> RecentMessages { get; } = new();

    public World(Track track, FeelSettings feel, IRng rng)
    {
        Track = track;
        Feel = feel;
        Rng = rng;
        _view = new WorldView(this);
        View.Build(Track, Player.Distance);
    }

    /// <summary>
    /// Runs whole fixed steps for <paramref name="frameSeconds"/> (capped at 0.25 s); returns the step count.
    /// <paramref name="afterStep"/> runs after every step, so rules that must not depend on how frames are split can
    /// run once per step; if it returns true the frame stops there and the unused time is kept for the next call.
    /// </summary>
    public int Advance(ControlFrame input, double frameSeconds, Func<bool>? afterStep = null)
    {
        _events.Clear();
        // Clamp the total, not just the frame: time left over from an early exit must not stack past 0.25 s.
        _accumulator = Math.Min(_accumulator + Math.Clamp(frameSeconds, 0, MaxFrameSeconds), MaxFrameSeconds);
        int steps = 0;
        while (_accumulator >= Step - StepEpsilon)
        {
            StepOnce(input);
            _accumulator -= Step;
            steps++;
            if (afterStep is not null && afterStep()) break;
        }
        if (_accumulator < 0) _accumulator = 0;
        View.Build(Track, Player.Distance);
        return steps;
    }

    public void StepOnce(ControlFrame input)
    {
        PlayerFiredThisStep = false;
        CapturePrev();
        if (Player.State == VehicleState.Wrecked)
        {
            Player.Throttle = 0;
            Player.Brake = 0;
            Player.Shake = Math.Max(0, Player.Shake - Step);
            CarPhysics.StepWrecked(Player, Step);
        }
        else
        {
            var pedals = Scheme.Update(input, Player.Speed, Feel, Step);
            if (input.PadConnected && (input.PadGas > 0.03 || input.PadBrake > 0.03))
                pedals = new PedalOutput(input.PadGas, input.PadBrake);   // analog triggers work in every scheme

            Steering.Update(input, Feel, Step);
            double steer = Player.GripLoss > 0 ? 0 : Steering.Value;     // no grip on enemy oil
            if (Player.PunctureLeft > 0)
            {
                steer = Math.Clamp(steer + 0.15 * Math.Sin(4 * Math.PI * Time), -1, 1);
                if (Player.Speed >= PlayerCar.PunctureSpeedCap)
                {
                    // Throttle cut; above the cap a gentle automatic brake tops CarPhysics's coasting drag up to 4.5 m/s^2.
                    double auto = 0;
                    if (Player.Speed > PlayerCar.PunctureSpeedCap)
                    {
                        double coast = 0.6 + 1.6 * Player.Speed / Feel.MaxSpeed;
                        auto = Math.Max(0, PlayerCar.PunctureBrakeDecel - coast) / Feel.BrakeDecel;
                    }
                    pedals = new PedalOutput(0, Math.Max(pedals.Brake, auto));
                }
            }
            CarPhysics.StepPlayer(Player, pedals, steer, Track, Feel, Step);
            Player.GripLoss = Math.Max(0, Player.GripLoss - Step);
            if (Player.GripLoss < StepEpsilon) Player.GripLoss = 0;
            Player.PunctureLeft = Math.Max(0, Player.PunctureLeft - Step);
            if (Player.PunctureLeft < StepEpsilon) Player.PunctureLeft = 0;
            if (Player.Armor <= 0) WreckPlayer("SCRAPED TO PIECES");     // guard-rail scraping drains armor in StepPlayer
        }

        if (Player.State != VehicleState.Wrecked) CheckSpeedTraps();
        StepWeapons(input);
        Loadout.Update(Step);
        StepMissiles();
        StepStrike();
        StepHazards();
        StepAgents();
        Collisions.Resolve(this);
        ClearBlockedRoadblocks();
        StepHeat();
        StepEnemyMissiles();            // after every move of the player this step, collisions too

        // Cars leave the list only now, so nothing above sees the list change mid-step.
        Agents.RemoveAll(IsGone);
        _removals.Clear();

        Player.HitFlash = Math.Max(0, Player.HitFlash - Step);
        PublishNewWarnings();
        Time += Step;
        RecentMessages.RemoveAll(e => Time - e.Time > MessageLifetime);
    }

    /// <summary>Publishes a <see cref="WarningRaised"/> for each warning kind on screen now that was not there last step. Runs inside StepOnce, before the mission director's afterStep.</summary>
    void PublishNewWarnings()
    {
        int now = 0;
        foreach (var warning in Warnings.Collect(this))
        {
            int bit = 1 << (int)warning.Kind;
            now |= bit;
            if ((_shownWarnings & bit) == 0) Publish(new WarningRaised(Time, warning.Kind, warning.Text));
        }
        _shownWarnings = now;
    }

    /// <summary>Remembers where everything that moves was before this step, for render interpolation.</summary>
    void CapturePrev()
    {
        Player.SnapPrev();
        foreach (var a in Agents) a.SnapPrev();
        foreach (var m in Missiles)
        {
            m.PrevDistance = m.Pose.Distance;
            m.PrevLateral = m.Pose.Lateral;
            m.PrevHeading = m.Pose.Heading;
        }
        foreach (var m in EnemyMissiles)
        {
            m.PrevDistance = m.Distance;
            m.PrevLateral = m.Lateral;
            m.JustLaunched = false;     // launched last step: it moves from this one on
        }
        foreach (var h in Hazards)
            if (h is Wreck w) w.PrevDistance = w.Distance;
            else if (h is SmokeCloud s) s.PrevDistance = s.Distance;
    }

    void StepAgents()
    {
        foreach (var a in Agents)
        {
            if (a.Punctured && a.IsOnRoadDriving) a.PunctureSeconds += Step;
            UpdateBlindness(a);
            switch (a.State)
            {
                case VehicleState.Driving:
                    // Order: the driver decides (a boss's driver includes its slow and leash) -> keep clear of the car
                    // ahead -> a puncture overrides -> move -> a boss is kept on the road -> weapons.
                    bool blind = a.Blinded > 0;
                    var driver = blind ? BlindDriver.Instance : a.Driver;   // DriverState is untouched, so the plan resumes
                    var intent = driver.Decide(a, _view, Step);
                    // A blinded car can't see the car ahead either, which is how cars collide inside smoke.
                    if (!blind)
                        intent = DriverHelpers.KeepClearOfCarAhead(a, _view, intent, a.AheadMemory, DriverHelpers.ReactionDelay(a.Kind), Step);
                    if (a.Punctured) intent = PuncturedIntent(a, intent);
                    CarPhysics.StepAgent(a, intent, Step);
                    if (a is BossCar driving) KeepBossOnRoad(driving);
                    StepAgentWeapons(a, intent);      // after the car has moved
                    break;
                case VehicleState.Spinning:
                    CarPhysics.StepSpinning(a, Step);
                    if (a is BossCar spinning) KeepBossOnRoad(spinning);
                    break;
                case VehicleState.Wrecked: CarPhysics.StepWrecked(a, Step); break;
            }
            if (a is BossCar boss) CountDownBoss(boss);
            if (a.State != VehicleState.Wrecked) a.Arms.Update(Step);
            a.HitFlash = Math.Max(0, a.HitFlash - Step);
            a.ContactCooldown = Math.Max(0, a.ContactCooldown - Step);
            a.HitHeatCooldown = Math.Max(0, a.HitHeatCooldown - Step);
            a.AgentContactCooldown = Math.Max(0, a.AgentContactCooldown - Step);
        }
    }

    /// <summary>A punctured car sheds speed and limps toward the edge of the road on its own side, wobbling as it goes.</summary>
    static DriveIntent PuncturedIntent(AgentCar a, DriveIntent decided)
    {
        int side = Math.Abs(a.Lateral) < 0.5 ? a.Side : Math.Sign(a.Lateral);
        return decided with
        {
            TargetSpeed = Math.Max(0, a.Speed - 5 * Step),
            TargetLateral = a.Lateral + side * 0.5 * a.Stats.SideSpeed / 1.8 + 0.8 * Math.Sin(2 * Math.PI * 1.5 * a.PunctureSeconds),
        };
    }

    /// <summary>Smoke blinds a driving car for 2 s after it was last inside a cloud; a spinning or wrecked car isn't blinded.</summary>
    void UpdateBlindness(AgentCar a)
    {
        bool covered = false;
        if (a.State == VehicleState.Driving && a.Faction != Faction.Civilian)   // civilians slow in smoke but are never blinded
            foreach (var h in Hazards)
                if (h is SmokeCloud c && c.Covers(a)) { covered = true; break; }
        if (covered)
        {
            if (a.Blinded <= 0)
            {
                Publish(new Message(Time, $"{KindRules.DisplayName(a.Kind)} LOST YOU IN THE SMOKE", Tone.Good));
                a.WanderTimer = 0;                                 // pick a fresh wander target, not one left from an earlier cloud
            }
            a.Blinded = BlindSeconds;
        }
        else
        {
            a.Blinded = Math.Max(0, a.Blinded - Step);
            if (a.Blinded < StepEpsilon) a.Blinded = 0;
        }
    }

    /// <summary>Killed by gunfire, burnt out after 1.5 s, or too far from the player to matter (never a boss: it leaves only when destroyed).</summary>
    bool IsGone(AgentCar a)
    {
        if (_removals.Contains(a)) return true;
        if (a.State == VehicleState.Wrecked && a.StateTimer <= 0) return true;
        if (a is BossCar) return false;
        double ahead = a.Distance - Player.Distance;
        return ahead > MaxAhead || ahead < -MaxBehind;
    }

    /// <summary>
    /// Spins a car out: it turns, slides sideways in direction <paramref name="dir"/> and loses a fifth of its speed.
    /// With <paramref name="announce"/> false only the <see cref="VehicleSpun"/> event goes out, for a second car spun
    /// by the same collision, so one collision shows one message.
    /// </summary>
    public void Spin(Vehicle v, int dir, string reason, bool announce = true, Tone? tone = null)
    {
        v.State = VehicleState.Spinning;
        v.StateTimer = 1.8;
        v.SpinRate = dir * (6 + Rng.NextDouble() * 3);
        v.PushVel = dir * (4 + Rng.NextDouble() * 3);
        v.Speed *= 0.8;
        Publish(new VehicleSpun(Time, v.Id, reason));
        if (announce) Publish(new Message(Time, reason, tone ?? (v.Faction is Faction.Civilian or Faction.Police ? Tone.Info : Tone.Good)));
    }

    /// <summary>Hurts the player. The Message is skipped when the same reason was messaged less than <paramref name="messageGap"/> seconds ago.</summary>
    internal void DamagePlayer(double amount, string reason, double messageGap = 0)
    {
        if (Player.State == VehicleState.Wrecked) return;
        Player.Armor -= amount;
        Player.HitFlash = 0.35;
        Publish(new PlayerDamaged(Time, amount, reason));
        if (messageGap <= 0 || !_lastDamageMessage.TryGetValue(reason, out double last) || Time - last >= messageGap)
        {
            Publish(new Message(Time, reason, Tone.Bad));
            if (messageGap > 0) _lastDamageMessage[reason] = Time;
        }
        if (Player.Armor <= 0) WreckPlayer(reason);
    }

    internal void WreckPlayer(string reason)
    {
        if (Player.State == VehicleState.Wrecked) return;
        Player.Armor = 0;
        Player.State = VehicleState.Wrecked;
        Player.StateTimer = 3;
        Publish(new PlayerWrecked(Time, reason));
        Publish(new Message(Time, $"WRECKED: {reason}", Tone.Bad));
    }

    void StepWeapons(ControlFrame input)
    {
        var guns = Loadout.Guns;
        guns.Update(Step);
        bool trigger = (input.Fire || input.PadFire) && Player.State != VehicleState.Wrecked;
        if (guns.TryFire(trigger, Step))
        {
            // Guns sit 2 m ahead of the driver and 0.7 m either side of the car's centre line.
            double side = guns.LeftGun ? -0.7 : 0.7, c = Math.Cos(Player.Heading), s = Math.Sin(Player.Heading);
            PlayerFiredThisStep = true;
            Tracers.Add(new Tracer
            {
                Pose = new RoadPose(Player.Distance + 2 * c - side * s, Player.Lateral + 2 * s + side * c, Player.Heading),
                Speed = Player.Speed + MachineGuns.MuzzleSpeed,
                OwnerId = Player.Id,
            });
        }

        foreach (var t in Tracers)
        {
            t.PrevDistance = t.Pose.Distance;
            t.PrevLateral = t.Pose.Lateral;
            t.PrevHeading = t.Pose.Heading;
            t.Pose = RoadMotion.MoveStraight(Track, t.Pose, t.Speed * Step);
            t.Travel += t.MuzzleSpeed * Step;
            if (t.Travel > t.Range || Math.Abs(t.Pose.Lateral) > 60) { t.Dead = true; continue; }
            if (t.Hostile) { StepHostileTracer(t); continue; }
            if (ShootDownEnemyMissile(t)) continue;     // an incoming missile is tested before the cars
            // Several cars can overlap the swept segment; the bullet hits the one nearest along its path.
            AgentCar? hit = null;
            double nearest = double.MaxValue;
            foreach (var car in Agents)       // Destroy never changes the list, so it can be walked directly
            {
                if (!car.IsOnRoadDriving || !SweptHit(t.PrevDistance, t.PrevLateral, t.Pose.Distance, t.Pose.Lateral, car, 0.15)) continue;
                double along = car.Distance - t.PrevDistance;
                if (along < nearest) { nearest = along; hit = car; }
            }
            if (hit is null) continue;
            t.Dead = true;
            hit.HitFlash = 0.08;
            BlamePlayer(hit);
            OffenceHit(hit, bullet: true);
            if (hit is BossCar boss)
            {
                DamageBoss(boss, BossGunfireDamage(boss), KillCause.Gunfire, "GUNFIRE");
                continue;
            }
            hit.Armor -= hit.Kind == AgentKind.Armored ? ArmoredBulletDamage : 1;
            if (hit.Kind == AgentKind.Armored && !hit.HintShown)
            {
                hit.HintShown = true;
                Publish(new Message(Time, "ARMORED: USE A MISSILE, A RAM OR A HAZARD", Tone.Info));
            }
            if (hit.Armor <= 0) Destroy(hit, KillCause.Gunfire);
        }
        Tracers.RemoveAll(t => t.Dead);
        EnemyMissiles.RemoveAll(m => m.Dead);
    }

    /// <summary>True when a projectile's last step, from (d0, l0) to (d1, l1), crossed the car's length within its width plus <paramref name="margin"/>.</summary>
    public static bool SweptHit(double d0, double l0, double d1, double l1, Vehicle car, double margin) =>
        SweptHit(d0, l0, d1, l1, car.Distance, car.Lateral, car.Stats.Length / 2, car.Stats.Width / 2, margin);

    /// <summary>The same test for any box centred at (<paramref name="cd"/>, <paramref name="cl"/>), such as an enemy missile.</summary>
    public static bool SweptHit(double d0, double l0, double d1, double l1, double cd, double cl,
        double halfLength, double halfWidth, double margin)
    {
        if (cd + halfLength < Math.Min(d0, d1) || cd - halfLength > Math.Max(d0, d1)) return false;
        double span = d1 - d0;
        double u = Math.Abs(span) < 1e-9 ? 1 : Math.Clamp((cd - d0) / span, 0, 1);
        double lateral = l0 + (l1 - l0) * u;
        return Math.Abs(lateral - cl) <= halfWidth + margin;
    }

    void StepHazards()
    {
        foreach (var h in Hazards) h.Update(Step, Player.Distance);
        Hazards.RemoveAll(h => h.Expired);
    }

    /// <summary>
    /// Kills an enemy car and scores it. Gunfire, missiles and satellite strikes leave a sliding <see cref="Wreck"/> and removes the car at the end of
    /// the step; other causes keep the car as Wrecked for 1.5 s, then it is removed.
    /// </summary>
    public void Destroy(AgentCar car, KillCause cause)
    {
        if (car.State == VehicleState.Wrecked || _removals.Contains(car)) return;
        car.State = VehicleState.Wrecked;
        car.StateTimer = 1.5;
        var faction = car.Faction;
        int points = faction == Faction.Hostile ? car.Stats.Points : 0;   // civilians and police never change the score
        Score += points;
        Publish(new VehicleDestroyed(Time, car.Id, car.Kind, cause, points, faction));
        if (faction == Faction.Hostile)
        {
            string name = car is BossCar boss ? boss.Definition.Name : car.Kind.ToString();
            Publish(new Message(Time, $"{name.ToUpperInvariant()} DESTROYED  +{points}", Tone.Good));
        }
        else PublishNonHostileDestroyed(car, faction);
        if (cause.LeavesWreck())
        {
            Hazards.Add(new Wreck
            {
                Distance = car.Distance, Lateral = car.Lateral, PrevDistance = car.PrevDistance,
                Width = car.Stats.Width, Length = car.Stats.Length, Speed = car.Speed, Seed = car.Id,
            });
            _removals.Add(car);
        }
    }

    public void Handle(GameAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.SelectScheme:
                SchemeIndex = Math.Clamp(a.Value, 0, _schemes.Length - 1);
                Scheme.Reset(Player.Speed);
                break;
            case ActionKind.Cruise:
            case ActionKind.LeverPreset:
            case ActionKind.LeverUp:
            case ActionKind.LeverDown:
                Scheme.Handle(a, Player.Speed, Feel);
                break;
            case ActionKind.DropRear:
                DropRear();
                break;
            case ActionKind.CycleRear:
                CycleRear();
                break;
            case ActionKind.FireMissile:
                FireMissile();
                break;
            case ActionKind.FireStrike:
                BeginStrike();
                break;
            // Pause, ToggleHelp, Restart: handled by the mission run.
        }
    }

    /// <summary>The next unused vehicle id (the player is 0).</summary>
    public int NextId() => ++_lastId;

    public void Publish(GameEvent e)
    {
        _events.Add(e);
        if (e is Message)
        {
            RecentMessages.Insert(0, e);
            if (RecentMessages.Count > MaxRecentMessages) RecentMessages.RemoveAt(RecentMessages.Count - 1);
        }
    }

    /// <summary>
    /// FNV-1a 64-bit, in a fixed order, over (numbers to 1e-6): the player's Distance, Lateral, Speed, Armor, GripLoss
    /// and PunctureLeft; each agent in list order with the same four numbers, its Blinded timer, its player-blame time and hit-heat cooldown, its oil and caltrops
    /// droppers' charges and timers, its missile rack's cooldown and, for a boss, its phase, shield, caltrops slow and
    /// whether it is engaged; every missile's and enemy missile's Distance and Lateral; each hazard's type, Distance and
    /// Lateral (a speed trap's Triggered and Caught, a roadblock's HitPlayer), and their number; and the player's weapons: each one's charges, cooldown and recharge, then the strike sweep; last, the heat's value and the time since the last offence.
    /// </summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        Mix(ref h, Player);
        Add(ref h, Player.GripLoss); Add(ref h, Player.PunctureLeft);
        foreach (var a in Agents)
        {
            Mix(ref h, a);
            Add(ref h, a.Blinded);
            Add(ref h, a.PlayerBlameUntil); Add(ref h, a.HitHeatCooldown);
            Charged(ref h, a.Arms.Oil);
            Charged(ref h, a.Arms.Caltrops);
            Add(ref h, a.Arms.Missiles?.CooldownLeft ?? -1);
            if (a is BossCar b)
            {
                Add(ref h, b.PhaseIndex); Add(ref h, b.Shield); Add(ref h, b.SlowLeft); Add(ref h, b.Engaged ? 1 : 0);
            }
        }
        foreach (var m in Missiles) { Add(ref h, m.Pose.Distance); Add(ref h, m.Pose.Lateral); }
        foreach (var m in EnemyMissiles) { Add(ref h, m.Distance); Add(ref h, m.Lateral); }
        foreach (var z in Hazards)
        {
            foreach (char c in z.GetType().Name) { h ^= c; h *= 1099511628211UL; }
            Add(ref h, z.Distance); Add(ref h, z.Lateral);
            if (z is SpeedTrap trap) Add(ref h, (trap.Triggered ? 1 : 0) + (trap.Caught ? 2 : 0));
            else if (z is Roadblock block) Add(ref h, block.HitPlayer ? 1 : 0);
        }
        Add(ref h, Hazards.Count);
        Charged(ref h, Loadout.Oil);
        Charged(ref h, Loadout.Smoke);
        Charged(ref h, Loadout.Caltrops);
        Charged(ref h, Loadout.Missiles);
        Charged(ref h, Loadout.Strike);
        Add(ref h, Loadout.Strike.Targeting ? 1 : 0); Add(ref h, Loadout.Strike.SweepLeft);
        Add(ref h, Heat.Value); Add(ref h, Heat.SinceOffence);
        return h;

        static void Mix(ref ulong h, Vehicle v)
        {
            Add(ref h, v.Distance); Add(ref h, v.Lateral); Add(ref h, v.Speed); Add(ref h, v.Armor);
        }

        static void Charged(ref ulong h, ChargedWeapon? w)
        {
            if (w is null) { Add(ref h, -1); return; }   // keeps the positions of what follows unambiguous
            Add(ref h, w.Charges); Add(ref h, w.CooldownLeft); Add(ref h, w.RechargeProgress);
        }

        static void Add(ref ulong h, double x)
        {
            long q = (long)Math.Round(x * 1e6);
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(q >> (8 * i));
                h *= 1099511628211UL;
            }
        }
    }
}
