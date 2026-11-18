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
                Hazards.Add(new Caltrops { Distance = Player.Distance - 5, Lateral = NearestLane(Player.Lateral) });
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

    static double NearestLane(double lateral)
    {
        double best = Track.LaneCentres[0];
        foreach (double lane in Track.LaneCentres)
            if (Math.Abs(lane - lateral) < Math.Abs(best - lateral)) best = lane;
        return best;
    }
}
