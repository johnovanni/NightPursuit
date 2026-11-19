using NightPursuit.Core.Common;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    /// <summary>A roadblock is cleared when a wreck blocks its gap lane from this far before it to this far after it, metres.</summary>
    const double GapWreckBefore = 150, GapWreckAfter = 30;
    /// <summary>The half width the gap check allows a car, so a wreck that would stop a car passing counts.</summary>
    const double GapCarHalfWidth = 1.0;

    /// <summary>Called right after the player moves: a trap the player's front just crossed checks the speed, once.</summary>
    void CheckSpeedTraps()
    {
        foreach (var h in Hazards)
        {
            if (h is not SpeedTrap trap || trap.Triggered) continue;
            if (!(Player.PrevDistance < trap.Distance && trap.Distance <= Player.Distance)) continue;
            trap.Triggered = true;
            double mph = Units.MsToMph(Player.Speed);
            if (mph > trap.LimitMph)
            {
                trap.Caught = true;
                AddHeat(HeatRules.SpeedTrap, "SPEED TRAP");
                Publish(new Message(Time, $"SPEED TRAP {mph:0} IN A {trap.LimitMph:0}  +{HeatRules.SpeedTrap:0} HEAT", Tone.Bad));
                Publish(new SpeedTrapPassed(Time, mph, trap.LimitMph));
            }
            else Publish(new Message(Time, "SPEED TRAP: UNDER THE LIMIT", Tone.Good));
        }
    }

    /// <summary>A roadblock whose gap lane has a wreck in it would box the player in, so it is removed at once.</summary>
    void ClearBlockedRoadblocks()
    {
        for (int i = Hazards.Count - 1; i >= 0; i--)
        {
            if (Hazards[i] is not Roadblock block || !GapIsBlocked(block)) continue;
            Hazards.RemoveAt(i);
            Publish(new Message(Time, "ROADBLOCK CLEARED", Tone.Good));
        }
    }

    bool GapIsBlocked(Roadblock block)
    {
        foreach (var h in Hazards)
        {
            if (h is not Wreck wreck) continue;
            double d = wreck.Distance - block.Distance;
            if (d >= -GapWreckBefore && d <= GapWreckAfter && wreck.Blocks(block.Lateral, GapCarHalfWidth)) return true;
        }
        return false;
    }
}
