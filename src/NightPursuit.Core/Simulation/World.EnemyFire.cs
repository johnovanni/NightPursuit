using NightPursuit.Core.Common;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    const double EnemyMuzzleSpeed = 160, EnemyRange = 120, EnemyTracerMargin = 0.15;

    /// <summary>Fires an enemy car's weapons for the intent it just drove; runs after the car has moved.</summary>
    void StepAgentWeapons(AgentCar a, DriveIntent intent)
    {
        if (a.Arms.RearGuns is { } guns && guns.TryFire(intent.Fire, Step))
        {
            double d = a.Distance - a.Stats.Length / 2 - 0.3;
            Tracers.Add(new Tracer
            {
                Pose = new RoadPose(d, a.Lateral, Math.PI),
                PrevDistance = d, PrevLateral = a.Lateral, PrevHeading = Math.PI,   // created after the Prev refresh: drawn where it starts
                Speed = Math.Max(20, EnemyMuzzleSpeed - a.Speed),
                MuzzleSpeed = EnemyMuzzleSpeed, Range = EnemyRange,
                OwnerId = a.Id, Hostile = true,
            });
        }
        if (intent.DropRear) DropFromAgent(a);
        if (intent.FireMissile && a.Arms.Missiles is { } rack) LaunchEnemyMissile(a, rack);
    }

    int _lastEnemyMissileId;

    /// <summary>
    /// Launches a missile from the car's end nearest the player, in the car's lane, and announces it. Refused (with the
    /// rack kept ready) when the gap, rechecked now the car has moved, is outside 60-160 m, when a missile is already in
    /// flight, or while the player is wrecked, so nothing unannounced is in flight when the next car starts.
    /// </summary>
    void LaunchEnemyMissile(AgentCar a, EnemyMissileRack rack)
    {
        double gap = a.Distance - Player.Distance;
        if (Player.State == VehicleState.Wrecked || !EnemyMissile.InLaunchWindow(gap) || EnemyMissiles.Count > 0
            || !rack.TryLaunch()) return;
        int dir = gap > 0 ? 1 : -1;
        double d = a.Distance - dir * a.Stats.Length / 2;
        EnemyMissiles.Add(new EnemyMissile
        {
            Id = ++_lastEnemyMissileId, Distance = d, Lateral = a.Lateral,
            PrevDistance = d, PrevLateral = a.Lateral,          // created after the Prev refresh: drawn where it starts (D18)
            TargetLateral = Player.Lateral, LaunchTime = Time, Dir = dir,
        });
        Publish(new EnemyMissileLaunched(Time, _lastEnemyMissileId));
        Publish(new Message(Time, "MISSILE INCOMING", Tone.Bad));
    }

    /// <summary>
    /// Moves the first <paramref name="count"/> enemy missiles (those in flight when the step began; one launched during
    /// this step starts next step). Runs after everything that moves the player this step, collisions included, so
    /// each missile follows the player's own movement and closes at exactly 25 m/s, whatever the player does.
    /// </summary>
    void StepEnemyMissiles(int count)
    {
        var p = Player;
        double moved = p.Distance - p.PrevDistance;
        for (int i = 0; i < count; i++)
        {
            var m = EnemyMissiles[i];
            m.Distance += moved - m.Dir * EnemyMissile.ClosingSpeed * Step;
            if (!m.Locked) m.TargetLateral = p.Lateral;
            m.Lateral = MathX.MoveToward(m.Lateral, m.TargetLateral, EnemyMissile.LateralRate * Step);
            if (!m.Locked && m.TimeToImpact(p) <= EnemyMissile.LockSeconds) m.Locked = true;   // stops following your lane
            if (m.Dir * (m.Distance - p.Distance) <= EnemyMissile.ContactGap) EnemyMissileArrives(m);   // reached you, or passed
        }
        EnemyMissiles.RemoveAll(m => m.Dead);
    }

    /// <summary>Hits a player still in its line for 25; otherwise it was dodged. A wrecked car is never hit (preflight F19).</summary>
    void EnemyMissileArrives(EnemyMissile m)
    {
        m.Dead = true;
        if (Player.State == VehicleState.Wrecked)
        {
            Publish(new EnemyMissileEnded(Time, m.Id, MissileResult.Dodged));
            Publish(new Message(Time, "MISSILE MISSED", Tone.Info));
        }
        else if (Math.Abs(m.Lateral - Player.Lateral) < EnemyMissile.HitHalfWidth)
        {
            Publish(new EnemyMissileEnded(Time, m.Id, MissileResult.Hit));
            DamagePlayer(EnemyMissile.Damage, "MISSILE HIT");
        }
        else
        {
            Publish(new EnemyMissileEnded(Time, m.Id, MissileResult.Dodged));
            Publish(new Message(Time, "MISSILE DODGED", Tone.Good));
        }
    }

    /// <summary>
    /// True when the player's bullet <paramref name="t"/> crossed an enemy missile this step (1.2 m by 1.6 m, margin
    /// 0.15): both are gone and the player scores 50. Dead missiles leave the list after the tracer loop.
    /// </summary>
    bool ShootDownEnemyMissile(Tracer t)
    {
        foreach (var m in EnemyMissiles)
        {
            if (m.Dead || !SweptHit(t.PrevDistance, t.PrevLateral, t.Pose.Distance, t.Pose.Lateral, m.Distance, m.Lateral,
                    EnemyMissile.GunTargetHalfLength, EnemyMissile.GunTargetHalfWidth, EnemyMissile.GunTargetMargin)) continue;
            t.Dead = true;
            m.Dead = true;
            Score += EnemyMissile.ShotDownPoints;
            Publish(new EnemyMissileEnded(Time, m.Id, MissileResult.ShotDown));
            Publish(new Message(Time, $"MISSILE SHOT DOWN +{EnemyMissile.ShotDownPoints}", Tone.Good));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Drops the hazard the car's <see cref="AgentArms.NextDropIsOil"/> asks for, or the other one if that cannot be used.
    /// Nothing drops (and nothing is published) when neither can, or when the player would have too little time to react.
    /// </summary>
    void DropFromAgent(AgentCar a)
    {
        if (DropperDriver.NextDrop(a.Arms) is not bool oil
            || DropperDriver.ReactionGap(a, Player, oil) < DropperDriver.RequiredGap(Player))   // a switched drop is rechecked here
        {
            a.Arms.LastDropSkipped = true;      // the driver retries soon
            return;
        }
        double d = DropperDriver.CentreBehind(a, oil);
        if (oil)
        {
            a.Arms.Oil!.TryDrop();
            Hazards.Add(new OilSlick { Distance = d, Lateral = a.Lateral, Hostile = true });
            Publish(new WeaponUsed(Time, WeaponKind.Oil, a.Id));
            Publish(new Message(Time, "DROPPER: OIL AHEAD", Tone.Bad));
        }
        else
        {
            a.Arms.Caltrops!.TryUse();
            Hazards.Add(new Caltrops { Distance = d, Lateral = DriverHelpers.NearestLane(a.Lateral), Hostile = true });
            Publish(new WeaponUsed(Time, WeaponKind.Caltrops, a.Id));
            Publish(new Message(Time, "DROPPER: CALTROPS AHEAD", Tone.Bad));
        }
        a.Arms.NextDropIsOil = !oil;
    }

    /// <summary>An enemy bullet only ever tests the player; it passes through other cars.</summary>
    void StepHostileTracer(Tracer t)
    {
        if (Player.State == VehicleState.Wrecked) return;
        if (!SweptHit(t.PrevDistance, t.PrevLateral, t.Pose.Distance, t.Pose.Lateral, Player, EnemyTracerMargin)) return;
        t.Dead = true;
        DamagePlayer(2, "SHOT BY A GUNNER", messageGap: 1.0);
    }
}
