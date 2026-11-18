using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>Road edges, lane dashes, posts and lamps, and the hiding line for scenery behind hill crests.</summary>
public static class RoadRenderer
{
    const double DrawDistance = 1500;

    public static void Draw(DrawingContext dc, Projector p, World world)
    {
        var player = world.Player;
        const double seg = Track.SegmentLength;
        double first = Math.Ceiling((player.Distance + 1.5) / seg) * seg;   // samples sit on world segment boundaries
        double firstD = first - player.Distance;
        int n = (int)(DrawDistance / seg);
        p.BeginClip(firstD, n + 1);

        LineBatch[] edges = { new(), new(), new() };
        LineBatch[] lanes = { new(), new(), new() };
        var posts = new LineBatch();
        var lamps = new LineBatch();

        double[] lat = { -Track.RoadHalfWidth, -Track.LaneLine, Track.LaneLine, Track.RoadHalfWidth };
        var prev = new Point[4];
        var cur = new Point[4];
        double maxY = p.Height, prevY = 0;
        bool prevOk = false;

        for (int i = 0; i <= n; i++)
        {
            double d = firstD + i * seg;
            p.SetClip(i, maxY);
            bool ok = p.ToScreen(d, 0, 0, out var centre, out double depth);
            for (int k = 0; k < 4 && ok; k++) ok = p.ToScreen(d, lat[k], 0, out cur[k], out _);
            double sy = centre.Y;

            if (ok && prevOk && sy < maxY)
            {
                // Coming out from behind a crest: start the line where it emerges.
                double t = prevY > maxY ? (maxY - prevY) / (sy - prevY) : 0;
                int band = d < 250 ? 0 : d < 700 ? 1 : 2;
                Point From(int k) => prev[k] + (cur[k] - prev[k]) * t;

                edges[band].Line(From(0), cur[0]);
                edges[band].Line(From(3), cur[3]);
                long index = (long)Math.Round((player.Distance + d) / seg);
                if (index % 4 < 2)
                {
                    lanes[band].Line(From(1), cur[1]);
                    lanes[band].Line(From(2), cur[2]);
                }
                if (index % 8 == 0 && d < 900)
                {
                    foreach (int side in Projector.Sides)
                    {
                        double px = side * (Track.RoadHalfWidth + 2.2);
                        if (!p.ToScreen(d, px, 0, out var foot, out _)) continue;
                        p.ToScreen(d, px, 1.1, out var top, out _);
                        posts.Line(foot, top);
                        if (index % 32 == 0 && p.ToScreen(d, px, 5.5, out var lamp, out _)
                                            && p.ToScreen(d, px - side * 1.2, 5.6, out var arm, out _))
                        {
                            posts.Line(foot, lamp);
                            lamps.Line(lamp, arm);
                        }
                    }
                }
            }
            if (ok)
            {
                // Only road still heading away from us can hide what lies beyond it. Where a tight bend
                // swings the road sideways it projects far off-screen and must not black out the rest.
                bool headingAway = depth >= 0.3 * d;
                if (headingAway && (i == 0 || (prevOk && sy < maxY))) maxY = Math.Max(0, Math.Min(maxY, sy));
                (prev, cur) = (cur, prev);
                prevY = sy;
            }
            prevOk = ok;
        }

        posts.Draw(dc, Neon.Post);
        lamps.Draw(dc, Neon.Lamp);
        lanes[2].Draw(dc, Neon.LaneFar); lanes[1].Draw(dc, Neon.LaneMid); lanes[0].Draw(dc, Neon.LaneNear);
        edges[2].Draw(dc, Neon.RoadFar); edges[1].Draw(dc, Neon.RoadMid); edges[0].Draw(dc, Neon.RoadNear);
    }
}
