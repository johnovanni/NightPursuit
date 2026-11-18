using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

/// <summary>Every contact rule: car against car, car against wreck, car against oil, and running off the road (never a boss).</summary>
public static class Collisions
{
    const double ContactCooldown = 0.25;
    const double WreckDamage = 30;
    const double WreckHitSpeed = 5;
    const double FirstHitPushBack = 1;
    const double CrabSpeed = 3;
    const double OffRoadLimit = Track.RoadHalfWidth + 1.0;
    const double AgentReach = 6.5;              // longer than any two cars' half lengths added together
    const double AgentRearEndDamage = 0.5;
    const double AgentSpinClosing = 4;
    const double ArmoredSpinInto = 8;           // every other kind spins at > 5

    public static void Resolve(World w)
    {
        AgentsVsAgents(w);

        var p = w.Player;
        foreach (var a in w.Agents)
        {
            if (!a.IsOnRoadDriving) continue;
            if (p.State != VehicleState.Wrecked) CarVsCar(w, a);     // the player may have been wrecked earlier in this loop
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

            if (a is not BossCar && Math.Abs(a.Lateral) > OffRoadLimit && (a.State == VehicleState.Spinning || Math.Abs(a.PushVel) > 2 || a.Punctured))
                w.Destroy(a, KillCause.RanOffRoad);
        }

        if (p.State != VehicleState.Wrecked) PlayerVsWrecks(w);
        HazardContacts.Resolve(w);
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
            if (playerInto > (a.Kind == AgentKind.Armored ? ArmoredSpinInto : 5) && a.State == VehicleState.Driving)
                w.Spin(a, side, $"{KindName(a)} KNOCKED INTO A SPIN");
        }

        if (a.Armor <= 0) w.Destroy(a, KillCause.Rammed);
    }

    static readonly Comparison<AgentCar> ByDistanceThenId = (a, b) =>
        a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Id.CompareTo(b.Id);

    /// <summary>
    /// Every pair of enemy cars on the road. Sorted along the road, each car is checked only against the cars just
    /// ahead of it (less than 6.5 m), so 25 cars cost a few dozen checks, not 300.
    /// </summary>
    static void AgentsVsAgents(World w)
    {
        var cars = w.AgentScratch;
        cars.Clear();
        foreach (var a in w.Agents)
            if (a.IsOnRoadDriving) cars.Add(a);
        cars.Sort(ByDistanceThenId);              // Ids are unique, so the order is the same on every run

        for (int i = 0; i < cars.Count; i++)
        {
            for (int j = i + 1; j < cars.Count; j++)
            {
                var a = cars[i];
                var b = cars[j];
                if (b.Distance - a.Distance >= AgentReach) break;
                if (!a.IsOnRoadDriving) break;   // destroyed by an earlier pair this pass
                if (!b.IsOnRoadDriving) continue;
                AgentVsAgent(w, a, b);
            }
        }
        cars.Clear();
    }

    /// <summary><paramref name="a"/> is level with or behind <paramref name="b"/>.</summary>
    static void AgentVsAgent(World w, AgentCar a, AgentCar b)
    {
        if (a.AgentContactCooldown > 0 || b.AgentContactCooldown > 0) return;
        double dz = b.Distance - a.Distance, dx = b.Lateral - a.Lateral;
        double contactZ = (a.Stats.Length + b.Stats.Length) / 2, halfW = (a.Stats.Width + b.Stats.Width) / 2;
        if (dz >= contactZ || Math.Abs(dx) >= halfW) return;

        a.AgentContactCooldown = b.AgentContactCooldown = ContactCooldown;
        double overlapX = halfW - Math.Abs(dx), overlapZ = contactZ - dz;

        if (overlapZ / contactZ < overlapX / halfW)
        {
            // a rear-ended b: held at b's rear edge, a little slower than b (never sped up by a faster car that cut in).
            a.Distance = b.Distance - contactZ;
            a.Speed = Math.Max(0, Math.Min(a.Speed, b.Speed - 2));
            a.Armor -= AgentRearEndDamage;
            b.Armor -= AgentRearEndDamage;
        }
        else
        {
            int side = dx >= 0 ? 1 : -1;                          // +1: b is on a's right
            double closing = Math.Max(0, (a.LateralVel - b.LateralVel) * side);
            double push = 2.5 + 0.8 * closing;
            a.Lateral -= side * overlapX * 0.5;
            b.Lateral += side * overlapX * 0.5;
            a.PushVel = -side * push;
            b.PushVel = side * push;
            if (closing > AgentSpinClosing)
            {
                string reason = $"{KindName(a)} AND {KindName(b)} COLLIDED";
                bool aSpins = a.State == VehicleState.Driving;
                if (aSpins) w.Spin(a, -side, reason);
                if (b.State == VehicleState.Driving) w.Spin(b, side, reason, announce: !aSpins);   // one collision, one message
            }
        }

        if (a.Armor <= 0) w.Destroy(a, KillCause.Rammed);
        if (b.Armor <= 0) w.Destroy(b, KillCause.Rammed);
    }

    /// <summary>
    /// The first touch costs 30 armor and nearly stops the car; after that the wreck just blocks. Hit from behind, the
    /// player is held back; hit from the side, it is pushed out sideways along the shallower overlap, so a wreck can never be driven through.
    /// </summary>
    static void PlayerVsWrecks(World w)
    {
        var p = w.Player;
        foreach (var h in w.Hazards)
        {
            if (h is not Wreck wreck || !Overlaps(p, wreck.Distance, wreck.Lateral, wreck.Length, wreck.Width)) continue;

            bool firstHit = !wreck.HitPlayer;
            if (firstHit)
            {
                wreck.HitPlayer = true;
                p.Speed = Math.Min(p.Speed, WreckHitSpeed);
                p.Shake = 0.5;
                w.DamagePlayer(WreckDamage, "HIT A WRECK");
            }

            double dz = wreck.Distance - p.Distance, dx = p.Lateral - wreck.Lateral;
            double contactZ = (wreck.Length + p.Stats.Length) / 2, halfW = (wreck.Width + p.Stats.Width) / 2;
            double overlapZ = contactZ - Math.Abs(dz), overlapX = halfW - Math.Abs(dx);
            if (overlapX / halfW < overlapZ / contactZ)
            {
                int side = dx > 0 || (dx == 0 && p.LateralVel >= 0) ? 1 : -1;     // which side of the wreck the player is on
                p.Lateral = wreck.Lateral + side * halfW;
                if (p.LateralVel * side < 0) p.LateralVel = 0;
                if (p.PushVel * side < 0) p.PushVel = 0;
            }
            else if (dz > 0)
            {
                // Held back at the wreck's rear edge (1 m further back on the first hit, so the car isn't nose-in).
                p.Distance = wreck.Distance - contactZ - (firstHit ? FirstHitPushBack : 0);
                p.Speed = Math.Min(p.Speed, wreck.Speed);
                // Speed is now ~0, so steering has no turning power: crab sideways round the wreck instead.
                if (Math.Abs(p.Steer) > 0.05) p.Lateral += Math.Sign(p.Steer) * CrabSpeed * World.Step;
            }
        }
    }
}
