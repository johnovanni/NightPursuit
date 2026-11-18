using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Road;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

/// <summary>
/// Owns the whole simulation and advances it in fixed <see cref="Step"/> increments, so the same seed and
/// the same inputs always give the same result.
/// </summary>
public sealed class World
{
    public const double Step = 1.0 / 120;
    const double MaxFrameSeconds = 0.25;
    const double StepEpsilon = 1e-9;
    const int MaxRecentMessages = 4;
    const double MessageLifetime = 2.5;

    readonly List<GameEvent> _events = new();
    readonly List<AgentCar> _removals = new();
    readonly IThrottleScheme[] _schemes = { new RampPedalScheme(), new SpeedLeverScheme() };
    double _accumulator;
    int _lastId;

    public Track Track { get; }
    public RoadView View { get; } = new();
    public FeelSettings Feel { get; }
    public IRng Rng { get; }
    public PlayerCar Player { get; } = new() { Id = 0 };
    public List<AgentCar> Agents { get; } = new();
    public List<Tracer> Tracers { get; } = new();
    public List<Hazard> Hazards { get; } = new();
    public Loadout Loadout { get; } = new();
    public SteeringInput Steering { get; } = new();
    public double Time { get; private set; }
    public int Score { get; set; }

    public int SchemeIndex { get; private set; }
    public IThrottleScheme Scheme => _schemes[SchemeIndex];

    /// <summary>Events published since the last <see cref="Advance"/> began.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Newest first, at most 4, each kept 2.5 s.</summary>
    public List<GameEvent> RecentMessages { get; } = new();

    public World(Track track, FeelSettings feel, IRng rng)
    {
        Track = track;
        Feel = feel;
        Rng = rng;
        View.Build(Track, Player.Distance);
    }

    /// <summary>Runs whole fixed steps for <paramref name="frameSeconds"/> (capped at 0.25 s); returns the step count.</summary>
    public int Advance(ControlFrame input, double frameSeconds)
    {
        _events.Clear();
        _accumulator += Math.Clamp(frameSeconds, 0, MaxFrameSeconds);
        int steps = 0;
        while (_accumulator >= Step - StepEpsilon)
        {
            StepOnce(input);
            _accumulator -= Step;
            steps++;
        }
        if (_accumulator < 0) _accumulator = 0;
        View.Build(Track, Player.Distance);
        return steps;
    }

    public void StepOnce(ControlFrame input)
    {
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
            CarPhysics.StepPlayer(Player, pedals, Steering.Value, Track, Feel, Step);
            if (Player.Armor <= 0) WreckPlayer("SCRAPED TO PIECES");     // guard-rail scraping drains armor in StepPlayer
        }

        StepWeapons(input);
        Loadout.Oil.Update(Step);
        StepHazards();
        StepAgents();       // T10 replaces the Driving branch with real drivers
        Collisions.Resolve(this);

        // Cars killed by gunfire leave the list only now, so nothing above sees the list change mid-step.
        if (_removals.Count > 0)
        {
            Agents.RemoveAll(a => _removals.Contains(a));
            _removals.Clear();
        }

