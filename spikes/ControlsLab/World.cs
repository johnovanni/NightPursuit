namespace ControlsLab;

public enum EnemyKind { Runner, Blocker, Pursuer }
public enum EnemyState { Driving, Spinning, Wrecked }
public enum PursuerPhase { Approach, Attack, Retreat }
public enum Tone { Info, Good, Bad }

public sealed class PlayerCar
{
    public double Z, X, Speed, Throttle, Brake, Steer, PushVel, LateralVel;
    /// <summary>Where the nose points relative to the road's direction, radians (positive = right).</summary>
    public double Heading;
    public double Health = 100, HitFlash, FireCooldown, MuzzleFlash, OilCooldown, OilRecharge, Shake;
    public int OilCharges = World.MaxOil;
    public bool GunLeft, OffRoad, OverRev, Wrecked;
    public double WreckTimer;
    /// <summary>Diagnostic: what last set the screen shake.</summary>
    public string ShakeWhy = "";
}

public sealed class EnemyCar
{
    public EnemyKind Kind;
    public EnemyState State;
    public PursuerPhase Phase;
    public double Z, X, Speed, LateralVel, Motion, TargetX, CruiseSpeed;
    public double Health, MaxHealth, AiTimer, StateTimer, SpinAngle, SpinRate, HitFlash, ContactCooldown;
    public int Side = 1;

    public double Width => Kind == EnemyKind.Blocker ? 2.4 : 1.9;
    public double Height => Kind switch { EnemyKind.Blocker => 2.1, EnemyKind.Pursuer => 1.15, _ => 1.4 };
    public string Name => Kind switch { EnemyKind.Blocker => "BLOCKER", EnemyKind.Pursuer => "PURSUER", _ => "RUNNER" };
    public int Points => Kind switch { EnemyKind.Blocker => 300, EnemyKind.Pursuer => 500, _ => 100 };
}

/// <summary>A tracer in road coordinates; Heading is relative to the road where it currently is.</summary>
public sealed class Bullet { public double Z, X, Heading, Travel; public bool Dead; }
public sealed class OilSlick { public double Z, X, Life = 25, Radius = 2.4; }
public sealed class Explosion { public double Z, X, Age; public int Seed; }
public sealed class Message { public string Text = ""; public double Timer = 2.5; public Tone Tone; }

/// <summary>The whole prototype simulation: player physics, enemies, weapons, collisions, spawning.</summary>
public sealed class World
{
    public const int MaxOil = 5;
    public const double CarLength = 4.4, PlayerWidth = 1.9;
    static readonly double[] Lanes = { -5, 0, 5 };

    public Track Track { get; } = new();
    public RoadView View { get; } = new();
    public Tuning Tuning { get; }
    public PlayerCar Player { get; } = new();
    public List<EnemyCar> Enemies { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<OilSlick> Slicks { get; } = new();
    public List<Explosion> Explosions { get; } = new();
    public List<Message> Messages { get; } = new();
    public IReadOnlyList<IThrottleScheme> Schemes { get; }
    public int SchemeIndex { get; private set; }
    public IThrottleScheme Scheme => Schemes[SchemeIndex];
    public int Score { get; private set; }
    /// <summary>Diagnostic: enemies destroyed by gunfire so far.</summary>
    public int GunKills { get; private set; }
    public double Time { get; private set; }
    public bool Paused { get; set; }
    public bool ShowHelp { get; set; } = true;
    public bool PadConnected { get; private set; }
    public bool PadSteering { get; private set; }

    readonly Random _rng = new(1234);
    double _pursuerCooldown = 6, _blockerCooldown = 12;

    public World(Tuning tuning)
    {
        Tuning = tuning;
        Schemes = new IThrottleScheme[] { new RampPedalScheme(), new SpeedLeverScheme(), new GearboxScheme(), new WheelLeverScheme() };
        View.Build(Track, Player.Z);
    }

    // ---------------------------------------------------------------- actions

    public void Handle(GameAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.NextScheme: SelectScheme((SchemeIndex + 1) % Schemes.Count); break;
            case ActionKind.SelectScheme: SelectScheme(Math.Clamp(a.Value, 0, Schemes.Count - 1)); break;
            case ActionKind.DropOil: DropOil(); break;
            case ActionKind.ToggleHelp: ShowHelp = !ShowHelp; break;
            case ActionKind.TogglePause: Paused = !Paused; break;
            case ActionKind.Reset: ResetRun(); break;
            default: Scheme.Handle(a, Player, Tuning); break;
        }
    }

