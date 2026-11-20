using System.Windows;
using System.Windows.Controls;
using NightPursuit.App.Rendering;

namespace NightPursuit.App.Screens;

/// <summary>The controls help screen: <see cref="Overlays.HelpLines"/> (the same lines as the intro box, without its "Press gas to start" line), then how to go back.</summary>
public partial class ControlsScreen : UserControl
{
    public ControlsScreen()
    {
        InitializeComponent();
        var lines = Overlays.HelpLines;
        // The last two lines are a blank and "Press gas to start", which belong to the intro box only.
        for (int i = 0; i < lines.Count - 2; i++)
        {
            var t = new TextBlock { Text = lines[i].Length == 0 ? " " : lines[i], TextWrapping = TextWrapping.NoWrap };
            if (i == 0)
            {
                t.FontWeight = FontWeights.Bold;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.SetResourceReference(TextBlock.FontSizeProperty, "TextHeading");
                t.SetResourceReference(TextBlock.ForegroundProperty, "TextAmber");
            }
            else
            {
                t.SetResourceReference(TextBlock.FontSizeProperty, "TextBody");
                t.SetResourceReference(TextBlock.ForegroundProperty, "TextWhite");
            }
            LinesPanel.Children.Add(t);
        }
    }
}
