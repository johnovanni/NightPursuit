using System.Windows;
using System.Windows.Controls;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Screens;

/// <summary>
/// The mission-complete (with its bonus lines), game-over and finished-all screens. Every figure is words (NFR-3).
/// Confirming is ignored until the confirm and fire controls have been released once (GameLoop), so a player still firing
/// as the mission ends does not skip the screen.
/// </summary>
public partial class ResultScreen : UserControl
{
    // In practice Enter goes back to the title and R starts the mission over (ScreenFlow).
    const string PracticeKeys = "ENTER  BACK TO TITLE   R  RESTART";

    /// <summary>A bonus line: the label in a fixed 22-place column, then the amount right-aligned, so the amounts line up (Consolas is monospaced).</summary>
    static string BonusLine(string label, int points) => label.PadRight(22) + ("+" + points).PadLeft(6);

    public ResultScreen() => InitializeComponent();

    public void Update(ScreenFlow flow)
    {
        var g = flow.Game;
        if (g is null) return;
        LinesPanel.Children.Clear();
        switch (flow.Current)
        {
            case Screen.MissionComplete:
                TitleText.Text = g.Practice ? "PRACTICE COMPLETE" : $"MISSION {g.MissionIndex + 1} COMPLETE";
                if (g.Mission.Boss is { } boss) Add($"{boss.Name.ToUpperInvariant()} DESTROYED", "TextHeading", "TextWhite", bold: true);
                if (g.LastBonus is { } b)
                {
                    Add(BonusLine("MISSION BONUS", b.Base), "TextBody", "TextWhite");
                    Add(BonusLine($"ARMOR LEFT {b.ArmorLeft} X 10", b.ArmorPoints), "TextBody", "TextWhite");
                    Add(BonusLine(b.NoCarLost ? "NO CAR LOST" : "A CAR WAS LOST", b.CleanPoints), "TextBody", "TextWhite");
                }
                Add($"SCORE {g.Score:00000}   SPARE CARS {g.SpareCars}", "TextHeading", "TextCyan", bold: true, top: 14);
                KeyText.Text = g.Practice ? PracticeKeys : g.MissionIndex + 1 < g.Missions.Count ? "ENTER  NEXT MISSION" : "ENTER  CONTINUE";
                break;
            case Screen.GameOver:
                TitleText.Text = g.QuitEarly ? "GAME ENDED" : "GAME OVER";
                Add($"MISSION REACHED {g.MissionReached} OF {g.Missions.Count}", "TextHeading", "TextWhite");
                Add($"FINAL SCORE {g.Score:00000}", "TextHeading", "TextCyan", bold: true, top: 10);
                KeyText.Text = g.Practice ? PracticeKeys : "ENTER  CONTINUE";
                break;
            default:   // Finished
                TitleText.Text = "ALL SIX MISSIONS COMPLETE";
                Add("THE SYNDICATE IS BROKEN. THE ROAD IS YOURS.", "TextHeading", "TextWhite");
                Add($"FINAL SCORE {g.Score:00000}", "TextHeading", "TextCyan", bold: true, top: 10);
                KeyText.Text = g.Practice ? "ENTER  BACK TO TITLE" : "ENTER  CONTINUE";
                break;
        }
        NoticeText.Text = flow.Notice ?? "";
        NoticeText.Visibility = flow.Notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void Add(string text, string size, string brush, bool bold = false, double top = 4)
    {
        var t = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, top, 0, 4) };
        if (bold) t.FontWeight = FontWeights.Bold;
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        LinesPanel.Children.Add(t);
    }
}
