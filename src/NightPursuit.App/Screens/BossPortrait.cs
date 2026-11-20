using System.Windows;
using System.Windows.Media;
using NightPursuit.App.Rendering;
using NightPursuit.Core.Missions;

namespace NightPursuit.App.Screens;

/// <summary>
/// A boss seen from behind, about 300x180, for the briefing: the same outline tables (<see cref="BossShapes"/>) and the same
/// line-art pens as the boss in play, in the boss colour. Open outlines with gaps and no line along the base. What shows the
/// look is the shape (two fins, four fins, a raised cabin), not the colour.
/// </summary>
public sealed class BossPortrait : FrameworkElement
{
    public const double PortraitWidth = 300, PortraitHeight = 180;

    const double Scale = 95;        // pixels per metre
    const double HalfWidthM = 1.2;  // half of the boss's 2.4 m width
    const double Ground = 165;      // the road's y in the picture

    BossLook _look;
    string _name = "";

    public BossPortrait()
    {
        Width = PortraitWidth;
        Height = PortraitHeight;
        Focusable = false;
        IsHitTestVisible = false;
    }

    public BossLook Look
    {
        get => _look;
        set { if (_look == value) return; _look = value; InvalidateVisual(); }
    }

    /// <summary>The boss's name, for screen readers (the screen prints it beside the picture).</summary>
    public new string Name
    {
        get => _name;
        set
        {
            _name = value;
            System.Windows.Automation.AutomationProperties.SetName(this, "Portrait of " + value);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double cx = PortraitWidth / 2, halfW = HalfWidthM * Scale;
        Point P(double x, double y) => new(cx + x * halfW, Ground - y * Scale);

        var colour = LineArt.ColorOf(Neon.BossCar);
        var outline = LineArt.Outline(colour, 1);
        var detail = LineArt.Detail(colour, 1);

        // The tyres: only the outer side, as on the boss in play.
        double wheel = 0.28 * Scale, ww = 0.3 * Scale;
        var tyres = new LineBatch();
        foreach (int side in Projector.Sides)
        {
            double outer = cx + side * (halfW - ww / 2) + side * ww / 2;
            tyres.Line(new Point(outer, Ground - 0.224 * Scale), new Point(outer, Ground - 0.224 * Scale + wheel));
        }

        dc.DrawGeometry(Neon.Black, null, BossShapes.PiecesFill(_look, P));
        foreach (var line in BossShapes.PieceLines(_look, P)) LineArt.Draw(dc, line, outline);
        dc.DrawGeometry(Neon.Black, null, BossShapes.BodyFill(_look, P));

        var edge = BossShapes.Edge(_look, P);
        double total = 0, toShoulder = 0;
        int half = edge.Count / 2;
        for (int i = 1; i < edge.Count; i++)
        {
            total += (edge[i] - edge[i - 1]).Length;
            if (i == half) toShoulder = total;
        }
        double sh = total > 0 ? toShoulder / total : 0.6;
        LineArt.Draw(dc, LineArt.Polyline(edge, (0.1, 0.14), (sh + 0.02, sh + 0.07)), outline);
        tyres.Draw(dc, detail);

        // The gold-trimmed tail strip (as wide as the body is) and the exhaust ring.
        double stripHalf = 0.94 * BossShapes.Widening(_look);
        var strip = new Rect(P(-stripHalf, 0.66), P(stripHalf, 0.5));
        dc.DrawRectangle(Neon.TailLight, null, strip);
        LineArt.Draw(dc, new RectangleGeometry(strip), LineArt.Detail(LineArt.ColorOf(Neon.BossTrim), 1));
        LineArt.Draw(dc, new EllipseGeometry(P(0, 0.33), 0.15 * Scale, 0.1 * Scale), detail);
    }
}
