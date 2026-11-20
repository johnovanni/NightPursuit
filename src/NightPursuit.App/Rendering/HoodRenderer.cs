using System.Windows;
using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>The bonnet outline, the two gun barrels and the aim mark at the screen centre.</summary>
internal static class HoodRenderer
{
    public static void Draw(DrawingContext dc, HudLayout l, Projector p)
    {
        double top = l.HoodTop, hw = l.HalfWidth, k = l.K;
        var hood = new StreamGeometry();
        using (var c = hood.Open())
        {
            c.BeginFigure(new Point(-5, l.Height + 5), true, true);
            c.PolyLineTo(new[]
            {
                new Point(-5, top + 46 * k), new Point(l.Width * 0.2, top + 10 * k), new Point(hw, top),
                new Point(l.Width * 0.8, top + 10 * k), new Point(l.Width + 5, top + 46 * k), new Point(l.Width + 5, l.Height + 5),
            }, true, false);
        }
        hood.Freeze();
        dc.DrawGeometry(Neon.Black, null, hood);   // the fill hides the road; only the top edge is stroked, below
        LineArt.Draw(dc, LineArt.Polyline(new[]
        {
            new Point(-5, top + 46 * k), new Point(l.Width * 0.2, top + 10 * k), new Point(hw, top),
            new Point(l.Width * 0.8, top + 10 * k), new Point(l.Width + 5, top + 46 * k),
        }, (0.56, 0.6)), LineArt.Outline(Neon.Green, 1));

        var guns = new LineBatch();
        foreach (int side in Projector.Sides)
            guns.Line(new Point(hw + side * 95 * k, top + 6 * k), new Point(hw + side * 78 * k, top - 12 * k));
        guns.Draw(dc, LineArt.Outline(Neon.Green, 1));

        // Aim mark: where the tracers will be 60 m ahead. The nose is always at the screen centre.
        double aimY = l.Height * 0.44 - (p.View.At(60).Y - Projector.CamHeight + 0.6) * hw * Projector.Focal / 60;
        var aim = new Point(hw, aimY);
        var cross = new LineBatch();
        cross.Line(aim + new Vector(-14 * k, 0), aim + new Vector(-5 * k, 0));
        cross.Line(aim + new Vector(5 * k, 0), aim + new Vector(14 * k, 0));
        cross.Line(aim + new Vector(0, -10 * k), aim + new Vector(0, -4 * k));
        cross.Draw(dc, LineArt.Outline(Neon.Green, 1));
    }
}
