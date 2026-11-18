using NightPursuit.Core.Road;

namespace NightPursuit.Core.Tests;

public class RoadGeometryTests
{
    [Fact] public void Straight_road_is_straight() { var v = new RoadView(); v.Build(Track.FromSegments(new double[400]), 0); var p = v.At(300, 2); Assert.Equal(2, p.X, 6); Assert.Equal(300, p.F, 6); }

    [Fact] public void Constant_bend_matches_circle()   // radius 200 m, bend right
    {
        var v = new RoadView(); v.Build(Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray()), 0);
        double d = 150, theta = d / 200;
        var p = v.At(d);
        Assert.Equal(200 * (1 - Math.Cos(theta)), p.X, 3);
        Assert.Equal(200 * Math.Sin(theta), p.F, 3);
        Assert.Equal(theta, p.Heading, 6);
    }

    [Fact] public void Bend_curves_same_side_behind() { var v = new RoadView(); v.Build(Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray()), 1000); Assert.True(v.At(-100).X > 0); Assert.True(v.At(-100).F < 0); }

    [Fact] public void Long_tight_bend_wraps_round()   // 300 m on radius 180 m turns 95 degrees: the road comes back toward the camera plane
    { var v = new RoadView(); v.Build(Track.FromSegments(Enumerable.Repeat(1 / 180.0, 400).ToArray()), 0); Assert.True(v.At(300).F < v.At(280).F); }

    [Fact] public void Straight_motion_leaves_a_bend_on_the_outside()
    {
        var t = Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray());
        var pose = new RoadPose(0, 0, 0);
        for (int i = 0; i < 100; i++) pose = RoadMotion.MoveStraight(t, pose, 1);
        // A tangent line 100 m long ends sqrt(200^2 + 100^2) - 200 = 23.6 m outside the road centre line (left, negative).
        Assert.InRange(pose.Lateral, -24.5, -22.5);
    }

    [Fact] public void MoveStraight_is_pure()
    {
        var t = Track.FromSegments(new double[400]);
        var pose = new RoadPose(10, 1, 0.1);
        var moved = RoadMotion.MoveStraight(t, pose, 5);
        Assert.Equal(new RoadPose(10, 1, 0.1), pose);
        Assert.NotEqual(pose, moved);
    }

    [Fact] public void At_clamps_outside_the_view()
    {
        var v = new RoadView(); v.Build(Track.FromSegments(new double[400]), 0);
        Assert.Equal(v.At(RoadView.Ahead).F, v.At(RoadView.Ahead + 500).F, 6);
        Assert.Equal(v.At(-RoadView.Behind).F, v.At(-RoadView.Behind - 500).F, 6);
    }

    [Fact] public void View_near_loop_end_continues_into_the_start()
    {
        var t = Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray());   // 2000 m loop
        var v = new RoadView(); v.Build(t, t.Length - 100);
        double theta = 150 / 200.0;
        var p = v.At(150);   // crosses the seam
        Assert.Equal(200 * (1 - Math.Cos(theta)), p.X, 3);
        Assert.Equal(200 * Math.Sin(theta), p.F, 3);
    }
}
