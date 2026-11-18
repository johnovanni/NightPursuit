using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    /// <summary>
    /// Hurts the boss. Any hit engages it, but while its phase shield is up the damage is ignored. Health never falls
    /// below the current phase's threshold in one blow; reaching it starts the next phase. Only the last phase can reach 0.
    /// </summary>
    public void DamageBoss(BossCar b, double amount, KillCause cause, string reason)
    {
        b.Engaged = true;
        if (b.Shield > 0 || b.State == VehicleState.Wrecked) return;

        double floor = b.Phase.EndsBelow * b.MaxHealth;
        double before = b.Armor;
        b.Armor = Math.Min(b.Armor, Math.Max(floor, b.Armor - amount));   // never heals one already below the floor
        Publish(new BossDamaged(Time, before - b.Armor, reason));

        bool lastPhase = b.PhaseIndex == b.Definition.Phases.Count - 1;
        if (lastPhase)
        {
            if (b.Armor <= 0) Destroy(b, cause);
        }
        else if (b.Armor <= floor)
            NextPhase(b);
    }

    void NextPhase(BossCar b)
    {
        b.PhaseIndex++;
        b.DriverState = null;                  // the new behaviour starts fresh
        b.Shield = BossDamage.PhaseShieldSeconds;
        int n = b.PhaseIndex + 1, m = b.Definition.Phases.Count;
        var behaviour = b.Phase.Behaviour;
        Publish(new BossPhaseChanged(Time, n, m, behaviour));
        Publish(new Message(Time, $"BOSS PHASE {n} OF {m}: {behaviour.ToString().ToUpperInvariant()}", Tone.Info));
    }

    /// <summary>Spins the boss out in place for 1.5 s with its armor open; this engages it too.</summary>
    public void SpinBoss(BossCar b)
    {
        b.Engaged = true;
        if (b.State == VehicleState.Wrecked) return;
        b.State = VehicleState.Spinning;
        b.StateTimer = BossDamage.OilSpinSeconds;
        b.SpinRate = (Rng.Next(2) == 0 ? -1 : 1) * 7;
        b.PushVel = 0;
        b.Speed *= 0.85;
        Publish(new Message(Time, "BOSS SPUN OUT - ARMOR OPEN", Tone.Good));
    }

    /// <summary>One bullet: 1 point, doubled while it spins with its armor open, a quarter in an Armored phase.</summary>
    static double BossGunfireDamage(BossCar b) =>
        (b.ArmorOpen ? BossDamage.OpenArmorGunFactor : 1)
        * (b.Phase.Behaviour == AgentKind.Armored ? BossDamage.ArmoredPhaseGunFactor : 1);

    /// <summary>The first touch of a wreck (not <see cref="BossCar.LastWreck"/>): 10% of its health, a swerve and a slowdown; the wreck never destroys it by itself.</summary>
    internal void BossHitWreck(BossCar b, Wreck wreck)
    {
        b.LastWreck = wreck;
        b.PushVel = RoomierSide(b, wreck) * BossDamage.MaxPush;
        b.Speed *= BossDamage.WreckSpeedFactor;
        Publish(new Message(Time, "BOSS HIT A WRECK", Tone.Good));
        DamageBoss(b, BossDamage.WreckFraction * b.MaxHealth, KillCause.WreckCollision, "HIT A WRECK");
    }

    /// <summary>
    /// +1 or -1: the side of the wreck with more road beside it; a wreck on the centre line sends it to the side it is
    /// already on (right when exactly level).
    /// </summary>
    static int RoomierSide(BossCar b, Wreck wreck)
    {
        if (wreck.Lateral != 0) return wreck.Lateral < 0 ? 1 : -1;
        return b.Lateral >= wreck.Lateral ? 1 : -1;
    }

    /// <summary>Caltrops never puncture the boss: each set slows it once for 4 s from first contact, and engages it.</summary>
    internal void SlowBoss(BossCar b, Caltrops c)
    {
        b.LastCaltrops = c;
        b.SlowLeft = BossDamage.CaltropSlowSeconds;
        b.Engaged = true;
        Publish(new Message(Time, "BOSS SLOWED BY CALTROPS", Tone.Good));
    }

    /// <summary>Keeps all of the boss on the road: no ram, oil or wreck can push it off (Review Focus 1).</summary>
    static void KeepBossOnRoad(BossCar b)
    {
        double limit = DriverHelpers.OnRoadLimit(b);
        if (Math.Abs(b.Lateral) <= limit) return;
        int side = Math.Sign(b.Lateral);
        b.Lateral = side * limit;
        if (b.PushVel * side > 0) b.PushVel = 0;   // an outward shove stops at the edge
    }

    /// <summary>The boss's own countdowns: the phase shield and the caltrops slow.</summary>
    static void CountDownBoss(BossCar b)
    {
        b.Shield = DriverHelpers.Countdown(b.Shield, Step);
        b.SlowLeft = DriverHelpers.Countdown(b.SlowLeft, Step);
    }
}
