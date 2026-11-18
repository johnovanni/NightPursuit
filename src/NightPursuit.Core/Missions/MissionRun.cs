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
        World = new World(new Track(_def.Road), _feel, new SeededRng(_seed)) { Loadout = new Loadout(_def.Loadout) };
        Director = new MissionDirector(_def);
        State = RunState.Playing;
        StateTimer = 0;
    }

    public void Update(ControlFrame input, double frameSeconds)
    {
        switch (State)
        {
            case RunState.Playing:
                World.Advance(input, frameSeconds, AfterPlayingStep);
                break;

            case RunState.Respawning:
                World.Advance(ControlFrame.Empty, frameSeconds, AfterRespawningStep);   // the world keeps moving while the wreck coasts
                break;
        }
    }

    // The rules below run once per fixed step (not once per frame), so a mission plays out the same however
    // frames are split. A step that changes the run state ends the frame, because the next state takes different input.

    bool AfterPlayingStep()
    {
        Director.Update(World);
        if (Director.Completed) { State = RunState.Completed; return true; }   // crossing the finish beats a wreck in the same step
        if (World.Player.State != VehicleState.Wrecked) return false;
        OnWrecked();
        return true;
    }

    bool AfterRespawningStep()
    {
        StateTimer -= World.Step;
        if (StateTimer > 0) return false;
        Respawn();
        return true;
    }

    /// <summary>R restarts only when the run is paused or over, so one stray key can't throw a run away (decision D13).</summary>
    public bool CanRestart => State is RunState.Paused or RunState.Completed or RunState.GameOver;

    public void Handle(GameAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.Restart:
                if (CanRestart) Rebuild();
                else if (State == RunState.Respawning) World.Publish(new Message(World.Time, "RESTART AFTER THE RESPAWN", Tone.Info));
                else World.Publish(new Message(World.Time, "PRESS P TO PAUSE, THEN R TO RESTART", Tone.Info));
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

    /// <summary>
    /// DIAGNOSTICS ONLY (used by <c>--stress</c> so every run-state overlay gets drawn). The game never calls this:
    /// real play reaches these states only through <see cref="Update"/> and <see cref="Handle"/>.
    /// </summary>
    public void ForceStateForDiagnostics(RunState state, double stateTimer = 0)
    {
        State = state;
        StateTimer = stateTimer;
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
        p.GripLoss = 0;       // both only count down while driving, so they would carry into the new car
        p.PunctureLeft = 0;
        p.SnapPrev();   // a teleport: do not draw the car sweeping back to the start
        World.Steering.Reset();
        World.Scheme.Reset(0);
        // The boss is never cleared (Review Focus 1): it goes back 300 m ahead and eases in again.
        World.Agents.RemoveAll(a => a is not BossCar && Math.Abs(a.Distance - p.Distance) < RespawnClearRadius);
        foreach (var a in World.Agents)
            if (a is BossCar boss && boss.State != VehicleState.Wrecked)
                boss.ReturnTo(p.Distance + MissionDirector.BossAheadDistance, Math.Max(0, p.Speed - MissionDirector.BossArrivalSlower));
        World.Hazards.RemoveAll(h => Math.Abs(h.Distance - p.Distance) < RespawnClearRadius);
        State = RunState.Playing;
        StateTimer = 0;
    }
}
