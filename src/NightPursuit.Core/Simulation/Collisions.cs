using NightPursuit.Core.Drivers;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

/// <summary>
/// Every contact rule: car against car, car against wreck, car against oil, and running off the road (never a boss).
/// A boss goes through its damage table instead: oil spins it with <see cref="World.SpinBoss"/>, a wreck hurts it once,
/// and ramming and car contact never hurt, spin or destroy it.
/// </summary>
public static class Collisions
{
    const double ContactCooldown = 0.25;
    const double WreckDamage = 30;
    const double WreckHitSpeed = 5;
    const double FirstHitPushBack = 1;
    const double CrabSpeed = 3;
    const double RoadblockCrabSpeed = 5;
    const double RoadblockDamage = 25;
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
                if (h is not Wreck wreck || !Overlaps(a, wreck.Distance, wreck.Lateral, wreck.Length, wreck.Width)) continue;
                if (a is BossCar boss)
                {
                    if (!boss.HazardsMet.Add(wreck)) continue;   // already paid for: it no longer hurts
                    w.BossHitWreck(boss, wreck);
                }
                else w.Destroy(a, KillCause.WreckCollision);
                break;
            }
            if (a.IsOnRoadDriving && a is not BossCar)       // a boss is never destroyed by a roadblock (they are cleared when its stage starts)
            {
                foreach (var h in w.Hazards)
                {
                    if (h is not Roadblock rb || !rb.Touches(a.Distance, a.Lateral, a.Stats.Length, a.Stats.Width)) continue;
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
                        if (a is BossCar boss)
                        {
                            if (!boss.HazardsMet.Add(slick)) continue;   // one spin per slick, or a slow boss is pinned on it
                            w.SpinBoss(boss);
                        }
                        else
                        {
                            if (!slick.Hostile) w.BlamePlayer(a);        // your own oil
                            w.Spin(a, w.Rng.Next(2) == 0 ? -1 : 1, $"{KindName(a)} HIT THE OIL");
                        }
                        break;
                    }
                }
            }

            if (a is not BossCar && Math.Abs(a.Lateral) > OffRoadLimit && (a.State == VehicleState.Spinning || Math.Abs(a.PushVel) > 2 || a.Punctured))
                w.Destroy(a, KillCause.RanOffRoad);
        }

        if (p.State != VehicleState.Wrecked) PlayerVsWrecks(w);
        if (p.State != VehicleState.Wrecked) PlayerVsRoadblocks(w);
        HazardContacts.Resolve(w);
    }

    static string KindName(AgentCar a) => KindRules.DisplayName(a.Kind);

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
                // A rear-end always blames the car (a death within 5 s is yours), but it only costs heat when it was your fault.
                w.BlamePlayer(a);
                if (PlayerAtFault(a, rearEnd: true, 0, 0)) w.OffenceHit(a, bullet: false);
                p.Distance = a.Distance - contactZ;
                double pv = p.Speed;
                p.Speed = Math.Max(0, Math.Min(p.Speed, a.Speed) - 1.5);
                a.Speed = Math.Max(a.Speed, pv * 0.6 + a.Speed * 0.4) + 1.5;
                if (a is not BossCar)                             // ramming never hurts a boss (call 19)
                {
                    a.Armor -= 1 + rel * 0.1;
                    a.HitFlash = 0.1;
                }
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
            // Fault counts only the player's own steering, read from its source (speed and heading): LateralVel also holds the
            // decaying push from an enemy or from enemy oil, which is not the player's doing.
            double ownInto = Math.Max(0, p.Speed * Math.Sin(p.Heading) * side);
            if (PlayerAtFault(a, rearEnd: false, ownInto, agentInto)) Offend(w, a);
            Shove(a, side * (3 + 1.3 * playerInto));
            p.PushVel = -side * (2.5 + 1.0 * agentInto);
            a.Lateral += side * overlapX * 0.5;
            p.Lateral -= side * overlapX * 0.5;
            w.DamagePlayer(2 + agentInto * 1.2, "SIDESWIPED");
            if (playerInto > (a.Kind == AgentKind.Armored ? ArmoredSpinInto : 5) && CanBeSpunByContact(a))
                w.Spin(a, side, $"{KindName(a)} KNOCKED INTO A SPIN");
        }

        if (RammedToDeath(a)) w.Destroy(a, KillCause.Rammed);
    }

    /// <summary>Sideways speed (m/s) of the player's own steering below which a side contact is not blamed on it (lane-keeping corrections).</summary>
    const double OwnSteeringDeadBand = 0.5;

    /// <summary>
    /// Whose fault a contact between the player and <paramref name="a"/> is. The player is at fault for rear-ending a car,
    /// except a police car that is going slower than its cruise speed or is boxing you in as the Front car right now
    /// (F9: it was put there, or held back, to slow you; see <see cref="ChaseDriver.IsBoxingFront"/>), and for a
    /// side contact where its own steering moved it into the car (<paramref name="ownInto"/>, above the 0.5 m/s dead band)
    /// more than the car moved into it (<paramref name="agentInto"/>).
    /// </summary>
    static bool PlayerAtFault(AgentCar a, bool rearEnd, double ownInto, double agentInto)
    {
        if (rearEnd) return !(a.Faction == Faction.Police && (a.Speed < a.CruiseSpeed || ChaseDriver.IsBoxingFront(a)));
        return ownInto > OwnSteeringDeadBand && ownInto > agentInto;
    }

    /// <summary>The player caused this contact: the car blames you for 5 s and a civilian or police car adds heat.</summary>
    static void Offend(World w, AgentCar a)
    {
        w.BlamePlayer(a);
        w.OffenceHit(a, bullet: false);
    }

    // Ramming and car contact never hurt, spin or destroy a boss, and shove it at most 4 m/s (call 19, ENM-3).

    /// <summary>Takes <paramref name="damage"/> armor from an enemy car in a rear-end with another; a boss loses none.</summary>
    static void Knock(AgentCar a, double damage)
    {
        if (a is not BossCar) a.Armor -= damage;
    }

    /// <summary>Sets a car's sideways shove; a boss's is capped at <see cref="BossDamage.MaxPush"/>.</summary>
    static void Shove(AgentCar a, double push) =>
        a.PushVel = a is BossCar ? Math.Clamp(push, -BossDamage.MaxPush, BossDamage.MaxPush) : push;

    static bool CanBeSpunByContact(AgentCar a) => a is not BossCar && a.State == VehicleState.Driving;

    static bool RammedToDeath(AgentCar a) => a is not BossCar && a.Armor <= 0;

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
            Knock(a, AgentRearEndDamage);
            Knock(b, AgentRearEndDamage);
        }
        else
        {
            int side = dx >= 0 ? 1 : -1;                          // +1: b is on a's right
            double closing = Math.Max(0, (a.LateralVel - b.LateralVel) * side);
            double push = 2.5 + 0.8 * closing;
            a.Lateral -= side * overlapX * 0.5;
            b.Lateral += side * overlapX * 0.5;
            Shove(a, -side * push);
            Shove(b, side * push);
            if (closing > AgentSpinClosing)
            {
                string reason = $"{KindName(a)} AND {KindName(b)} COLLIDED";
                var tone = a.Faction == Faction.Hostile && b.Faction == Faction.Hostile ? Tone.Good : Tone.Info;
                bool aSpins = CanBeSpunByContact(a);
                if (aSpins) w.Spin(a, -side, reason, tone: tone);
                if (CanBeSpunByContact(b)) w.Spin(b, side, reason, announce: !aSpins, tone: tone);   // one collision, one message
            }
        }

        if (RammedToDeath(a)) w.Destroy(a, KillCause.Rammed);
        if (RammedToDeath(b)) w.Destroy(b, KillCause.Rammed);
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

            HoldAtBox(p, wreck.Distance, wreck.Lateral, wreck.Length, wreck.Width, wreck.Speed, CrabSpeed, firstHit);
        }
    }

    /// <summary>
    /// Keeps the player out of a solid box (a wreck, or one blocked span of a roadblock). Hit from behind, the player is held back to the
    /// box's rear edge, moving no faster than <paramref name="boxSpeed"/>, and crabs sideways at <paramref name="crab"/> m/s when steering;
    /// hit from the side, it is pushed out sideways along the shallower overlap, so the box can never be driven through.
    /// </summary>
    static void HoldAtBox(PlayerCar p, double distance, double lateral, double length, double width, double boxSpeed, double crab, bool firstHit)
    {
        double dz = distance - p.Distance, dx = p.Lateral - lateral;
        double contactZ = (length + p.Stats.Length) / 2, halfW = (width + p.Stats.Width) / 2;
        double overlapZ = contactZ - Math.Abs(dz), overlapX = halfW - Math.Abs(dx);
        if (overlapX / halfW < overlapZ / contactZ)
        {
            int side = dx > 0 || (dx == 0 && p.LateralVel >= 0) ? 1 : -1;     // which side of the box the player is on
            p.Lateral = lateral + side * halfW;
            if (p.LateralVel * side < 0) p.LateralVel = 0;
            if (p.PushVel * side < 0) p.PushVel = 0;
        }
        else if (dz > 0)
        {
            // Held back at the box's rear edge (1 m further back on the first hit, so the car isn't nose-in).
            p.Distance = distance - contactZ - (firstHit ? FirstHitPushBack : 0);
            p.Speed = Math.Min(p.Speed, boxSpeed);
            // Speed is now ~0, so steering has no turning power: crab sideways round the box instead.
            if (Math.Abs(p.Steer) > 0.05) p.Lateral += Math.Sign(p.Steer) * crab * World.Step;
        }
    }

    /// <summary>
    /// The first touch of a roadblock costs 25 armor and 15 heat and nearly stops the car; after that its two blocked spans just block,
    /// like a wreck, and steering crabs you along them at 5 m/s toward the gap.
    /// </summary>
    static void PlayerVsRoadblocks(World w)
    {
        var p = w.Player;
        foreach (var h in w.Hazards)
        {
            if (h is not Roadblock rb || !rb.Touches(p.Distance, p.Lateral, p.Stats.Length, p.Stats.Width)) continue;

            bool firstHit = !rb.HitPlayer;
            if (firstHit)
            {
                rb.HitPlayer = true;
                p.Speed = Math.Min(p.Speed, WreckHitSpeed);
                p.Shake = 0.5;
                w.DamagePlayer(RoadblockDamage, "HIT A ROADBLOCK");
                w.AddHeat(HeatRules.RoadblockHit, "ROADBLOCK");
                w.Publish(new Message(w.Time, $"ROADBLOCK  +{HeatRules.RoadblockHit:0} HEAT", Tone.Bad));
                w.Publish(new RoadblockHit(w.Time));
            }

            for (int i = 0; i < Track.LaneCentres.Length; i++)
            {
                if (i == rb.GapLane) continue;
                if (!Overlaps(p, rb.Distance, Track.LaneCentres[i], Roadblock.Depth, 2 * Roadblock.BarrierHalfWidth)) continue;
                HoldAtBox(p, rb.Distance, Track.LaneCentres[i], Roadblock.Depth, 2 * Roadblock.BarrierHalfWidth, 0, RoadblockCrabSpeed, firstHit);
            }
        }
    }
}
