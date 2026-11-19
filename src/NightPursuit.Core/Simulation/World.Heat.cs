using NightPursuit.Core.Drivers;
using NightPursuit.Core.Police;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    /// <summary>A heat banner does not replace one that went up less than this long ago (the message still goes out).</summary>
    const double BannerHoldSeconds = 1.0;

    /// <summary>How much the police want the player; stays 0 in a mission without police.</summary>
    public HeatMeter Heat { get; } = new();

    /// <summary>Adds heat for <paramref name="reason"/>, publishing the change and, if the level moved, its banner and message.</summary>
    public void AddHeat(double amount, string reason)
    {
        var from = Heat.Level;
        var change = Heat.Add(amount);
        Publish(new HeatChanged(Time, amount, Heat.Value, reason));
        if (change is { } to) PublishLevelChange(from, to);
    }

    /// <summary>Runs the meter's decay for this step, publishing a level change if there is one. Runs in StepOnce after the collisions.</summary>
    void StepHeat()
    {
        var from = Heat.Level;
        if (Heat.Update(Step, PoliceNear()) is { } to) PublishLevelChange(from, to);
    }

    /// <summary>True when a live police car is within <see cref="HeatRules.NoPoliceRadius"/> m of the player, or one is chasing (not on patrol) and can see.</summary>
    bool PoliceNear()
    {
        foreach (var a in Agents)
        {
            if (a.Faction != Faction.Police || a.State == VehicleState.Wrecked) continue;
            if (Math.Abs(a.Distance - Player.Distance) <= HeatRules.NoPoliceRadius) return true;
            if (!ReferenceEquals(a.Driver, PatrolDriver.Instance) && a.Blinded <= 0) return true;
        }
        return false;
    }

    /// <summary>Publishes the level event and exactly one message (a non-urgent banner can be held back, so the message always goes out), and the banner on a rise.</summary>
    void PublishLevelChange(HeatLevel from, HeatLevel to)
    {
        Publish(new HeatLevelChanged(Time, from, to));
        string name = HeatRules.LevelName(to);
        if (to < from)
        {
            Publish(new Message(Time, $"HEAT DOWN: {name}", Tone.Good));
            return;
        }
        if (to == HeatLevel.Watched)
        {
            Publish(new Message(Time, "HEAT: WATCHED - THE POLICE ARE TAKING NOTE", Tone.Info));
            return;
        }
        Publish(new Message(Time, $"HEAT: {name}", Tone.Bad));
        if (Announcement is { } shown && Time - shown.Start < BannerHoldSeconds) return;
        switch (to)
        {
            case HeatLevel.Patrol: Announce("POLICE PATROLS", "SPEED TRAPS AND ROADBLOCKS AHEAD", 2.5); break;
            case HeatLevel.Pursuit: Announce("POLICE PURSUIT", "DON'T LET THEM STOP YOU", 2.5); break;
            case HeatLevel.Manhunt: Announce("MANHUNT", "THEY WILL RAM YOU - SHAKE THEM WITH SMOKE", 2.5); break;
        }
    }
}
