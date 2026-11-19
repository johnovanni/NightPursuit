using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

public enum Tone { Info, Good, Bad }

/// <summary>How an enemy missile ended: it hit you, you dodged it (or it missed because you were wrecked or respawned), or your guns shot it down.</summary>
public enum MissileResult { Hit, Dodged, ShotDown }

public enum KillCause { Gunfire, RanOffRoad, WreckCollision, Rammed, Missile, Satellite }

public static class KillCauseRules
{
    /// <summary>Shot, blasted or struck from the sky: the car leaves a sliding wreck. Other causes keep it as a burnt-out car for 1.5 s.</summary>
    public static bool LeavesWreck(this KillCause c) => c is KillCause.Gunfire or KillCause.Missile or KillCause.Satellite;
}

/// <summary>Something that happened in the simulation, for the HUD, score and (later) effects.</summary>
public abstract record GameEvent(double Time);

public sealed record Message(double Time, string Text, Tone Tone) : GameEvent(Time);

public sealed record VehicleDestroyed(double Time, int VehicleId, AgentKind Kind, KillCause Cause, int Points) : GameEvent(Time);

public sealed record PlayerDamaged(double Time, double Amount, string Reason) : GameEvent(Time);

public sealed record VehiclePunctured(double Time, int VehicleId) : GameEvent(Time);

public sealed record VehicleSpun(double Time, int VehicleId, string Reason) : GameEvent(Time);

public sealed record PlayerWrecked(double Time, string Reason) : GameEvent(Time);

public sealed record WeaponUsed(double Time, WeaponKind Weapon, int OwnerId) : GameEvent(Time);
public sealed record SupplyCollected(double Time, SupplyItem Item, int Added) : GameEvent(Time);
public sealed record StrikeLanded(double Time, int Destroyed) : GameEvent(Time);
/// <summary>The boss lost <paramref name="Amount"/> health (what was really taken, after the phase floor).</summary>
public sealed record BossDamaged(double Time, double Amount, string Reason) : GameEvent(Time);
/// <summary>A warning of this kind appeared on screen this step (it was not there the step before); <paramref name="Text"/> is what it says.</summary>
public sealed record WarningRaised(double Time, WarningKind Kind, string Text) : GameEvent(Time);
/// <summary>The mission's boss, called <paramref name="Name"/>, appeared at the end of the approach.</summary>
public sealed record BossAppeared(double Time, string Name) : GameEvent(Time);
/// <summary>The boss began phase <paramref name="Phase"/> (1-based) of <paramref name="PhaseCount"/>, driving as <paramref name="Behaviour"/>.</summary>
public sealed record BossPhaseChanged(double Time, int Phase, int PhaseCount, AgentKind Behaviour) : GameEvent(Time);
/// <summary>The boss launched enemy missile <paramref name="MissileId"/>.</summary>
public sealed record EnemyMissileLaunched(double Time, int MissileId) : GameEvent(Time);
/// <summary>Enemy missile <paramref name="MissileId"/> is gone: it hit you, was dodged, or was shot down.</summary>
public sealed record EnemyMissileEnded(double Time, int MissileId, MissileResult Result) : GameEvent(Time);
/// <summary>Heat changed by <paramref name="Amount"/> (as asked, before clamping) for <paramref name="Reason"/>; <paramref name="Value"/> is the heat now.</summary>
public sealed record HeatChanged(double Time, double Amount, double Value, string Reason) : GameEvent(Time);
/// <summary>The heat level went from <paramref name="From"/> to <paramref name="To"/>.</summary>
public sealed record HeatLevelChanged(double Time, Police.HeatLevel From, Police.HeatLevel To) : GameEvent(Time);
