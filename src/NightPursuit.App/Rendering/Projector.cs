using System.Windows;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Turns road coordinates into screen points for one frame (70 degree field of view, eye 1.15 m above the road),
/// and holds the per-frame hiding line that the road renderer fills and later renderers read.
/// </summary>
public sealed class Projector
{
    public const double CamHeight = 1.15;
    public static readonly double Focal = 1 / Math.Tan(35 * Math.PI / 180);

    World? _world;
    double _firstD;
    double[] _clip = new double[320];
    int _clipCount;

    public double Width { get; private set; }
    public double Height { get; private set; }
    public double HalfWidth { get; private set; }
    public double Horizon { get; private set; }

    /// <summary>Screen pixels per radian of turning near the screen centre.</summary>
    public double PxPerRadian => HalfWidth * Focal;

    public void Begin(double width, double height, World w)
    {
        _world = w;
        Width = width;
        Height = height;
        HalfWidth = width / 2;
        Horizon = height * 0.44;
        _clipCount = 0;
    }

    /// <summary>Pixels per metre at distance <paramref name="depth"/> ahead of the eye.</summary>
    public double Ppm(double depth) => HalfWidth * Focal / depth;

    /// <summary>
    /// Screen position of a point <paramref name="d"/> m along the road from the player, <paramref name="lateral"/> m right of
    /// the road centre and <paramref name="up"/> m above it. False when it is less than 0.8 m in front of the eye.
    /// </summary>
    public bool ToScreen(double d, double lateral, double up, out Point pt, out double depth)
    {
        var player = _world!.Player;
        RoadPoint r = _world.View.At(d, lateral);
        // Rotate the road-frame offset into the car's frame: the camera turns with the car's nose.
        double x = r.X - player.Lateral, f = r.F;
        double c = Math.Cos(player.Heading), s = Math.Sin(player.Heading);
        double cx = x * c - f * s;
        depth = x * s + f * c;
        if (depth < 0.8) { pt = default; return false; }
        double scale = Ppm(depth);
        pt = new Point(HalfWidth + cx * scale, Horizon - (r.Y - CamHeight + up) * scale);
        return true;
    }

    /// <summary>Starts a new hiding line: entry i belongs to distance <paramref name="firstD"/> + i segments.</summary>
    public void BeginClip(double firstD, int count)
    {
        _firstD = firstD;
        if (_clip.Length < count) _clip = new double[count];
        _clipCount = count;
    }

    public void SetClip(int i, double y) => _clip[i] = y;

    /// <summary>Screen row below which the road at distance <paramref name="d"/> is hidden by a nearer hill (Height when nothing hides it).</summary>
    public double ClipAt(double d)
    {
        if (_clipCount == 0) return Height;
        int i = (int)Math.Ceiling((d - _firstD) / Track.SegmentLength);
        return _clip[Math.Clamp(i, 0, _clipCount - 1)];
    }
}
