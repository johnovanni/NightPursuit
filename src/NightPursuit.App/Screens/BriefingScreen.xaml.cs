using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Police;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Screens;

/// <summary>
/// The mission briefing: the mission's title and text, its boss with a portrait, what you start with and what the road
/// holds. Every fact is words (NFR-3). A mission without a boss says so instead of showing the boss lines.
/// </summary>
public partial class BriefingScreen : UserControl
{
    public BriefingScreen() => InitializeComponent();

    public void Update(ScreenFlow flow)
    {
        var game = flow.Game;
        if (game is null) return;
        var m = game.Mission;

        MissionText.Text = game.Practice ? "PRACTICE" : $"MISSION {game.MissionIndex + 1} OF {game.Missions.Count}";
        TitleText.Text = m.Title.ToUpperInvariant();
        BriefingText.Text = m.Briefing;

        if (m.Boss is { } boss)
        {
            BossPanel.Visibility = Visibility.Visible;
            NoBossText.Visibility = Visibility.Collapsed;
            Portrait.Look = boss.Look;
            Portrait.Name = boss.Name;
            BossNameText.Text = "BOSS: " + boss.Name.ToUpperInvariant();
            PhasesText.Text = $"{boss.Phases.Count} PHASES";
            bool missiles = boss.Phases.Any(p => p.FiresMissiles);
            MissileText.Text = missiles ? "FIRES MISSILES: WATCH FOR THE MISSILE WARNING" : "";
            MissileText.Visibility = missiles ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            BossPanel.Visibility = Visibility.Collapsed;
            NoBossText.Visibility = Visibility.Visible;
            NoBossText.Text = "NO BOSS: REACH THE END OF THE ROAD";
        }

        var l = m.Loadout;
        LoadoutText.Text = $"LOADOUT   OIL {l.Oil}   SMOKE {l.Smoke}   CALTROPS {l.Caltrops}   MISSILES {l.Missiles}   STRIKES {l.Strikes}";
        RoadText.Text = string.Create(CultureInfo.InvariantCulture,
            $"ROAD      {m.ApproachLength / 1000:0.0} KM TO THE {(m.Boss is null ? "END" : "BOSS")}");
        TrafficText.Text = "TRAFFIC   " + TrafficWords(m.Traffic?.CarsPerKm ?? 0);
        PoliceText.Text = "POLICE    " + PoliceWords(m.Police);
        ScoreText.Text = $"SCORE {game.Score:00000}   SPARE CARS {game.SpareCars}";

        NoticeText.Text = flow.Notice ?? "";
        NoticeText.Visibility = flow.Notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>0 is NONE, up to 2 per km LIGHT, up to 5 MODERATE, above 5 HEAVY; the figure follows when above 0.</summary>
    static string TrafficWords(double carsPerKm)
    {
        if (carsPerKm <= 0) return "NONE";
        string level = carsPerKm <= 2 ? "LIGHT" : carsPerKm <= 5 ? "MODERATE" : "HEAVY";
        return string.Create(CultureInfo.InvariantCulture, $"{level} ({carsPerKm:0.#} CARS PER KM)");
    }

    /// <summary>None or presence 0 is NONE, below 1 LOW, exactly 1 NORMAL, above 1 HIGH; then the starting heat when above 0.</summary>
    static string PoliceWords(PoliceDefinition? police)
    {
        if (police is not { Presence: > 0 }) return "NONE";
        string level = police.Presence < 1 ? "LOW" : police.Presence == 1 ? "NORMAL" : "HIGH";
        if (police.StartingHeat <= 0) return level;
        return string.Create(CultureInfo.InvariantCulture,
            $"{level}   STARTING HEAT {police.StartingHeat:0}: {HeatRules.LevelName(HeatRules.LevelOf(police.StartingHeat))}");
    }
}
