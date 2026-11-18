namespace ControlsLab;

/// <summary>A looping procedural road: curvature and height per 5 m segment.</summary>
public sealed class Track
{
    public const double SegmentLength = 5.0;
    public const double RoadHalfWidth = 7.5;   // three 5 m lanes
    public const double LaneLine = 2.5;
    const int Segments = 4000;                 // 20 km loop

    readonly double[] _curve = new double[Segments];   // 1/m, positive bends right
    readonly double[] _height = new double[Segments];  // m at segment start
    readonly double[] _heading = new double[Segments]; // radians at segment start

    public Track(int seed = 7)
    {
        var rng = new Random(seed);
        int i = 60;
        while (i < Segments - 120)
        {
            i += rng.Next(20, 90);
            int len = rng.Next(50, 160);
            double radius = rng.Next(180, 700);
            double k = (rng.Next(2) == 0 ? -1 : 1) / radius;
            for (int j = 0; j < len && i + j < Segments - 60; j++)
            {
                double t = (j + 0.5) / len;
                double e = Math.Min(1, Math.Min(t, 1 - t) / 0.25);
                _curve[i + j] = k * e * e * (3 - 2 * e);
            }
            i += len;
        }

        double length = Length, heading = 0;
        for (int s = 0; s < Segments; s++)
        {
            double z = s * SegmentLength;
            _height[s] = 11 * Math.Sin(2 * Math.PI * 11 * z / length) + 5 * Math.Sin(2 * Math.PI * 29 * z / length + 1.3);
            _heading[s] = heading;
            heading += _curve[s] * SegmentLength;
        }
    }

    public double Length => Segments * SegmentLength;

    public double Wrap(double z)
    {
        z %= Length;
        return z < 0 ? z + Length : z;
    }

    int Index(double z) => Math.Min(Segments - 1, (int)(Wrap(z) / SegmentLength));

    public double CurvatureAt(double z) => _curve[Index(z)];

    public double HeightAt(double z)
    {
        double u = Wrap(z) / SegmentLength;
        int i = Math.Min(Segments - 1, (int)u);
        double a = _height[i], b = _height[(i + 1) % Segments];
        return a + (b - a) * (u - i);
    }

    public double SlopeAt(double z)
    {
        int i = Index(z);
        return (_height[(i + 1) % Segments] - _height[i]) / SegmentLength;
    }

    public double HeadingAt(double z)
    {
        int i = Index(z);
        double f = Wrap(z) / SegmentLength - i;
        return _heading[i] + _curve[i] * SegmentLength * f;
    }
}

/// <summary>A point on or beside the road in the player's frame: X right, F forward (depth), Y up, metres.</summary>
public readonly record struct RoadPoint(double X, double F, double Y);

/// <summary>
/// The road as seen from the player each frame. Walks the road ahead (d &gt; 0) and behind (d &lt; 0)
/// from the player's position, integrating the heading exactly, so long tight bends wrap round
/// properly. The frame is aligned with the road direction at the player.
/// </summary>
public sealed class RoadView
{
    public const double Ahead = 1520, Behind = 320;
    readonly Sample[] _ahead = new Sample[(int)(Ahead / Track.SegmentLength) + 2];
    readonly Sample[] _behind = new Sample[(int)(Behind / Track.SegmentLength) + 2];

    record struct Sample(double X, double F, double Y, double Heading);

    public void Build(Track track, double playerZ)
    {
        double baseHeight = track.HeightAt(playerZ);
        Fill(track, playerZ, +1, baseHeight, _ahead);
        Fill(track, playerZ, -1, baseHeight, _behind);
    }

    static void Fill(Track track, double z0, int dir, double baseHeight, Sample[] samples)
    {
        const double s = Track.SegmentLength;
        double x = 0, f = 0, h = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = new Sample(x, f, track.HeightAt(z0 + dir * i * s) - baseHeight, h);
            double k = track.CurvatureAt(z0 + dir * (i + 0.5) * s);
            double mid = h + dir * k * s / 2;          // heading halfway along this step
            x += dir * Math.Sin(mid) * s;
            f += dir * Math.Cos(mid) * s;
            h += dir * k * s;
        }
    }

    /// <summary>The point <paramref name="d"/> metres along the road, <paramref name="lateral"/> metres right of its centre.</summary>
    public RoadPoint At(double d, double lateral = 0)
    {
        var arr = d >= 0 ? _ahead : _behind;
        double u = Math.Abs(d) / Track.SegmentLength;
        int i = (int)u;
        Sample a, b;
        double t;
        if (i >= arr.Length - 1) { a = b = arr[^1]; t = 0; }
        else { a = arr[i]; b = arr[i + 1]; t = u - i; }
        double x = a.X + (b.X - a.X) * t, f = a.F + (b.F - a.F) * t;
        double y = a.Y + (b.Y - a.Y) * t, h = a.Heading + (b.Heading - a.Heading) * t;
        // Right of the road direction (sin h, cos h) is (cos h, -sin h).
        return new RoadPoint(x + lateral * Math.Cos(h), f - lateral * Math.Sin(h), y);
    }
}
