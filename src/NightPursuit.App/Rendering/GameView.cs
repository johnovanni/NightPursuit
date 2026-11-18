using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Missions;

namespace NightPursuit.App.Rendering;

/// <summary>
/// Draws one frame: the first-person vector world inside a shake transform, then everything that is fixed to
/// the screen (hood, readouts, mirror, radar, warnings, messages and overlays) on top of it.
/// </summary>
public sealed class GameView : FrameworkElement
{
    readonly Projector _projector = new();
    readonly HudLayout _layout = new();

#if DEBUG
    readonly Stopwatch _frameClock = Stopwatch.StartNew();
    double _lastFrame, _fps = 60;
#endif

    public MissionRun? Run { get; set; }

    /// <summary>Explosions and other presentation-only effects; the game loop feeds it after each update.</summary>
    public EffectList Effects { get; } = new();

    /// <summary>Whether the controls help is on screen: shown at the start until the first gas, and toggled by F1 (the game loop owns the logic).</summary>
    public bool ShowHelp { get; set; } = true;

    /// <summary>Milliseconds the latest <see cref="OnRender"/> spent recording its draw calls (read by the --perf diagnostic).</summary>
    public double LastRenderMilliseconds { get; private set; }

    /// <summary>How many times OnRender has run (the diagnostics check that every frame was really redrawn).</summary>
    public int RenderCount { get; private set; }

    protected override void OnRender(DrawingContext dc)
    {
        RenderCount++;
        long start = Stopwatch.GetTimestamp();
        Draw(dc);
        LastRenderMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    void Draw(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
        if (Run is null) return;

        var run = Run;
        var world = run.World;
        if (w < 50 || h < 50) return;   // too small to draw anything legible

        _projector.Begin(w, h, world);
        _layout.Begin(w, h, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        double shake = world.Player.Shake * 14;
        dc.PushTransform(new TranslateTransform(Math.Sin(world.Time * 61) * shake, Math.Cos(world.Time * 53) * shake));
        SkyRenderer.Draw(dc, _projector, world.Track, Hud.ReadoutBounds(_layout));
        RoadRenderer.Draw(dc, _projector);
        HazardRenderer.DrawSlicks(dc, _projector, world);
        ArsenalRenderer.DrawCaltrops(dc, _projector, world);
        ArsenalRenderer.DrawCrates(dc, _projector, world, _layout);
        VehicleRenderer.DrawAll(dc, _projector, world);
        ArsenalRenderer.DrawSmoke(dc, _projector, world);   // after the cars: a car inside it fades behind it
        HazardRenderer.DrawSmoke(dc, _projector, world);    // wreck smoke rises in front of the wrecks
        EffectRenderer.DrawTracers(dc, _projector, world);
        ArsenalRenderer.DrawMissiles(dc, _projector, world);
        ArsenalRenderer.DrawStrike(dc, _projector, world);
        EffectRenderer.DrawBeams(dc, _projector, Effects, Mirror.Bounds(_layout));
        EffectRenderer.DrawExplosions(dc, _projector, world, Effects);
        ArsenalRenderer.DrawLock(dc, _projector, world);
        dc.Pop();

        // Screen-fixed from here on.
        HoodRenderer.Draw(dc, _layout, _projector);
        EffectRenderer.DrawMuzzleFlash(dc, _layout, world, Effects);
        Hud.Draw(dc, _layout, run);
        Mirror.Draw(dc, _layout, world, _projector);
        Radar.Draw(dc, _layout, world, _projector);
        Overlays.DrawWarnings(dc, _layout, world);
        Overlays.DrawMessages(dc, _layout, world);
        Overlays.DrawAnnouncement(dc, _layout, world);
        Overlays.DrawDamageAndOffRoad(dc, _layout, run);
        if (ShowHelp) Overlays.DrawHelp(dc, _layout);
        Overlays.DrawState(dc, _layout, run);   // after the help, so PAUSED / GAME OVER etc. are never hidden under it

#if DEBUG
        double now = _frameClock.Elapsed.TotalSeconds, frame = now - _lastFrame;
        _lastFrame = now;
        if (frame > 0 && frame < 1) _fps = _fps * 0.95 + 0.05 / frame;
        _layout.Text(dc, $"{_fps:0} FPS", 16 * _layout.K, h - 24 * _layout.K, 13, Neon.TextGray);
#endif
    }
}
