using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

/// <summary>Every contact rule: car against car, car against wreck, car against oil, and running off the road.</summary>
public static class Collisions
{
    const double ContactCooldown = 0.25;
    const double WreckDamage = 30;
    const double WreckHitSpeed = 5;
    const double OffRoadLimit = Track.RoadHalfWidth + 1.0;

    public static void Resolve(World w)
    {
        var p = w.Player;
        bool playerLive = p.State != VehicleState.Wrecked;

        foreach (var a in w.Agents)
        {
            if (!a.IsOnRoadDriving) continue;
            if (playerLive) CarVsCar(w, a);
            if (!a.IsOnRoadDriving) continue;

            foreach (var h in w.Hazards)
            {
                if (h is Wreck wreck && Overlaps(a, wreck.Distance, wreck.Lateral, wreck.Length, wreck.Width))
                {
                    w.Destroy(a, KillCause.WreckCollision);
                    break;
                }
            }
            if (!a.IsOnRoadDriving) continue;

            if (a.State == VehicleState.Driving)
            {
                foreach (var h in w.Hazards)
                {
                    if (h is OilSlick slick
                        && Math.Abs(a.Distance - slick.Distance) < OilSlick.Radius + a.Stats.Length / 2
                        && Math.Abs(a.Lateral - slick.Lateral) < OilSlick.Radius + a.Stats.Width / 2)
                    {
                        w.Spin(a, w.Rng.Next(2) == 0 ? -1 : 1, $"{KindName(a)} HIT THE OIL");
                        break;
                    }
                }
            }

            if (Math.Abs(a.Lateral) > OffRoadLimit && (a.State == VehicleState.Spinning || Math.Abs(a.PushVel) > 2))
                w.Destroy(a, KillCause.RanOffRoad);
        }

        if (playerLive) PlayerVsWrecks(w);
    }

    static string KindName(AgentCar a) => a.Kind.ToString().ToUpperInvariant();

    static bool Overlaps(Vehicle v, double distance, double lateral, double length, double width) =>
        Math.Abs(distance - v.Distance) < (length + v.Stats.Length) / 2
        && Math.Abs(lateral - v.Lateral) < (width + v.Stats.Width) / 2;

    static void CarVsCar(World w, AgentCar a)
    {
        var p = w.Player;
        double dz = a.Distance - p.Distance, dx = a.Lateral - p.Lateral;
        double contactZ = (a.Stats.Length + p.Stats.Length) / 2, halfW = (a.Stats.Width + p.Stats.Width) / 2;
        if (Math.Abs(dz) >= contactZ || Math.Abs(dx) >= halfW || a.ContactCooldown > 0) return;

        a.ContactCooldown = ContactCooldown;
        double overlapX = halfW - Math.Abs(dx), overlapZ = contactZ - Math.Abs(dz);
        p.Shake = 0.35;

        if (overlapZ / contactZ < overlapX / halfW)
        {
            double rel = Math.Abs(p.Speed - a.Speed);
            if (dz > 0)
            {
                // The player rear-ended the agent.
                p.Distance = a.Distance - contactZ;
                double pv = p.Speed;
                p.Speed = Math.Max(0, Math.Min(p.Speed, a.Speed) - 1.5);
                a.Speed = Math.Max(a.Speed, pv * 0.6 + a.Speed * 0.4) + 1.5;
                a.Armor -= 1 + rel * 0.1;
                a.HitFlash = 0.1;
                w.DamagePlayer(3 + rel * 0.5, "RAMMED");
            }
            else
            {
                // The agent rear-ended the player.
                a.Distance = p.Distance - contactZ;
                double ev = a.Speed;
                a.Speed = Math.Max(0, p.Speed - 3);
                p.Speed = Math.Max(p.Speed, ev * 0.5 + p.Speed * 0.5);
                w.DamagePlayer(3 + rel * 0.5, "HIT FROM BEHIND");
            }
        }
        else
        {
            int side = dx >= 0 ? 1 : -1;                          // +1: the agent is on the player's right
            double playerInto = Math.Max(0, p.LateralVel * side);
            double agentInto = Math.Max(0, -a.LateralVel * side);
            a.PushVel = side * (3 + 1.3 * playerInto);
            p.PushVel = -side * (2.5 + 1.0 * agentInto);
            a.Lateral += side * overlapX * 0.5;
            p.Lateral -= side * overlapX * 0.5;
            w.DamagePlayer(2 + agentInto * 1.2, "SIDESWIPED");
            if (playerInto > 5 && a.State == VehicleState.Driving)
                w.Spin(a, side, $"{KindName(a)} KNOCKED INTO A SPIN");
        }

        if (a.Armor <= 0) w.Destroy(a, KillCause.Rammed);
    }

    /// <summary>The first touch costs 30 armor and nearly stops the car; after that the wreck just blocks.</summary>
    static void PlayerVsWrecks(World w)
    {
        var p = w.Player;
        foreach (var h in w.Hazards)
        {
            if (h is not Wreck wreck || !Overlaps(p, wreck.Distance, wreck.Lateral, wreck.Length, wreck.Width)) continue;

            if (!wreck.HitPlayer)
            {
                wreck.HitPlayer = true;
                p.Speed = Math.Min(p.Speed, WreckHitSpeed);
                p.Shake = 0.5;
                w.DamagePlayer(WreckDamage, "HIT A WRECK");
            }
            if (wreck.Distance > p.Distance)
            {
                p.Distance = wreck.Distance - (wreck.Length + p.Stats.Length) / 2;
                p.Speed = Math.Min(p.Speed, wreck.Speed);
            }
        }
    }
}
