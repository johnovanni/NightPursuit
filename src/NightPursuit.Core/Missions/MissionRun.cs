using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Missions;

public enum RunState { Playing, Paused, Respawning, Completed, GameOver }

/// <summary>One attempt at a mission: the world, the director, and what happens after a wreck.</summary>
public sealed class MissionRun
{
    const double RespawnSeconds = 3;
    const double RespawnClearRadius = 200;

    readonly MissionDefinition _def;
    readonly FeelSettings _feel;
    readonly int _seed;

    public World World { get; private set; } = null!;
    public MissionDirector Director { get; private set; } = null!;
    public RunState State { get; private set; }
    /// <summary>Seconds left in the current state (counts down while respawning).</summary>
    public double StateTimer { get; private set; }

    public MissionRun(MissionDefinition def, FeelSettings feel, int seed)
    {
        _def = def;
        _feel = feel;
        _seed = seed;
        Rebuild();
    }

    void Rebuild()
    {
        World = new World(new Track(_def.Road), _feel, new SeededRng(_seed)) { Loadout = new Loadout(_def.StartingOil) };
        Director = new MissionDirector(_def);
        State = RunState.Playing;
        StateTimer = 0;
    }

    public void Update(ControlFrame input, double frameSeconds)
    {
        switch (State)
        {
            case RunState.Playing:
                World.Advance(input, frameSeconds);
                Director.Update(World);
                if (World.Events.Any(e => e is PlayerWrecked)) OnWrecked();
                else if (Director.Completed) State = RunState.Completed;
                break;

            case RunState.Respawning:
                World.Advance(ControlFrame.Empty, frameSeconds);   // the world keeps moving while the wreck coasts
                StateTimer -= frameSeconds;
                if (StateTimer <= 0) Respawn();
                break;
        }
    }

    public void Handle(GameAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.Restart:
                Rebuild();
                break;
            case ActionKind.Pause:
                if (State == RunState.Playing) State = RunState.Paused;
                else if (State == RunState.Paused) State = RunState.Playing;
                break;
            default:
                if (State == RunState.Playing) World.Handle(a);
                break;
        }
    }

    void OnWrecked()
    {
        if (World.Player.SpareCars <= 0)
        {
            State = RunState.GameOver;
            return;
        }
        State = RunState.Respawning;
        StateTimer = RespawnSeconds;
    }

    void Respawn()
    {
        var p = World.Player;
        p.SpareCars--;
        p.Armor = 100;
        p.Speed = 0;
        p.Lateral = 0;
        p.Heading = 0;
        p.PushVel = 0;
        p.State = VehicleState.Driving;
        p.StateTimer = 0;
        World.Steering.Reset();
        World.Scheme.Reset(0);
        World.Agents.RemoveAll(a => Math.Abs(a.Distance - p.Distance) < RespawnClearRadius);
        State = RunState.Playing;
        StateTimer = 0;
    }
}
