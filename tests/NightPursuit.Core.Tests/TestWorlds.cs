using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Drivers;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

/// <summary>Small ready-made tracks and worlds for tests.</summary>
static class TestWorlds
{
    /// <summary>A dead-straight, flat road, 20 km long.</summary>
    public static Track Straight() => Track.FromSegments(new double[4000]);

    /// <summary>A constant bend of the given radius (metres), curving right, 20 km long.</summary>
    public static Track Bend(double radius)
    {
        var c = new double[4000];
        Array.Fill(c, 1 / radius);
        return Track.FromSegments(c);
    }

    /// <summary>A world on the given track (default: the generated track for the seed).</summary>
    public static World World(int seed = 1, Track? track = null) =>
        new(track ?? new Track(new TrackProfile(seed)), new FeelSettings(), new SeededRng(seed));
}

/// <summary>A driver that keeps its car exactly as it is, for tests that need a predictable target.</summary>
sealed class StillDriver : IDriver
{
    public DriveIntent Decide(AgentCar self, WorldView view, double dt) => new(self.Speed, self.Lateral);
}
