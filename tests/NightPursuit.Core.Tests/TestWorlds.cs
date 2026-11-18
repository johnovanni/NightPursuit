using NightPursuit.Core.Road;

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
}
