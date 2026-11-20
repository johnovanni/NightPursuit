using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Police roadblocks and speed traps. The flat road markings (the green GAP chevrons, parked and rolling) are painted before
/// the vehicles by <see cref="DrawMarkings"/>; the solid things (barriers, posts and signs) are drawn one by one by
/// <see cref="VehicleRenderer.DrawAll"/>, far to near with the cars and wrecks, so a farther car never paints over them (F13).
/// </summary>
internal static class RoadsideRenderer
{
    /// <summary>How far each is drawn, metres. A speed trap fades in from <see cref="TrapFadeFrom"/>; the GAP word shows within <see cref="GapWordDistance"/>.</summary>
    public const double RoadblockDistance = 450, TrapDistance = 400, TrapFadeFrom = 350, GapWordDistance = 400;

    const double BarrierHeight = 1.1;
    const int StripePairs = 5;
    const double PostHeight = 2.5, SignSize = 1.2;
    static readonly Typeface Font = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>Whether <paramref name="h"/> is a roadblock or speed trap in range, and where it sorts (its distance ahead).</summary>
    public static bool InRange(Projector p, Hazard h, out double ahead)
    {
        ahead = h.Distance - p.CamDistance;
        return h switch
        {
            Roadblock => ahead >= 2 && ahead <= RoadblockDistance,
            SpeedTrap => ahead >= 2 && ahead <= TrapDistance,
            _ => false,
        };
    }

    /// <summary>Draws one roadblock or speed trap picked by <see cref="InRange"/>.</summary>
    public static void Draw(DrawingContext dc, Projector p, Hazard h, double time)
    {
        if (h is Roadblock rb) DrawBarriers(dc, p, rb, time);
        else if (h is SpeedTrap trap) DrawSpeedTrap(dc, p, trap);
    }

    /// <summary>The green GAP chevrons on the road, painted before any vehicle: three at each parked roadblock, one at each formed rolling pair.</summary>
    public static void DrawMarkings(DrawingContext dc, Projector p, World w)
    {
        foreach (var h in w.Hazards)
        {
            if (h is not Roadblock rb) continue;
            double ahead = rb.Distance - p.CamDistance;
            if (ahead < 2 || ahead > RoadblockDistance) continue;
            GapChevron.Draw(dc, p, GapChevron.PlaceAt(ahead), Track.LaneCentres[rb.GapLane], count: 3, label: ahead <= GapWordDistance);
        }
        foreach (var rb in w.RollingRoadblocks)
        {
            if (!rb.Formed) continue;
            double nearest = double.NaN;   // the nearer SUV that is still a car; the chevron goes with them
            foreach (var suv in rb.Cars)
                if (suv.State != VehicleState.Wrecked) nearest = double.IsNaN(nearest) ? p.Ahead(suv) : Math.Min(nearest, p.Ahead(suv));
            if (!double.IsNaN(nearest)) GapChevron.Draw(dc, p, GapChevron.PlaceAt(nearest), Track.LaneCentres[rb.OpenLane]);
        }
    }

    // ---- roadblock ------------------------------------------------------------------------------------------