    void SelectScheme(int i)
    {
        SchemeIndex = i;
        Scheme.Reset(Player);
        Say($"CONTROL: {Scheme.Name}", Tone.Info);
    }

    void ResetRun()
    {
        var p = Player;
        p.Z = 0; p.X = 0; p.Speed = 0; p.Steer = 0; p.PushVel = 0; p.Health = 100; p.Wrecked = false;
        p.OilCharges = MaxOil; p.Throttle = 0; p.Brake = 0;
        Enemies.Clear(); Bullets.Clear(); Slicks.Clear(); Explosions.Clear();
        Score = 0; Time = 0; _pursuerCooldown = 6; _blockerCooldown = 12;
        Scheme.Reset(p);
        Say("RESET", Tone.Info);
    }

    void DropOil()
    {
        var p = Player;
        if (p.Wrecked || p.OilCooldown > 0) return;
        if (p.OilCharges <= 0) { Say("NO OIL LEFT (recharging)", Tone.Bad); return; }
        Slicks.Add(new OilSlick { Z = p.Z - 5, X = p.X });
        p.OilCharges--;
        p.OilCooldown = 0.6;
        Say("OIL SLICK DROPPED", Tone.Info);
    }

    public void Say(string text, Tone tone)
    {
        Messages.Insert(0, new Message { Text = text, Tone = tone });
        if (Messages.Count > 4) Messages.RemoveAt(Messages.Count - 1);
    }

    // ---------------------------------------------------------------- update

    public void Update(InputFrame input, double dt)
    {
        PadConnected = input.PadConnected;
        if (ShowHelp && (input.Gas || input.PadGas > 0.2)) ShowHelp = false;
        if (Paused) return;

        const double step = 1.0 / 120;
        int n = Math.Max(1, (int)Math.Ceiling(dt / step));
        for (int i = 0; i < n; i++) Step(input, dt / n);
        View.Build(Track, Player.Z);
    }

    void Step(InputFrame input, double dt)
    {
        Time += dt;
        var p = Player;
        if (p.Wrecked)
        {
            p.WreckTimer -= dt;
            p.Speed = Math.Max(0, p.Speed - 25 * dt);
            p.Z += p.Speed * dt;
            if (p.WreckTimer <= 0) RespawnPlayer();
        }
        else
        {
            Scheme.Update(input, p, Tuning, dt);
            if (input.PadConnected && (input.PadGas > 0.03 || input.PadBrake > 0.03))
            {
                // Analog triggers act as a real pedal in every scheme.
                p.Throttle = input.PadGas;
                p.Brake = input.PadBrake;
            }
            Steering(input, dt);
            Physics(dt);
            Weapons(input, dt);
        }

        UpdateEnemies(dt);
        if (!p.Wrecked) Collisions();
        UpdateEffects(dt);
        Spawn(dt);
    }

    void Steering(InputFrame input, double dt)
    {
        var p = Player;
        PadSteering = input.PadConnected && input.SteerDir == 0 && Math.Abs(input.PadSteer) > 0;
        if (PadSteering)
        {
            p.Steer = Math.Clamp(input.PadSteer, -1, 1);
            return;
        }
        int target = input.SteerDir;
        if (target == 0)
            p.Steer = MathX.MoveToward(p.Steer, 0, Tuning.SteerReturn * dt);
        else
        {
            bool reversing = Math.Sign(p.Steer) == -target;
            double rate = reversing ? Tuning.SteerReturn + Tuning.SteerRampUp : Tuning.SteerRampUp;
            p.Steer = MathX.MoveToward(p.Steer, target, rate * dt);
        }
    }

