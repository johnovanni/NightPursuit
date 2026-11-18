using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

public enum Tone { Info, Good, Bad }

public enum KillCause { Gunfire, RanOffRoad, WreckCollision, Rammed }

/// <summary>Something that happened in the simulation, for the HUD, score and (later) effects.</summary>
public abstract record GameEvent(double Time);

public sealed record Message(double Time, string Text, Tone Tone) : GameEvent(Time);

public sealed record VehicleDestroyed(double Time, int VehicleId, AgentKind Kind, KillCause Cause, int Points) : GameEvent(Time);

public sealed record PlayerDamaged(double Time, double Amount, string Reason) : GameEvent(Time);

public sealed record VehicleSpun(double Time, int VehicleId, string Reason) : GameEvent(Time);

public sealed record PlayerWrecked(double Time, string Reason) : GameEvent(Time);
