using System.Windows;
using System.Windows.Media;

namespace NightPursuit.App.Rendering;

/// <summary>Collects many line segments into one geometry so a frame needs only a few draw calls.</summary>
internal sealed class LineBatch
{
    const double Far = 50_000;   // wildly off-screen points (road swinging past the camera) are skipped

    readonly StreamGeometry _geo = new();
    readonly StreamGeometryContext _ctx;
    bool _any;

    public LineBatch() => _ctx = _geo.Open();

    public void Line(Point a, Point b)
    {
        if (!(Math.Abs(a.X) < Far && Math.Abs(a.Y) < Far && Math.Abs(b.X) < Far && Math.Abs(b.Y) < Far)) return;
        _ctx.BeginFigure(a, false, false);
        _ctx.LineTo(b, true, false);
        _any = true;
    }

    public void Draw(DrawingContext dc, NeonPen pen)
    {
        _ctx.Close();
        if (!_any) return;
        _geo.Freeze();
        dc.DrawGeometry(null, pen.Glow, _geo);
        dc.DrawGeometry(null, pen.Core, _geo);
    }
}