    void Physics(double dt)
    {
        var p = Player;
        var t = Tuning;
        double v = p.Speed, vmax = t.MaxSpeed, rr = v / vmax;

        double engine;
        p.OverRev = false;
        if (Scheme.GearTopSpeed is double top)
        {
            double r = v / top;
            if (r < 1) engine = p.Throttle * t.MaxAccel * Scheme.AccelMultiplier * (1 - r * r);
            else { engine = -(2.5 + 0.4 * (v - top)); p.OverRev = r > 1.02; }
        }
        else
            engine = p.Throttle * t.MaxAccel * (1 - rr * rr);

        double coast = v > 0.1 ? 0.6 + 1.6 * (1 - p.Throttle) * rr : 0;
        double brake = p.Brake * t.BrakeDecel;
        double gravity = -9.8 * Track.SlopeAt(p.Z) * 0.6;
        p.OffRoad = Math.Abs(p.X) > Track.RoadHalfWidth + 0.4;
        double offRoad = p.OffRoad ? 0.35 * v + 2 : 0;

        v = Math.Max(0, v + (engine - coast - brake + gravity - offRoad) * dt);
        p.Speed = v;

        // Turn-to-aim steering: steering turns the nose; the road turning under the car (curvature × speed)
        // swings the nose outward on bends; with no input, the assist straightens it toward the road.
        double turn = p.Steer * t.TurnRateAt(v) - Track.CurvatureAt(p.Z) * v;
        if (t.AssistWhileSteering || Math.Abs(p.Steer) < 0.05) turn -= p.Heading * t.Straighten;
        double maxHeading = t.MaxHeadingAt(v);
        p.Heading = Math.Clamp(p.Heading + turn * dt, -maxHeading, maxHeading);
        p.LateralVel = v * Math.Sin(p.Heading) + p.PushVel;
        p.X += p.LateralVel * dt;
        p.PushVel *= Math.Exp(-5 * dt);

        if (Math.Abs(p.X) > 12)   // guard-rail
        {
            p.X = Math.Sign(p.X) * 12;
            p.Heading *= 0.5;
            p.Speed *= Math.Exp(-1.5 * dt);
            p.Health -= 6 * dt;
            p.Shake = Math.Max(p.Shake, 0.3); p.ShakeWhy = "rail";
        }
        if (p.OffRoad) { p.Shake = Math.Max(p.Shake, 0.12); p.ShakeWhy = "offroad"; }
        p.Shake = Math.Max(0, p.Shake - dt);
        p.Z += v * Math.Cos(p.Heading) * dt;
        if (p.Health <= 0) WreckPlayer("SCRAPED TO PIECES");
    }

    /// <summary>
    /// Moves something straight across the ground by <paramref name="step"/> metres, in road coordinates:
    /// advance along its heading, then take off the road's turning so its real direction stays fixed.
    /// </summary>
    public void MoveStraight(ref double z, ref double x, ref double heading, double step)
    {
        double along = step * Math.Cos(heading);
        x += step * Math.Sin(heading);
        heading -= Track.CurvatureAt(z + along / 2) * along;
        z += along;
    }

    /// <summary>Where the gun line is <paramref name="distance"/> metres ahead (road distance and lateral).</summary>
    public (double Z, double X) AimPoint(double distance)
    {
        var p = Player;
        double z = p.Z, x = p.X, h = p.Heading;
        for (double moved = 0; moved < distance; moved += 5) MoveStraight(ref z, ref x, ref h, 5);
        return (z, x);
    }

    void Weapons(InputFrame input, double dt)
    {
        var p = Player;
        p.FireCooldown -= dt;
        p.MuzzleFlash -= dt;
        p.OilCooldown -= dt;
        if ((input.Fire || input.PadFire) && p.FireCooldown <= 0)
        {
            p.FireCooldown = 0.075;
            p.MuzzleFlash = 0.04;
            p.GunLeft = !p.GunLeft;
            // Guns sit 2 m ahead of the driver and 0.7 m either side of the car's centre line.
            double side = p.GunLeft ? -0.7 : 0.7, c = Math.Cos(p.Heading), s = Math.Sin(p.Heading);
            Bullets.Add(new Bullet { Z = p.Z + 2 * c - side * s, X = p.X + 2 * s + side * c, Heading = p.Heading });
        }
        if (p.OilCharges < MaxOil && (p.OilRecharge += dt) >= 8)
        {
            p.OilCharges++;
            p.OilRecharge = 0;
        }

        foreach (var b in Bullets)
        {
            double z0 = b.Z;
            MoveStraight(ref b.Z, ref b.X, ref b.Heading, (p.Speed + 260) * dt);   // flies straight, not along the road
            b.Travel += 260 * dt;
            if (b.Travel > 380 || Math.Abs(b.X) > 60) { b.Dead = true; continue; }
            foreach (var e in Enemies)
            {
                if (e.State == EnemyState.Wrecked) continue;
                if (e.Z + 2.2 < z0 || e.Z - 2.2 > b.Z || Math.Abs(e.X - b.X) > e.Width / 2 + 0.15) continue;
                b.Dead = true;
                e.Health -= 1;
                e.HitFlash = 0.08;
                if (e.Health <= 0) { WreckEnemy(e, "DESTROYED"); GunKills++; }
                break;
            }
        }
        Bullets.RemoveAll(b => b.Dead);
    }

