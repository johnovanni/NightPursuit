using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

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
    readonly IThrottleScheme[] _schemes = { new RampPedalScheme(), new SpeedLeverScheme() };
    double _accumulator;
    int _lastId;

    public Track Track { get; }
    public RoadView View { get; } = new();
    public FeelSettings Feel { get; }
    public IRng Rng { get; }
    public PlayerCar Player { get; } = new() { Id = 0 };
    public List<AgentCar> Agents { get; } = new();
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
        var pedals = Scheme.Update(input, Player.Speed, Feel, Step);
        if (input.PadConnected && (input.PadGas > 0.03 || input.PadBrake > 0.03))
            pedals = new PedalOutput(input.PadGas, input.PadBrake);   // analog triggers work in every scheme

        Steering.Update(input, Feel, Step);
        CarPhysics.StepPlayer(Player, pedals, Steering.Value, Track, Feel, Step);

        // T8: weapons and tracers
        // T9: agents (drivers, spawning) and collisions
        // T10: hazards

        Time += Step;
        RecentMessages.RemoveAll(e => Time - e.Time > MessageLifetime);
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
            // DropRear: Task 9. Pause, ToggleHelp, Restart: handled by the mission run.
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
