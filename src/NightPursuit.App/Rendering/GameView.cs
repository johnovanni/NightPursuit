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

    /// <summary>Whether the start-of-game controls help is on screen (shown until the first gas; the game loop sets it from the flow each frame).</summary>
    public bool ShowHelp { get; set; } = true;

    /// <summary>Whether the world shakes with the car (the screen-shake setting; the game loop sets it each frame).</summary>
    public bool ShakeEnabled { get; set; } = true;

    /// <summary>The text-size setting's scale (1, 1.25 or 1.5): the HUD's text and the boxes that hold it grow with it (the game loop sets it each frame).</summary>
    public double TextScale { get; set; } = 1;

    /// <summary>A one-line notice from the screen flow (such as a failed save) shown in play, or null.</summary>
    public string? Notice { get; set; }

    /// <summary>Sound is switched off (the game loop sets it from the settings): a small SOUND OFF mark shows in play.</summary>
    public bool SoundOff { get; set; }

    /// <summary>A screen covers the road (the game loop sets it): the frozen world's text overlays are left out, so no stale text shows through: the warnings, messages, announcement, busted countdown and respawn box.</summary>
    public bool Covered { get; set; }

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
        _layout.TextScale = TextScale;

        double shake = ShakeEnabled ? world.Player.Shake * 14 : 0;
        if (ShakeEnabled) dc.PushTransform(new TranslateTransform(Math.Sin(world.Time * 61) * shake, Math.Cos(world.Time * 53) * shake));
        SkyRenderer.Draw(dc, _projector, world.Track, Hud.ReadoutBounds(_layout));
        RoadRenderer.Draw(dc, _projector);
        HazardRenderer.DrawSlicks(dc, _projector, world);
        RoadsideRenderer.DrawMarkings(dc, _projector, world);   // flat GAP chevrons: before the vehicles; barriers and signs are sorted into DrawAll
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
        if (ShakeEnabled) dc.Pop();

        // Screen-fixed from here on.
        HoodRenderer.Draw(dc, _layout, _projector);
        EffectRenderer.DrawMuzzleFlash(dc, _layout, world, Effects);
        WindshieldRenderer.Draw(dc, _layout, world.Player);   // the glass is between you and the hood; the HUD sits on top
        Hud.Draw(dc, _layout, run);
        WeaponPanel.Draw(dc, _layout, world);
        Mirror.Draw(dc, _layout, world, _projector);
        Radar.Draw(dc, _layout, world, _projector);
        BossBar.Draw(dc, _layout, run);
        bool bossBar = BossBar.Showing(run) is not null;
        if (!Covered)
        {
            Overlays.DrawWarnings(dc, _layout, world, bossBar);
            Overlays.DrawMessages(dc, _layout, world, bossBar);
            Overlays.DrawAnnouncement(dc, _layout, world, bossBar);
            Overlays.DrawBusted(dc, _layout, world);   // after the banner, so nothing covers the countdown
        }
        Overlays.DrawDamageAndOffRoad(dc, _layout, run);
        if (ShowHelp) Overlays.DrawHelp(dc, _layout);
        if (!Covered) Overlays.DrawState(dc, _layout, run);   // after the help, so the respawn countdown is never hidden under it
        if (SoundOff && !Covered && !ShowHelp) Hud.DrawSoundOff(dc, _layout);
        if (Notice is not null) Overlays.DrawNotice(dc, _layout, Notice);

#if DEBUG
        double now = _frameClock.Elapsed.TotalSeconds, frame = now - _lastFrame;
        _lastFrame = now;
        if (frame > 0 && frame < 1) _fps = _fps * 0.95 + 0.05 / frame;
        _layout.Text(dc, $"{_fps:0} FPS", 16 * _layout.K, h - 24 * _layout.K, 13, Neon.TextGray, scaled: false);
#endif
    }
}