    // ---------------------------------------------------------------- enemies

    void UpdateEnemies(double dt)
    {
        foreach (var e in Enemies)
        {
            e.HitFlash -= dt;
            e.ContactCooldown -= dt;
            switch (e.State)
            {
                case EnemyState.Wrecked:
                    e.StateTimer -= dt;
                    e.Speed = Math.Max(0, e.Speed - 25 * dt);
                    e.Z += e.Speed * dt;
                    e.X += e.LateralVel * dt;
                    e.LateralVel *= Math.Exp(-2 * dt);
                    continue;

                case EnemyState.Spinning:
                    e.StateTimer -= dt;
                    e.SpinAngle += e.SpinRate * dt;
                    e.Speed *= Math.Exp(-0.9 * dt);
                    e.X += e.LateralVel * dt;
                    e.Z += e.Speed * dt;
                    if (e.StateTimer <= 0) { e.State = EnemyState.Driving; e.SpinAngle = 0; e.LateralVel = 0; }
                    break;

                default:
                    Drive(e, dt);
                    foreach (var s in Slicks)
                        if (Math.Abs(e.Z - s.Z) < s.Radius + 1.5 && Math.Abs(e.X - s.X) < s.Radius + 0.8)
                        {
                            Spin(e, _rng.Next(2) == 0 ? -1 : 1, $"{e.Name} HIT THE OIL");
                            break;
                        }
                    break;
            }

            if (Math.Abs(e.X) > Track.RoadHalfWidth + 1.0)
            {
                if (e.State == EnemyState.Spinning || Math.Abs(e.LateralVel) > 2)
                    WreckEnemy(e, "RAN OFF THE ROAD");
                else
                    e.TargetX = Math.Clamp(e.TargetX, -5, 5);
            }
        }
    }

    void Drive(EnemyCar e, double dt)
    {
        var p = Player;
        double dz = e.Z - p.Z;
        double desired = e.CruiseSpeed, steerMax = 3.5;
        switch (e.Kind)
        {
            case EnemyKind.Runner:
                e.AiTimer -= dt;
                if (e.AiTimer <= 0) { e.AiTimer = 2.5 + _rng.NextDouble() * 4; e.TargetX = Lane(); }
                break;

            case EnemyKind.Blocker:
                steerMax = 2.8;
                if (dz > 0 && dz < 140)
                {
                    e.TargetX = Math.Clamp(p.X, -5.5, 5.5);      // gets in your lane
                    if (dz < 30) desired = Math.Max(15, Math.Min(e.CruiseSpeed, p.Speed - 5)); // brake-checks
                }
                break;

            case EnemyKind.Pursuer:
                steerMax = 6;
                switch (e.Phase)
                {
                    case PursuerPhase.Approach:
                        desired = Math.Min(85, p.Speed + 14);
                        e.TargetX = Math.Clamp(p.X + e.Side * 2.7, -6.5, 6.5);
                        if (dz > -7) { e.Phase = PursuerPhase.Attack; e.AiTimer = 3.5; }
                        break;
                    case PursuerPhase.Attack:
                        desired = p.Speed + Math.Clamp(-dz * 1.5, -8, 8);
                        e.AiTimer -= dt;
                        e.TargetX = e.AiTimer < 2.4 ? p.X + e.Side * 0.8 : Math.Clamp(p.X + e.Side * 2.7, -6.5, 6.5);
                        if (e.AiTimer <= 0 || dz > 30 || dz < -40) e.Phase = PursuerPhase.Retreat;
                        break;
                    case PursuerPhase.Retreat:
                        desired = Math.Max(10, p.Speed - 12);
                        if (dz < -80) { e.Phase = PursuerPhase.Approach; e.Side = _rng.Next(2) == 0 ? -1 : 1; }
                        break;
                }
                break;
        }

        e.Speed = MathX.MoveToward(e.Speed, desired, (e.Speed < desired ? 6 : 10) * dt);
        double steer = Math.Clamp((e.TargetX - e.X) * 1.8, -steerMax, steerMax);
        e.Motion = steer + e.LateralVel;
        e.X += e.Motion * dt;
        e.LateralVel *= Math.Exp(-1.2 * dt);
        e.Z += e.Speed * dt;
    }

