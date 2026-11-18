using NightPursuit.Core.Road;

namespace NightPursuit.Core.Tests;

public class TrackTests
{
    [Fact] public void Same_seed_same_track() { var a = new Track(new TrackProfile(3)); var b = new Track(new TrackProfile(3)); for (double d = 0; d < a.Length; d += 37) Assert.Equal(a.CurvatureAt(d), b.CurvatureAt(d)); }
    [Fact] public void Radii_stay_within_profile() { var t = new Track(new TrackProfile(5)); for (double d = 0; d < t.Length; d += 5) { double k = Math.Abs(t.CurvatureAt(d)); Assert.True(k == 0 || 1 / k >= 180 - 1e-9); } }
    [Fact] public void Start_and_end_are_straight() { var t = new Track(new TrackProfile(5)); Assert.Equal(0, t.CurvatureAt(10)); Assert.Equal(0, t.CurvatureAt(t.Length - 10)); }
    [Fact] public void Wrap_handles_negative() { var t = Track.FromSegments(new double[100]); Assert.Equal(495, t.Wrap(-5), 9); }
    [Fact] public void Heading_integrates_curvature() { var k = Enumerable.Repeat(0.01, 100).ToArray(); var t = Track.FromSegments(k); Assert.Equal(0.01 * 50, t.HeadingAt(50), 9); }

    [Fact] public void FromSegments_rejects_mismatched_height() => Assert.Throws<ArgumentException>(() => Track.FromSegments(new double[10], new double[9]));

    [Fact] public void Slope_is_height_derivative() { var h = new double[] { 0, 5, 5, 0 }; var t = Track.FromSegments(new double[4], h); Assert.Equal(1.0, t.SlopeAt(2), 9); Assert.Equal(2.5, t.HeightAt(2.5), 9); Assert.Equal(-1.0, t.SlopeAt(12), 9); }

    [Fact] public void Hills_reach_default_amplitude() { var t = new Track(new TrackProfile(1)); double max = 0; for (double d = 0; d < t.Length; d += 5) max = Math.Max(max, Math.Abs(t.HeightAt(d))); Assert.InRange(max, 10, 16); }

    [Fact] public void Different_seeds_differ() { var a = new Track(new TrackProfile(1)); var b = new Track(new TrackProfile(2)); bool differ = false; for (double d = 0; d < a.Length; d += 5) differ |= a.CurvatureAt(d) != b.CurvatureAt(d); Assert.True(differ); }
}
