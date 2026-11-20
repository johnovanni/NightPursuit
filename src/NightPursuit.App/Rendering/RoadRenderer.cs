using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Road;

namespace NightPursuit.App.Rendering;

/// <summary>The black ground, road edges, lane dashes, posts and lamps, and the hiding line for scenery behind hill crests.</summary>
public static class RoadRenderer
{
    const double DrawDistance = 1500;

    public static void Draw(DrawingContext dc, Projector p)
    {
        const double seg = Track.SegmentLength;
        double first = Math.Ceiling((p.CamDistance + 1.5) / seg) * seg;   // samples sit on world segment boundaries
        double firstD = first - p.CamDistance;
        int n = (int)(DrawDistance / seg);
        p.BeginClip(firstD, n + 1);

        // SPIKE: the trial styles fade the road in finer steps (see ArtBands).
        bool art = LineArt.Restyled;
        int bands = art ? ArtBands.Length : 3;
        var edges = new LineBatch[bands];
        var lanes = new LineBatch[bands];
        var postBands = new LineBatch[bands];
        var lampBands = new LineBatch[bands];
        for (int b = 0; b < bands; b++) { edges[b] = new(); lanes[b] = new(); postBands[b] = new(); lampBands[b] = new(); }

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
                int band = art ? ArtBand(d) : d < 250 ? 0 : d < 700 ? 1 : 2;
                var posts = postBands[art ? band : 0];
                var lamps = lampBands[art ? band : 0];
                Point From(int k) => prev[k] + (cur[k] - prev[k]) * t;

                edges[band].Line(From(0), cur[0]);
                edges[band].Line(From(3), cur[3]);
                long index = (long)Math.Round((p.CamDistance + d) / seg);
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

        // Ground: black from the terrain's top edge to the bottom of the screen, so a climbing road hides the mountains
        // behind it. maxY is the highest visible road sample (nothing nearer hides it); the horizon is the limit on flat land.
        double groundTop = Math.Min(p.Horizon, maxY);
        const double margin = 60;   // covers the shake offset
        dc.DrawRectangle(Neon.Black, null, new Rect(-margin, groundTop, p.Width + 2 * margin, Math.Max(0, p.Height + margin - groundTop)));

        if (!art)
        {
            postBands[0].Draw(dc, Neon.Post);
            lampBands[0].Draw(dc, Neon.Lamp);
            lanes[2].Draw(dc, Neon.LaneFar); lanes[1].Draw(dc, Neon.LaneMid); lanes[0].Draw(dc, Neon.LaneNear);
            edges[2].Draw(dc, Neon.RoadFar); edges[1].Draw(dc, Neon.RoadMid); edges[0].Draw(dc, Neon.RoadNear);
            return;
        }
        // Far to near, so the brighter near lines sit on top.
        for (int b = bands - 1; b >= 0; b--)
        {
            double fade = LineArt.Fade(ArtBandMiddle(b));
            postBands[b].Draw(dc, LineArt.Detail(Neon.Gray, fade * 0.8));
            lampBands[b].Draw(dc, LineArt.Detail(Neon.Amber, fade));
            lanes[b].Draw(dc, LineArt.Detail(Neon.Blue, fade));
            edges[b].Draw(dc, LineArt.Scenery(Neon.Cyan, fade));
        }
    }

    /// <summary>SPIKE: the far edge of each fade band, in metres ahead.</summary>
    static readonly double[] ArtBands = { 40, 90, 160, 260, 400, 600, 850, 1200, double.MaxValue };

    static int ArtBand(double d)
    {
        for (int b = 0; b < ArtBands.Length; b++) if (d < ArtBands[b]) return b;
        return ArtBands.Length - 1;
    }

    static double ArtBandMiddle(int b) => b == 0 ? 20 : b == ArtBands.Length - 1 ? 1350 : (ArtBands[b - 1] + ArtBands[b]) / 2;
}