        Player.HitFlash -= Step;
        Time += Step;
        RecentMessages.RemoveAll(e => Time - e.Time > MessageLifetime);
    }

    void StepAgents()
    {
        foreach (var a in Agents)
        {
            switch (a.State)
            {
                case VehicleState.Driving: CarPhysics.StepAgent(a, new DriveIntent(a.Speed, a.Lateral), Step); break;
                case VehicleState.Spinning: CarPhysics.StepSpinning(a, Step); break;
                case VehicleState.Wrecked: CarPhysics.StepWrecked(a, Step); break;
            }
            a.HitFlash -= Step;
            a.ContactCooldown -= Step;
        }
    }

    /// <summary>Spins a car out: it turns, slides sideways in direction <paramref name="dir"/> and loses a fifth of its speed.</summary>
    public void Spin(Vehicle v, int dir, string reason)
    {
        v.State = VehicleState.Spinning;
        v.StateTimer = 1.8;
        v.SpinRate = dir * (6 + Rng.NextDouble() * 3);
        v.PushVel = dir * (4 + Rng.NextDouble() * 3);
        v.Speed *= 0.8;
        Publish(new VehicleSpun(Time, v.Id, reason));
        Publish(new Message(Time, reason, Tone.Good));
    }

    internal void DamagePlayer(double amount, string reason)
    {
        if (Player.State == VehicleState.Wrecked) return;
        Player.Armor -= amount;
        Player.HitFlash = 0.35;
        Publish(new PlayerDamaged(Time, amount, reason));
        Publish(new Message(Time, reason, Tone.Bad));
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
            t.Pose = RoadMotion.MoveStraight(Track, t.Pose, t.Speed * Step);
            t.Travel += MachineGuns.MuzzleSpeed * Step;
            if (t.Travel > MachineGuns.Range || Math.Abs(t.Pose.Lateral) > 60) { t.Dead = true; continue; }
            // Several cars can overlap the swept segment; the bullet hits the one nearest along its path.
            AgentCar? hit = null;
            double nearest = double.MaxValue;
            foreach (var car in Agents)       // Destroy never changes the list, so it can be walked directly
            {
                if (!car.IsOnRoadDriving || !SweptHit(t, car)) continue;
                double along = car.Distance - t.PrevDistance;
                if (along < nearest) { nearest = along; hit = car; }
            }
            if (hit is null) continue;
            t.Dead = true;
            hit.Armor -= 1;
            hit.HitFlash = 0.08;
            if (hit.Armor <= 0) Destroy(hit, KillCause.Gunfire);
        }
        Tracers.RemoveAll(t => t.Dead);
    }

    /// <summary>True when the tracer's last step crossed the car's length within its width (plus a little).</summary>
    static bool SweptHit(Tracer t, AgentCar car)
    {
        double d0 = t.PrevDistance, d1 = t.Pose.Distance;
        double half = car.Stats.Length / 2;
        if (car.Distance + half < Math.Min(d0, d1) || car.Distance - half > Math.Max(d0, d1)) return false;
        double span = d1 - d0;
        double u = Math.Abs(span) < 1e-9 ? 1 : Math.Clamp((car.Distance - d0) / span, 0, 1);
        double lateral = t.PrevLateral + (t.Pose.Lateral - t.PrevLateral) * u;
        return Math.Abs(lateral - car.Lateral) <= car.Stats.Width / 2 + 0.15;
    }

    void StepHazards()
    {
        foreach (var h in Hazards) h.Update(Step, Player.Distance);
        Hazards.RemoveAll(h => h.Expired);
    }

    /// <summary>
    /// Kills an enemy car and scores it. Gunfire leaves a sliding <see cref="Wreck"/> and removes the car at the end of
    /// the step; other causes keep the car as Wrecked for 1.5 s (it is removed in Task 10).
    /// </summary>
    public void Destroy(AgentCar car, KillCause cause)
    {
        if (car.State == VehicleState.Wrecked || _removals.Contains(car)) return;
        car.State = VehicleState.Wrecked;
        car.StateTimer = 1.5;
        int points = car.Stats.Points;
        Score += points;
        Publish(new VehicleDestroyed(Time, car.Id, car.Kind, cause, points));
        Publish(new Message(Time, $"{car.Kind.ToString().ToUpperInvariant()} DESTROYED  +{points}", Tone.Good));
        if (cause == KillCause.Gunfire)
        {
            Hazards.Add(new Wreck
            {
                Distance = car.Distance, Lateral = car.Lateral,
                Width = car.Stats.Width, Length = car.Stats.Length, Speed = car.Speed,
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
                DropOil();
                break;
            // Pause, ToggleHelp, Restart: handled by the mission run.
        }
    }

    void DropOil()
    {
        if (Player.State == VehicleState.Wrecked) return;
        if (Loadout.Oil.TryDrop())
        {
            Hazards.Add(new OilSlick { Distance = Player.Distance - 5, Lateral = Player.Lateral });
            Publish(new Message(Time, "OIL SLICK DROPPED", Tone.Info));
        }
        else if (Loadout.Oil.Charges <= 0)
            Publish(new Message(Time, "NO OIL LEFT (recharging)", Tone.Bad));
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

    /// <summary>FNV-1a 64-bit over the player and every agent's Distance, Lateral, Speed and Armor (to 1e-6).</summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        Mix(ref h, Player);
        foreach (var a in Agents) Mix(ref h, a);
        return h;

        static void Mix(ref ulong h, Vehicle v)
        {
            Add(ref h, v.Distance); Add(ref h, v.Lateral); Add(ref h, v.Speed); Add(ref h, v.Armor);
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