    static void DrawBarriers(DrawingContext dc, Projector p, Roadblock rb, double time)
    {
        double nearD = rb.Distance - p.CamDistance - Roadblock.Depth / 2, farD = nearD + Roadblock.Depth;
        double clip = p.ClipAt(nearD);
        if (clip <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        for (int lane = 0; lane < Track.LaneCentres.Length; lane++)
            if (lane != rb.GapLane) Barrier(dc, p, Track.LaneCentres[lane], nearD, farD, time);
        dc.Pop();
    }

    /// <summary>One barrier box: the face toward you striped red and white, the visible side, and a two-colour flashing light bar on top.</summary>
    static void Barrier(DrawingContext dc, Projector p, double centre, double nearD, double farD, double time)
    {
        double x0 = centre - Roadblock.BarrierHalfWidth, x1 = centre + Roadblock.BarrierHalfWidth;
        if (!p.ToScreen(nearD, x0, 0, out var bl, out double depth) || !p.ToScreen(nearD, x1, 0, out var br, out _)
            || !p.ToScreen(nearD, x0, BarrierHeight, out var tl, out _) || !p.ToScreen(nearD, x1, BarrierHeight, out var tr, out _)) return;

        // The side facing the camera, when it stands outside the box's width.
        if (p.CamLateral < x0 || p.CamLateral > x1)
        {
            bool left = p.CamLateral < x0;
            double sx = left ? x0 : x1;
            if (p.ToScreen(farD, sx, 0, out var fb, out _) && p.ToScreen(farD, sx, BarrierHeight, out var ft, out _))
            {
                var side = Polygon(left ? bl : br, fb, ft, left ? tl : tr);
                dc.DrawGeometry(Neon.Black, Neon.BarrierPen.Glow, side);
                dc.DrawGeometry(null, Neon.BarrierPen.Core, side);
            }
        }

        var face = Polygon(bl, br, tr, tl);
        dc.DrawGeometry(Neon.Black, null, face);

        // Diagonal red and white stripes across the face, clipped to it. (u, v) = across the width and up the height, 0..1.
        Point At(double u, double v) => bl + (br - bl) * u + (tl - bl) * v;
        dc.PushClip(face);
        const double pair = 1.0 / StripePairs, slant = 0.25;
        for (int i = -1; i < StripePairs; i++)
        {
            double u = i * pair;
            dc.DrawGeometry(Neon.StripeRed, null, Polygon(At(u, 0), At(u + pair / 2, 0), At(u + pair / 2 + slant, 1), At(u + slant, 1)));
            dc.DrawGeometry(Neon.StripeWhite, null, Polygon(At(u + pair / 2, 0), At(u + pair, 0), At(u + pair + slant, 1), At(u + pair / 2 + slant, 1)));
        }
        dc.Pop();
        dc.DrawGeometry(null, Neon.BarrierPen.Glow, face);
        dc.DrawGeometry(null, Neon.BarrierPen.Core, face);

        // The light bar on top: two halves swapping blue and red on the shared clock, at least 3 px tall at any distance.
        double s = p.Ppm(depth), lift = Math.Max(0.2, 3.0 / s);
        foreach (int half in Projector.Sides)
        {
            double a = centre + (half < 0 ? -1.0 : 0), b = centre + (half < 0 ? 0 : 1.0);
            if (!p.ToScreen(nearD, a, BarrierHeight, out var lo, out _) || !p.ToScreen(nearD, b, BarrierHeight + lift, out var hi, out _)) continue;
            var rect = new Rect(lo, hi);
            var pen = Neon.LightPen(time, half);
            dc.DrawRectangle(Neon.LightFill(time, half), pen.Glow, rect);
            dc.DrawRectangle(null, pen.Core, rect);
        }
    }

    // ---- speed trap -----------------------------------------------------------------------------------------

    /// <summary>A post 2.5 m tall at the road's edge with a square limit sign (amber; red once it caught you) and a small camera box.</summary>
    static void DrawSpeedTrap(DrawingContext dc, Projector p, SpeedTrap trap)
    {
        double dz = trap.Distance - p.CamDistance;
        double clip = p.ClipAt(dz);
        if (clip <= 0) return;
        double lat = trap.Lateral;
        double top = PostHeight, bottom = PostHeight - SignSize, half = SignSize / 2;
        if (!p.ToScreen(dz, lat, 0, out var foot, out double depth) || !p.ToScreen(dz, lat, top, out var tip, out _)
            || !p.ToScreen(dz, lat - half, bottom, out var lo, out _) || !p.ToScreen(dz, lat + half, top, out var hi, out _)) return;
        double s = p.Ppm(depth);
        double fade = Math.Clamp((TrapDistance - dz) / (TrapDistance - TrapFadeFrom), 0, 1);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, p.Width, clip)));
        dc.PushOpacity(fade);
        dc.DrawLine(Neon.Post.Core, foot, tip);

        var sign = new Rect(lo, hi);
        var pen = trap.Caught ? Neon.SignCaughtPen : Neon.SignPen;
        dc.DrawRectangle(trap.Caught ? Neon.SignCaughtFill : Neon.SignFill, pen.Glow, sign);
        dc.DrawRectangle(null, pen.Core, sign);
        double size = Math.Max(6, 0.6 * s);
        var text = new FormattedText(trap.LimitMph.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                     FlowDirection.LeftToRight, Font, size, trap.Caught ? Neon.TextRed : Neon.TextAmber, 1.0);
        dc.DrawText(text, new Point(sign.X + (sign.Width - text.Width) / 2, sign.Y + (sign.Height - text.Height) / 2));

        // The camera box: on the road side of the post, a lens facing you, joined to the sign by a short arm.
        double toward = lat > 0 ? -1 : 1, camLo = lat + toward * (half + 0.8), camHi = lat + toward * (half + 0.15);
        if (p.ToScreen(dz, Math.Min(camLo, camHi), top - 0.5, out var cl, out _) && p.ToScreen(dz, Math.Max(camLo, camHi), top - 0.1, out var ch, out _))
        {
            var box = new Rect(cl, ch);
            double mid = box.Y + box.Height / 2, lens = Math.Max(1, box.Height * 0.25);
            dc.DrawRectangle(Neon.Black, Neon.SignPen.Core, box);
            dc.DrawEllipse(Neon.TextAmber, null, new Point(box.X + box.Width / 2, mid), lens, lens);
            dc.DrawLine(Neon.Post.Core, new Point(toward < 0 ? sign.X : sign.Right, mid), new Point(toward < 0 ? box.Right : box.X, mid));
        }
        dc.Pop();
        dc.Pop();
    }

    static StreamGeometry Polygon(Point a, Point b, Point c, Point d)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, true, true);
            ctx.LineTo(b, true, false);
            ctx.LineTo(c, true, false);
            ctx.LineTo(d, true, false);
        }
        g.Freeze();
        return g;
    }
}
