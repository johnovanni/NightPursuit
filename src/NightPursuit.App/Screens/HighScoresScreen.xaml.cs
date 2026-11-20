using System.Windows;
using System.Windows.Controls;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Screens;

/// <summary>The high-score table. The entry just added ends with <c>NEW</c> as well as being cyan (NFR-3).</summary>
public partial class HighScoresScreen : UserControl
{
    public HighScoresScreen() => InitializeComponent();

    public void Update(ScreenFlow flow)
    {
        RowsPanel.Children.Clear();
        var entries = flow.Scores.Entries;
        if (entries.Count == 0) AddRow("NO SCORES YET", "TextWhite", bold: false);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool isNew = flow.NewRank == i + 1;
            string text = $"{i + 1,2}. {e.Initials}   {e.Score:00000}   " + (e.Finished ? "ALL 6" : $"MISSION {e.MissionReached}");
            if (isNew) text += "  NEW";
            AddRow(text, isNew ? "TextCyan" : "TextWhite", isNew);
        }
        NoticeText.Text = flow.Notice ?? "";
        NoticeText.Visibility = flow.Notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void AddRow(string text, string brush, bool bold)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 2, 0, 2) };
        if (bold) t.FontWeight = FontWeights.Bold;
        t.SetResourceReference(TextBlock.FontSizeProperty, "TextHeading");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        RowsPanel.Children.Add(t);
    }
}
