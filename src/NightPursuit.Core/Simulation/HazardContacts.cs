using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

/// <summary>Oil and caltrops against cars: any caltrops puncture enemy cars; enemy oil and caltrops hurt the player.</summary>
static class HazardContacts
{
    const double OilGripLoss = 1.0;
    const double PunctureDuration = 5;
    const double CaltropDamage = 10;

    static void CollectCrates(World w, PlayerCar p)
    {
        for (int i = w.Hazards.Count - 1; i >= 0; i--)
        {
            if (w.Hazards[i] is not SupplyCrate c || c.Expired) continue;
            if (Math.Abs(p.Distance - c.Distance) >= (SupplyCrate.Size + p.Stats.Length) / 2
                || Math.Abs(p.Lateral - c.Lateral) >= (SupplyCrate.Size + p.Stats.Width) / 2) continue;

            int added;
            if (c.Item == SupplyItem.Armor)
            {
                double before = p.Armor;
                p.Armor = Math.Min(100, p.Armor + SupplyCrate.ArmorPerAmount * c.Amount);
                added = (int)Math.Round(p.Armor - before);
            }
            else added = w.Loadout.Restock(c.Item, c.Amount);

            string name = c.Item.ToString().ToUpperInvariant();
            w.Publish(added > 0
                ? new Message(w.Time, $"SUPPLY: +{added} {name}", Tone.Good)
                : new Message(w.Time, $"SUPPLY: {name} ALREADY FULL", Tone.Info));
            w.Publish(new SupplyCollected(w.Time, c.Item, added));
            c.Expired = true;
            w.Hazards.RemoveAt(i);
        }
    }

    public static void Resolve(World w)
    {
        foreach (var a in w.Agents)
        {
            if (!a.IsOnRoadDriving || a.Punctured) continue;
            foreach (var h in w.Hazards)
            {
                if (h is not Caltrops c || !c.Covers(a)) continue;
                a.Punctured = true;
                w.Publish(new VehiclePunctured(w.Time, a.Id));
                w.Publish(new Message(w.Time, $"{a.Kind.ToString().ToUpperInvariant()} PUNCTURED", Tone.Good));
                break;
            }
        }

        var p = w.Player;
        if (p.State == VehicleState.Wrecked) return;
        CollectCrates(w, p);
        foreach (var h in w.Hazards)
        {
            if (!h.Hostile) continue;
            if (h is OilSlick slick && !slick.HitPlayer
                && Math.Abs(p.Distance - slick.Distance) < OilSlick.Radius + p.Stats.Length / 2
                && Math.Abs(p.Lateral - slick.Lateral) < OilSlick.Radius + p.Stats.Width / 2)
            {
                slick.HitPlayer = true;
                p.GripLoss = OilGripLoss;
                p.PushVel = w.Rng.Next(2) == 0 ? -3 : 3;
                p.Speed *= 0.9;
                p.Shake = Math.Max(p.Shake, 0.4);
                w.Publish(new Message(w.Time, "OIL - NO GRIP", Tone.Bad));
            }
            else if (h is Caltrops c && !c.HitPlayer && c.Covers(p))
            {
                c.HitPlayer = true;
                // Enemy caltrops never kill by themselves: armor is left at 1 at the least.
                double damage = Math.Min(CaltropDamage, p.Armor - 1);
                if (damage > 0) w.DamagePlayer(damage, "PUNCTURED");
                else w.Publish(new Message(w.Time, "PUNCTURED", Tone.Bad));
                p.PunctureLeft = PunctureDuration;
            }
        }
    }
}
