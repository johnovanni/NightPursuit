using System.Windows;
using System.Windows.Controls;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Screens;

/// <summary>The three letters of a new high score. The cursor letter is <c>[J]</c> (brackets), underlined and cyan; the others are padded <c> M </c> so the row does not shift (NFR-3).</summary>
public partial class InitialsScreen : UserControl
{
    public InitialsScreen() => InitializeComponent();

    public void Update(ScreenFlow flow)
    {
        var e = flow.Initials;
        var g = flow.Game;
        if (e is null || g is null) return;
        RankText.Text = $"NEW HIGH SCORE  -  RANK {flow.Scores.RankFor(g.Score)}";
        ScoreText.Text = $"{g.Score:00000}";

        LettersPanel.Children.Clear();
        string text = e.Text;
        for (int i = 0; i < 3; i++)
        {
            bool cursor = i == e.Cursor;
            var t = new TextBlock { Text = cursor ? $"[{text[i]}]" : $" {text[i]} ", FontWeight = FontWeights.Bold };
            if (cursor) t.TextDecorations = TextDecorations.Underline;
            t.SetResourceReference(TextBlock.FontSizeProperty, "TextTitle");
            t.SetResourceReference(TextBlock.ForegroundProperty, cursor ? "TextCyan" : "TextWhite");
            LettersPanel.Children.Add(t);
        }

        NoticeText.Text = flow.Notice ?? "";
        NoticeText.Visibility = flow.Notice is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
