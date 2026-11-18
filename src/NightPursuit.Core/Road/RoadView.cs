namespace NightPursuit.Core.Road;

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

    public void Build(Track track, double distance)
    {
        double baseHeight = track.HeightAt(distance);
        Fill(track, distance, +1, baseHeight, _ahead);
        Fill(track, distance, -1, baseHeight, _behind);
    }

    // Heading is integrated outward from the build distance (never via Track.HeadingAt, which jumps at the loop seam).
    static void Fill(Track track, double d0, int dir, double baseHeight, Sample[] samples)
    {
        const double s = Track.SegmentLength;
        double x = 0, f = 0, h = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = new Sample(x, f, track.HeightAt(d0 + dir * i * s) - baseHeight, h);
            double k = track.CurvatureAt(d0 + dir * (i + 0.5) * s);
            double mid = h + dir * k * s / 2;          // heading halfway along this step
            // Chord of a constant-curvature arc: 2 sin(ks/2)/k, so a constant bend traces an exact circle.
            double chord = Math.Abs(k * s) < 1e-9 ? s : 2 * Math.Sin(k * s / 2) / k;
            x += dir * Math.Sin(mid) * chord;
            f += dir * Math.Cos(mid) * chord;
            h += dir * k * s;
        }
    }

    /// <summary>The point <paramref name="d"/> metres along the road (clamped to [-Behind, +Ahead]), <paramref name="lateral"/> metres right of its centre.</summary>
    public RoadPoint At(double d, double lateral = 0)
    {
        d = Math.Clamp(d, -Behind, Ahead);
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
        return new RoadPoint(x + lateral * Math.Cos(h), f - lateral * Math.Sin(h), y, h);
    }
}
