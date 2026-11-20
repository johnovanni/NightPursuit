using System.Windows;
using System.Windows.Controls;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Screens;

/// <summary>
/// Draws a <see cref="MenuModel"/>: its title, its lines, the items and the flow's notice. The selected item is marked
/// with <c>&gt;</c> (and its value with <c>&lt; &gt;</c>) as well as by colour (NFR-3).
/// </summary>
public partial class MenuScreen : UserControl
{
    const string Gap = "   ";

    public MenuScreen() => InitializeComponent();

    public void Update(ScreenFlow flow)
    {
        var menu = flow.Menu;
        if (menu is null) return;
        TitleText.Text = menu.Title;

        LinesPanel.Children.Clear();
        foreach (var line in menu.Lines)
            LinesPanel.Children.Add(Line(line, "TextBody", "TextGray", centred: true));

        // Consolas is monospaced: padding every label to the longest lines the values up in one column,
        // and "< " on the selected row takes the same two places as the "  " on the others.
        int labelWidth = menu.Items.Max(i => i.Label.Length);
        ItemsPanel.Children.Clear();
        for (int i = 0; i < menu.Items.Count; i++)
        {
            MenuEntry item = menu.Items[i];
            bool selected = i == menu.Selected;
            string text = (selected ? "> " : "  ") + item.Label;
            if (item.Value is not null)
                text = text.PadRight(2 + labelWidth) + Gap + (selected ? "< " + item.Value + " >" : "  " + item.Value);
            var row = Line(text, "TextHeading", selected ? "TextCyan" : "TextWhite", centred: false);
            if (selected) row.FontWeight = FontWeights.Bold;
            row.Margin = new Thickness(0, 4, 0, 4);
            ItemsPanel.Children.Add(row);
        }

        NoticeText.Text = flow.Notice ?? "";
        NoticeText.Visibility = flow.Notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    static TextBlock Line(string text, string size, string brush, bool centred)
    {
        var t = new TextBlock
        {
            Text = text,
            HorizontalAlignment = centred ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            TextAlignment = centred ? TextAlignment.Center : TextAlignment.Left,
            // A long line wraps; an item row never does (nor is it capped), so too wide a menu shrinks to fit instead.
            TextWrapping = centred ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = centred ? 1100 : double.PositiveInfinity,
        };
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