    void Spin(EnemyCar e, int dir, string why)
    {
        e.State = EnemyState.Spinning;
        e.StateTimer = 1.8;
        e.SpinRate = dir * (6 + _rng.NextDouble() * 3);
        e.LateralVel = dir * (4 + _rng.NextDouble() * 3);
        e.Speed *= 0.8;
        Say(why, Tone.Good);
    }

    void WreckEnemy(EnemyCar e, string why)
    {
        if (e.State == EnemyState.Wrecked) return;
        e.State = EnemyState.Wrecked;
        e.StateTimer = 1.5;
        Explosions.Add(new Explosion { Z = e.Z, X = e.X, Seed = _rng.Next() });
        Score += e.Points;
        Say($"{e.Name} {why}  +{e.Points}", Tone.Good);
        if (e.Kind == EnemyKind.Pursuer) _pursuerCooldown = 10;
        if (e.Kind == EnemyKind.Blocker) _blockerCooldown = 15;
    }

    // ---------------------------------------------------------------- collisions

    void Collisions()
    {
        var p = Player;
        foreach (var e in Enemies)
        {
            if (e.State == EnemyState.Wrecked) continue;
            double dz = e.Z - p.Z, dx = e.X - p.X;
            double halfW = (e.Width + PlayerWidth) / 2;
            if (Math.Abs(dz) >= CarLength || Math.Abs(dx) >= halfW || e.ContactCooldown > 0) continue;
            e.ContactCooldown = 0.25;
            double overlapX = halfW - Math.Abs(dx), overlapZ = CarLength - Math.Abs(dz);
            p.Shake = 0.35; p.ShakeWhy = $"contact-{e.Kind}-{e.State}";

            if (overlapZ / CarLength < overlapX / halfW)
            {
                double rel = Math.Abs(p.Speed - e.Speed);
                if (dz > 0)
                {
                    // We rear-ended them.
                    p.Z = e.Z - CarLength;
                    double pv = p.Speed;
                    p.Speed = Math.Max(0, Math.Min(p.Speed, e.Speed) - 1.5);
                    e.Speed = Math.Max(e.Speed, pv * 0.6 + e.Speed * 0.4) + 1.5;
                    e.Health -= 1 + rel * 0.1;
                    e.HitFlash = 0.1;
                    DamagePlayer(3 + rel * 0.5, "RAMMED");
                }
                else
                {
                    // They rear-ended us.
                    e.Z = p.Z - CarLength;
                    double ev = e.Speed;
                    e.Speed = Math.Max(0, p.Speed - 3);
                    p.Speed = Math.Max(p.Speed, ev * 0.5 + p.Speed * 0.5);
                    DamagePlayer(3 + rel * 0.5, "HIT FROM BEHIND");
                }
            }
            else
            {
                int side = dx >= 0 ? 1 : -1;   // +1: enemy is on our right
                double playerInto = Math.Max(0, p.LateralVel * side);
                double enemyInto = Math.Max(0, -e.Motion * side);
                e.LateralVel = side * (3 + 1.3 * playerInto);
                p.PushVel = -side * (2.5 + 1.0 * enemyInto);
                e.X += side * overlapX * 0.5;
                p.X -= side * overlapX * 0.5;
                if (e.Kind == EnemyKind.Pursuer && e.Phase == PursuerPhase.Attack) e.Phase = PursuerPhase.Retreat;
                DamagePlayer(2 + enemyInto * 1.2, "SIDESWIPED");
                if (playerInto > 5 && e.State == EnemyState.Driving)
                    Spin(e, side, $"{e.Name} KNOCKED INTO A SPIN");
            }
            if (e.Health <= 0) WreckEnemy(e, "DESTROYED");
        }
    }

