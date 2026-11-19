using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Traffic;

/// <summary>The one place that says where a new civilian or police car may appear (shared by the traffic and police spawners).</summary>
public static class SpawnSpots
{
    /// <summary>Nearest another car may be in the same lane, metres.</summary>
    public const double CarGap = 40;
    /// <summary>Nearest a wreck (in any lane) may be, metres. Task 6 adds roadblocks.</summary>
    public const double WreckGap = 60;
    /// <summary>Nearest a hazard in the same lane may be, metres.</summary>
    public const double HazardGap = 30;
    /// <summary>A car or hazard counts as being in a lane when its lateral position is within this of the lane's centre.</summary>
    public const double LaneHalfWidth = 2.5;

    /// <summary>True when a car may appear at <paramref name="distance"/> in the lane centred at <paramref name="lane"/>.</summary>
    public static bool IsFree(World w, double distance, double lane)
    {
        foreach (var a in w.Agents)
            if (Math.Abs(a.Lateral - lane) < LaneHalfWidth && Math.Abs(a.Distance - distance) < CarGap) return false;
        foreach (var h in w.Hazards)
        {
            double gap = Math.Abs(h.Distance - distance);
            if (h is Wreck) { if (gap < WreckGap) return false; }
            else if (Math.Abs(h.Lateral - lane) < LaneHalfWidth && gap < HazardGap) return false;
        }
        return true;
    }

    /// <summary>
    /// Tries the three lanes in turn, starting from a random one (one draw from <see cref="World.TrafficRng"/>); returns the first
    /// free lane centre, or null if none is.
    /// </summary>
    public static double? PickFreeLane(World w, double distance)
    {
        int first = w.TrafficRng.Next(Track.LaneCentres.Length);
        for (int i = 0; i < Track.LaneCentres.Length; i++)
        {
            double lane = Track.LaneCentres[(first + i) % Track.LaneCentres.Length];
            if (IsFree(w, distance, lane)) return lane;
        }
        return null;
    }

    /// <summary>
    /// Where the i-th of <paramref name="count"/> cars goes when a window is filled at once: evenly spread across
    /// <paramref name="from"/>..<paramref name="to"/> (metres from the player) with a random offset of up to 40 m either way (one draw),
    /// never closer to the player than <paramref name="clearOfPlayer"/>. Returns the offset from the player.
    /// </summary>
    public static double SeedOffset(World w, int i, int count, double from, double to, double clearOfPlayer)
    {
        double offset = from + (i + 0.5) * (to - from) / count + (w.TrafficRng.NextDouble() * 2 - 1) * 40;
        if (Math.Abs(offset) < clearOfPlayer) offset = offset < 0 ? -clearOfPlayer : clearOfPlayer;
        return offset;
    }
}
