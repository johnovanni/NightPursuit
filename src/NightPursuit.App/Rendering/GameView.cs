using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NightPursuit.Core.Common;
using NightPursuit.Core.Missions;

namespace NightPursuit.App.Rendering;

/// <summary>Stub view: black plus the speed readout. Real rendering arrives in later tasks.</summary>
public sealed class GameView : FrameworkElement
{
    static readonly Brush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x00)));
    static readonly Typeface Face = new("Consolas");

    readonly Projector _projector = new();

    public MissionRun? Run { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
        if (Run is null) return;

        var world = Run.World;
        if (w >= 50 && h >= 50)
        {
            _projector.Begin(w, h, world);
            double shake = world.Player.Shake * 14;
            dc.PushTransform(new TranslateTransform(Math.Sin(world.Time * 61) * shake, Math.Cos(world.Time * 53) * shake));
            SkyRenderer.Draw(dc, _projector, world);
            RoadRenderer.Draw(dc, _projector, world);
            // Later tasks: vehicles, hazards, tracers and effects here, then the HUD outside the shake.
            dc.Pop();
        }

        double mph = Units.MsToMph(Run.World.Player.Speed);
        var text = new FormattedText($"{mph:0} MPH", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Face, 32, Amber, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((w - text.Width) / 2, h - text.Height - 16));
    }

    static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
