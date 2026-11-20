using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NightPursuit.App.Rendering;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Screens;

/// <summary>
/// Shows the view for the flow's current screen over the game view: nothing in Play, a menu, the briefing, the controls, a result screen, the initials entry or the high scores.
/// Views are laid out in a 1280x720 design grid that scales with the window; inside it, a Viewbox makes too much text (the largest text size, a long notice) shrink instead of being cut off.
/// </summary>
public sealed class ScreenHost : ContentControl
{
    public const double DesignWidth = 1280, DesignHeight = 720;

    // The 100% sizes come from NeonStyles.xaml, the one place they are written.
    static readonly ResourceDictionary BaseStyles = new NeonStyles();

    readonly Grid _root = new();
    readonly Viewbox _fit = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(40) };
    readonly MenuScreen _menu = new();
    readonly BriefingScreen _briefing = new();
    readonly ControlsScreen _controls = new();
    readonly ResultScreen _result = new();
    readonly InitialsScreen _initials = new();
    readonly HighScoresScreen _highScores = new();

    Screen? _screen;
    int _version = -1;
    double _textScale = double.NaN;

    public ScreenHost()
    {
        Focusable = false;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        SetTextScale(1);

        var design = new Grid { Width = DesignWidth, Height = DesignHeight };
        design.Children.Add(_fit);
        _root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = design });
    }

    /// <summary>Swaps or refreshes the view when the flow's screen or version has changed (cheap to call every frame).</summary>
    public void Show(ScreenFlow flow)
    {
        SetTextScale(flow.Settings.TextScale);
        if (flow.Current == _screen && flow.Version == _version) return;
        _screen = flow.Current;
        _version = flow.Version;

        if (flow.Current == Screen.Play)
        {
            Content = null;
            return;
        }

        // Solid black where there is no road to show (or it would distract); elsewhere the frozen road shows faintly through.
        _root.Background = flow.Current is Screen.Title or Screen.Briefing or Screen.EnterInitials or Screen.HighScores ? Neon.Black : Neon.PanelFill;

        UIElement view;
        if (flow.Current is Screen.Title or Screen.Pause or Screen.Settings or Screen.QuitConfirm)
        {
            _menu.Update(flow);
            view = _menu;
        }
        else if (flow.Current == Screen.Briefing && flow.Game is not null)
        {
            _briefing.Update(flow);
            view = _briefing;
        }
        else if (flow.Current == Screen.Controls)
            view = _controls;
        else if (flow.Current is Screen.MissionComplete or Screen.GameOver or Screen.Finished)
        {
            _result.Update(flow);
            view = _result;
        }
        else if (flow.Current == Screen.EnterInitials)
        {
            _initials.Update(flow);
            view = _initials;
        }
        else
        {
            _highScores.Update(flow);
            view = _highScores;
        }
        if (!ReferenceEquals(_fit.Child, view)) _fit.Child = view;
        if (!ReferenceEquals(Content, _root)) Content = _root;
    }

    /// <summary>Sets the four text sizes, times <paramref name="scale"/>, as this host's own resources (the views use them as DynamicResource).</summary>
    void SetTextScale(double scale)
    {
        if (scale == _textScale) return;
        _textScale = scale;
        foreach (string key in NeonStyles.TextSizeKeys)
            Resources[key] = (double)BaseStyles[key] * scale;
    }
}
