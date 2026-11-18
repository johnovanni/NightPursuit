using NightPursuit.Core.Hazards;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

/// <summary>Oil and caltrops against cars: any caltrops puncture enemy cars; enemy oil and caltrops hurt the player.</summary>
static class HazardContacts
{
    const double OilGripLoss = 1.0;
    const double PunctureSeconds = 5;
    const double CaltropDamage = 10;

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
                w.DamagePlayer(CaltropDamage, "PUNCTURED");
                p.PunctureLeft = PunctureSeconds;
            }
        }
    }
}
