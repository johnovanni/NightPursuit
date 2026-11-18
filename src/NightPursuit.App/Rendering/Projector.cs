using System.Windows;
using NightPursuit.Core.Road;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Turns road coordinates into screen points for one frame (70 degree field of view, eye 1.15 m above the road),
/// and holds the per-frame hiding line that the road renderer fills and later renderers read.
/// The frame is drawn at the interpolated moment (<see cref="World.Alpha"/> of the way from the previous fixed step
/// to the current one), so motion stays even at any refresh rate. Every renderer asks this class for the camera and for
/// interpolated positions instead of reading <c>Player.Distance</c> and friends.
/// </summary>
public sealed class Projector
{
    public const double CamHeight = 1.15;
    public static readonly double Focal = 1 / Math.Tan(35 * Math.PI / 180);

    /// <summary>Left and right (-1, 1), shared so per-frame loops over the two sides allocate nothing.</summary>
    public static readonly int[] Sides = { -1, 1 };

    double _alpha;
    double _firstD;
    double[] _clip = new double[320];
    int _clipCount;

    /// <summary>The road as seen from the interpolated camera; rebuilt every frame by <see cref="Begin"/>.</summary>
    public RoadView View { get; } = new();

    /// <summary>The interpolated camera: along the road, across it, and nose angle relative to the road.</summary>
    public double CamDistance { get; private set; }
    public double CamLateral { get; private set; }
    public double CamHeading { get; private set; }

    public double Width { get; private set; }
    public double Height { get; private set; }
    public double HalfWidth { get; private set; }
    public double Horizon { get; private set; }

    /// <summary>Screen pixels per radian of turning near the screen centre.</summary>
    public double PxPerRadian => HalfWidth * Focal;

    public void Begin(double width, double height, World w)
    {
        _alpha = w.Alpha;
        var player = w.Player;
        CamDistance = Lerp(player.PrevDistance, player.Distance);
        CamLateral = Lerp(player.PrevLateral, player.Lateral);
        CamHeading = Lerp(player.PrevHeading, player.Heading);
        View.Build(w.Track, CamDistance);
        Width = width;
        Height = height;
        HalfWidth = width / 2;
        Horizon = height * 0.44;
        _clipCount = 0;
    }

    /// <summary>The value <c>Alpha</c> of the way from <paramref name="prev"/> (start of the latest step) to <paramref name="cur"/>; works for angles too.</summary>
    public double Lerp(double prev, double cur) => prev + (cur - prev) * _alpha;

    /// <summary>Metres from the camera to a car along the road, at the interpolated moment.</summary>
    public double Ahead(Vehicle v) => Lerp(v.PrevDistance, v.Distance) - CamDistance;

    /// <summary>A car's interpolated lateral position.</summary>
    public double LateralOf(Vehicle v) => Lerp(v.PrevLateral, v.Lateral);

    /// <summary>A car's interpolated spin angle.</summary>
    public double SpinOf(Vehicle v) => Lerp(v.PrevSpinAngle, v.SpinAngle);

    /// <summary>Metres from the camera to a wreck's centre along the road.</summary>
    public double Ahead(Wreck w) => Lerp(w.PrevDistance, w.Distance) - CamDistance;

    /// <summary>Metres from the camera to a tracer's head along the road.</summary>
    public double Ahead(Tracer t) => Lerp(t.PrevDistance, t.Pose.Distance) - CamDistance;

    /// <summary>Pixels per metre at distance <paramref name="depth"/> ahead of the eye.</summary>
    public double Ppm(double depth) => HalfWidth * Focal / depth;

    /// <summary>
    /// Screen position of a point <paramref name="d"/> m along the road from the player, <paramref name="lateral"/> m right of
    /// the road centre and <paramref name="up"/> m above it. False when it is less than 0.8 m in front of the eye.
    /// </summary>
    public bool ToScreen(double d, double lateral, double up, out Point pt, out double depth)
    {
        RoadPoint r = View.At(d, lateral);
        // Rotate the road-frame offset into the car's frame: the camera turns with the car's nose.
        double x = r.X - CamLateral, f = r.F;
        double c = Math.Cos(CamHeading), s = Math.Sin(CamHeading);
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
