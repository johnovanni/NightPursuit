using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    /// <summary>Drops the selected rear weapon behind the player; says why when it cannot.</summary>
    public void DropRear()
    {
        if (Player.State == VehicleState.Wrecked) return;
        var which = Loadout.SelectedRear;
        var weapon = Loadout.Rear(which);
        if (!weapon.TryUse())
        {
            string name = which switch { RearWeapon.Oil => "OIL", RearWeapon.Smoke => "SMOKE", _ => "CALTROPS" };
            if (weapon.Charges <= 0)
                Publish(new Message(Time, which == RearWeapon.Oil ? "NO OIL LEFT (recharging)" : $"NO {name} LEFT", Tone.Bad));
            else
                Publish(new Message(Time, $"{name} NOT READY", Tone.Info));
            return;
        }

        switch (which)
        {
            case RearWeapon.Oil:
                Hazards.Add(new OilSlick { Distance = Player.Distance - 5, Lateral = Player.Lateral });
                Publish(new Message(Time, "OIL SLICK DROPPED", Tone.Info));
                Publish(new WeaponUsed(Time, WeaponKind.Oil, Player.Id));
                break;
            case RearWeapon.Smoke:
                double d = Player.Distance - 6;
                Hazards.Add(new SmokeCloud { Distance = d, PrevDistance = d, Lateral = Player.Lateral, Speed = 0.5 * Player.Speed });
                Publish(new Message(Time, "SMOKE SCREEN", Tone.Info));
                Publish(new WeaponUsed(Time, WeaponKind.Smoke, Player.Id));
                break;
            default:
                Hazards.Add(new Caltrops { Distance = Player.Distance - 5, Lateral = DriverHelpers.NearestLane(Player.Lateral) });
                Publish(new Message(Time, "CALTROPS DROPPED", Tone.Info));
                Publish(new WeaponUsed(Time, WeaponKind.Caltrops, Player.Id));
                break;
        }
    }

    /// <summary>Selects the next rear weapon and says which one it is.</summary>
    public void CycleRear()
    {
        Loadout.CycleRear();
        var w = Loadout.SelectedRearWeapon;
        Publish(new Message(Time, $"REAR WEAPON: {w.Name.ToUpperInvariant()} ({w.Charges} LEFT)", Tone.Info));
    }

    readonly RoadView _lockView = new();
    double _lockViewAt = double.NaN;

    /// <summary>The nearest hostile car on the road inside the missile's lock cone, or null.</summary>
    public AgentCar? FindMissileTarget()
    {
        if (_lockViewAt != Player.Distance)
        {
            _lockView.Build(Track, Player.Distance);
            _lockViewAt = Player.Distance;
        }
        double c = Math.Cos(Player.Heading), s = Math.Sin(Player.Heading);
        AgentCar? best = null;
        double bestAhead = double.MaxValue;
        foreach (var car in Agents)
        {
            if (!car.IsOnRoadDriving || car.Faction != Faction.Hostile) continue;
            double ahead = car.Distance - Player.Distance;
            if (ahead < Missile.LockMin || ahead > Missile.LockRange || ahead >= bestAhead) continue;
            // The same rotation Projector.ToScreen applies, so the lock cone is what the player sees.
            var r = _lockView.At(car.Distance, car.Lateral);
            double x = r.X - Player.Lateral, f = r.F;
            double cx = x * c - f * s, depth = x * s + f * c;
            if (depth <= 0 || Math.Abs(Math.Atan2(cx, depth)) > Missile.LockHalfAngle) continue;
            best = car;
            bestAhead = ahead;
        }
        return best;
    }

    /// <summary>Launches a missile at the best lock, or flying straight when there is none; says why when it cannot.</summary>
    public void FireMissile()
    {
        if (Player.State == VehicleState.Wrecked) return;
        var launcher = Loadout.Missiles;
        if (!launcher.TryUse())
        {
            if (launcher.Charges <= 0) Publish(new Message(Time, "NO MISSILES", Tone.Bad));
            else Publish(new Message(Time, "MISSILE NOT READY", Tone.Info));
            return;
        }
        var target = FindMissileTarget();
        var pose = new RoadPose(Player.Distance + 2.5, Player.Lateral, Player.Heading);
        Missiles.Add(new Missile
        {
            Pose = pose, PrevDistance = pose.Distance, PrevLateral = pose.Lateral, PrevHeading = pose.Heading,
            Speed = Player.Speed + Missile.LaunchBoost, TargetId = target?.Id,
        });
        Publish(new Message(Time, target is null ? "MISSILE AWAY - NO LOCK" : $"MISSILE AWAY - LOCKED ON {target.Kind.ToString().ToUpperInvariant()}", Tone.Info));
        Publish(new WeaponUsed(Time, WeaponKind.Missile, 0));
    }

    void StepMissiles()
    {
        foreach (var m in Missiles)
        {
            AgentCar? target = null;
            if (m.TargetId is int id)
                foreach (var a in Agents)
                    if (a.Id == id) { if (a.IsOnRoadDriving) target = a; break; }
            if (target is not null)
            {
                double want = Math.Atan2(target.Lateral - m.Pose.Lateral, Math.Max(1, target.Distance - m.Pose.Distance));
                double turn = Math.Clamp(want - m.Pose.Heading, -Missile.TurnRate * Step, Missile.TurnRate * Step);
                m.Pose = m.Pose with { Heading = Math.Clamp(m.Pose.Heading + turn, -Missile.MaxHeading, Missile.MaxHeading) };
            }

            var from = m.Pose;
            m.Pose = RoadMotion.MoveStraight(Track, m.Pose, m.Speed * Step);
            m.Travel += m.Speed * Step;

            // Several cars can overlap the swept segment; the missile hits the one nearest along its path.
            AgentCar? hit = null;
            double nearest = double.MaxValue;
            foreach (var car in Agents)
            {
                if (!car.IsOnRoadDriving || car.Faction != Faction.Hostile) continue;
                if (!SweptHit(from.Distance, from.Lateral, m.Pose.Distance, m.Pose.Lateral, car, Missile.HitMargin)) continue;
                double along = car.Distance - from.Distance;
                if (along < nearest) { nearest = along; hit = car; }
            }
            if (hit is not null)
            {
                m.Dead = true;
                Destroy(hit, KillCause.Missile);
            }
            else if (m.Travel > Missile.Range || Math.Abs(m.Pose.Lateral) > 60)
            {
                m.Dead = true;
                Publish(new Message(Time, "MISSILE LOST", Tone.Info));
            }
        }
        Missiles.RemoveAll(m => m.Dead);
    }

    public const double StrikeAhead = 400, StrikeBehind = 150;

    /// <summary>True for a hostile car on the road, driving, from 150 m behind to 400 m ahead of the player.</summary>
    public bool InStrikeRange(AgentCar a)
    {
        if (!a.IsOnRoadDriving || a.Faction != Faction.Hostile) return false;
        double d = a.Distance - Player.Distance;
        return d >= -StrikeBehind && d <= StrikeAhead;
    }

    /// <summary>Starts the satellite sweep; says why when it cannot. The charge is kept when nothing is in range.</summary>
    public void BeginStrike()
    {
        if (Player.State == VehicleState.Wrecked) return;
        var strike = Loadout.Strike;
        if (strike.Targeting) { Publish(new Message(Time, "STRIKE ALREADY TARGETING", Tone.Info)); return; }
        if (strike.Charges <= 0) { Publish(new Message(Time, "NO SATELLITE STRIKE", Tone.Bad)); return; }
        if (!Agents.Any(InStrikeRange)) { Publish(new Message(Time, "NO TARGETS IN STRIKE RANGE", Tone.Info)); return; }
        strike.TryBegin();
        Publish(new WeaponUsed(Time, WeaponKind.Strike, 0));
        Publish(new Message(Time, "SATELLITE STRIKE: TARGETING", Tone.Info));
    }

    /// <summary>Runs the sweep; when it ends, every hostile car in range is destroyed (even if the player is wrecked).</summary>
    void StepStrike()
    {
        if (!Loadout.Strike.AdvanceSweep(Step)) return;
        int destroyed = 0;
        foreach (var car in Agents)
        {
            if (!InStrikeRange(car)) continue;
            Destroy(car, KillCause.Satellite);
            destroyed++;
        }
        Publish(new StrikeLanded(Time, destroyed));
        Publish(new Message(Time, $"SATELLITE STRIKE: {destroyed} DESTROYED", Tone.Good));
    }
}
