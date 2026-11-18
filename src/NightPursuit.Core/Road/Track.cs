using NightPursuit.Core.Common;

namespace NightPursuit.Core.Road;

/// <summary>A looping procedural road: curvature and height per 5 m segment.</summary>
public sealed class Track
{
    public const double SegmentLength = 5.0;
    public const double RoadHalfWidth = 7.5;   // three 5 m lanes
    public const double LaneLine = 2.5;
    public const double GuardRail = 12.0;

    public static readonly double[] LaneCentres = { -5, 0, 5 };

    private const int StraightStart = 60;
    private const int StraightEnd = 60;

    private readonly double[] _curve;    // 1/m, positive bends right; constant within a segment
    private readonly double[] _height;   // m at segment start
    private readonly double[] _heading;  // radians at segment start

    public Track(TrackProfile p)
    {
        int n = p.Segments;
        _curve = new double[n];
        var rng = new SeededRng(p.Seed);
        int radiusSpan = Math.Max(1, (int)(p.MaxRadius - p.MinRadius));

        int i = StraightStart;
        while (i < n - 120)
        {
            i += 20 + rng.Next(70);                  // straight of 20-89 segments
            int len = 50 + rng.Next(110);            // bend of 50-159 segments
            double radius = (int)p.MinRadius + rng.Next(radiusSpan);
            double k = (rng.Next(2) == 0 ? -1 : 1) / radius;
            for (int j = 0; j < len && i + j < n - StraightEnd; j++)
            {
                double t = (j + 0.5) / len;
                double e = Math.Min(1, Math.Min(t, 1 - t) / 0.25);
                _curve[i + j] = k * e * e * (3 - 2 * e);
            }
            i += len;
        }

        _height = new double[n];
        double length = n * SegmentLength;
        double a1 = p.HillAmplitude * 11 / 16, a2 = p.HillAmplitude * 5 / 16;
        for (int s = 0; s < n; s++)
        {
            double z = s * SegmentLength;
            _height[s] = a1 * Math.Sin(2 * Math.PI * 11 * z / length)
                       + a2 * Math.Sin(2 * Math.PI * 29 * z / length + 1.3);
        }
        _heading = BuildHeading(_curve);
    }

    private Track(double[] curve, double[] height)
    {
        _curve = curve;
        _height = height;
        _heading = BuildHeading(curve);
    }

    /// <summary>Builds a track from explicit per-segment curvature (1/m) and optional height at each segment start (m).</summary>
    public static Track FromSegments(double[] curvature, double[]? height = null)
    {
        ArgumentNullException.ThrowIfNull(curvature);
        if (height != null && height.Length != curvature.Length)
            throw new ArgumentException("height must have the same length as curvature.", nameof(height));
        return new Track((double[])curvature.Clone(),
            height != null ? (double[])height.Clone() : new double[curvature.Length]);
    }

    private static double[] BuildHeading(double[] curve)
    {
        var heading = new double[curve.Length];
        double h = 0;
        for (int s = 0; s < curve.Length; s++)
        {
            heading[s] = h;
            h += curve[s] * SegmentLength;
        }
        return heading;
    }

    private int Count => _curve.Length;

    public double Length => Count * SegmentLength;

    /// <summary>Maps any distance, including negative, into [0, Length).</summary>
    public double Wrap(double d)
    {
        d %= Length;
        if (d < 0) d += Length;
        return d >= Length ? 0 : d;   // guards tiny negatives rounding up to Length
    }

    private int Index(double d) => Math.Min(Count - 1, (int)(Wrap(d) / SegmentLength));

    /// <summary>Curvature in 1/m; positive bends right.</summary>
    public double CurvatureAt(double d) => _curve[Index(d)];

    public double HeightAt(double d)
    {
        double u = Wrap(d) / SegmentLength;
        int i = Math.Min(Count - 1, (int)u);
        double a = _height[i], b = _height[(i + 1) % Count];
        return a + (b - a) * (u - i);
    }

    /// <summary>Height derivative (m per m) along the road.</summary>
    public double SlopeAt(double d)
    {
        int i = Index(d);
        return (_height[(i + 1) % Count] - _height[i]) / SegmentLength;
    }

    /// <summary>Road heading in radians, integrating curvature exactly over distance.</summary>
    public double HeadingAt(double d)
    {
        int i = Index(d);
        double f = Wrap(d) / SegmentLength - i;
        return _heading[i] + _curve[i] * SegmentLength * f;
    }
}