    void DamagePlayer(double amount, string why)
    {
        var p = Player;
        p.Health -= amount;
        p.HitFlash = 0.35;
        if (p.Health <= 0) WreckPlayer(why);
        else Say(why, Tone.Bad);
    }

    void WreckPlayer(string why)
    {
        var p = Player;
        if (p.Wrecked) return;
        p.Wrecked = true;
        p.WreckTimer = 3;
        p.Health = 0;
        Explosions.Add(new Explosion { Z = p.Z + 6, X = p.X, Seed = _rng.Next() });
        Say($"WRECKED: {why}", Tone.Bad);
    }

    void RespawnPlayer()
    {
        var p = Player;
        p.Wrecked = false; p.Health = 100; p.Speed = 0; p.X = 0; p.Steer = 0; p.PushVel = 0;
        p.OilCharges = MaxOil;
        Enemies.RemoveAll(e => Math.Abs(e.Z - p.Z) < 200);
        Slicks.Clear();
        _pursuerCooldown = 6;
        Scheme.Reset(p);
        Say("BACK ON THE ROAD", Tone.Info);
    }

    // ---------------------------------------------------------------- effects and spawning

    void UpdateEffects(double dt)
    {
        var p = Player;
        p.HitFlash -= dt;
        foreach (var s in Slicks) s.Life -= dt;
        Slicks.RemoveAll(s => s.Life <= 0 || p.Z - s.Z > 600);
        foreach (var x in Explosions) x.Age += dt;
        Explosions.RemoveAll(x => x.Age > 1.2);
        foreach (var m in Messages) m.Timer -= dt;
        Messages.RemoveAll(m => m.Timer <= 0);
    }

    void Spawn(double dt)
    {
        var p = Player;
        Enemies.RemoveAll(e => (e.State == EnemyState.Wrecked && e.StateTimer <= 0)
                               || e.Z - p.Z < -500 || e.Z - p.Z > 1500);
        int runners = 0, blockers = 0, pursuers = 0;
        foreach (var e in Enemies)
        {
            if (e.State == EnemyState.Wrecked) continue;
            if (e.Kind == EnemyKind.Runner) runners++;
            else if (e.Kind == EnemyKind.Blocker) blockers++;
            else pursuers++;
        }
        _pursuerCooldown -= dt;
        _blockerCooldown -= dt;

        if (runners < 3)
        {
            double cruise = 36 + _rng.NextDouble() * 20;
            double x = Lane();
            Enemies.Add(new EnemyCar
            {
                Kind = EnemyKind.Runner, Z = p.Z + 350 + _rng.NextDouble() * 800, X = x, TargetX = x,
                CruiseSpeed = cruise, Speed = cruise, Health = 6, MaxHealth = 6, AiTimer = 2 + _rng.NextDouble() * 4,
            });
        }
        if (blockers < 1 && _blockerCooldown <= 0)
        {
            double cruise = 30 + _rng.NextDouble() * 8;
            double x = Lane();
            Enemies.Add(new EnemyCar
            {
                Kind = EnemyKind.Blocker, Z = p.Z + 600 + _rng.NextDouble() * 500, X = x, TargetX = x,
                CruiseSpeed = cruise, Speed = cruise, Health = 14, MaxHealth = 14,
            });
            _blockerCooldown = 3;
        }
        if (pursuers < 1 && _pursuerCooldown <= 0)
        {
            double x = Lane();
            Enemies.Add(new EnemyCar
            {
                Kind = EnemyKind.Pursuer, Z = p.Z - 280, X = x, TargetX = x, Speed = p.Speed + 10,
                Health = 10, MaxHealth = 10, Phase = PursuerPhase.Approach, Side = _rng.Next(2) == 0 ? -1 : 1,
            });
            _pursuerCooldown = 3;
        }
    }

    double Lane() => Lanes[_rng.Next(Lanes.Length)];
}
