using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Tests;

public class CampaignTests
{
    static string MissionPath(string id) => Path.Combine(AppContext.BaseDirectory, "content", "missions", id + ".json");

    static MissionDefinition[] LoadAll() => Campaign.MissionIds.Select(id => MissionLoader.LoadFile(MissionPath(id))).ToArray();

    [Fact]
    public void All_six_campaign_missions_load()
    {
        var all = LoadAll();
        Assert.Equal(6, all.Length);
        Assert.Equal(Campaign.MissionIds, all.Select(d => d.Id).ToArray());
    }

    [Fact]
    public void Campaign_rises_in_difficulty()
    {
        var all = LoadAll();
        for (int i = 1; i < all.Length; i++)
        {
            Assert.True(all[i].Boss!.Health > all[i - 1].Boss!.Health, $"boss health at mission {i + 1}");
            Assert.True(all[i].ApproachLength >= all[i - 1].ApproachLength, $"approach at mission {i + 1}");
        }
        Assert.Equal(6, all.Select(d => d.Road.Seed).Distinct().Count());
    }

    [Fact]
    public void Only_missions_5_and_6_have_missile_phases()
    {
        var all = LoadAll();
        for (int i = 0; i < all.Length; i++)
        {
            int n = all[i].Boss!.Phases.Count(p => p.FiresMissiles);
            Assert.Equal(i >= 4 ? 1 : 0, n);
        }
    }

    [Fact]
    public void Mission_values_match_the_plan()
    {
        var m2 = MissionLoader.LoadFile(MissionPath("mission-02"));
        Assert.Equal("Harbour Lights", m2.Title);
        Assert.Equal(6500, m2.ApproachLength);
        Assert.Equal(4, m2.Traffic!.CarsPerKm);
        Assert.Equal(1, m2.Police!.Presence);
        Assert.Equal(1500, m2.Bonus);
        Assert.Equal("Iron Gull", m2.Boss!.Name);
        Assert.Equal(220, m2.Boss.Health);
        Assert.Equal(BossLook.Twin, m2.Boss.Look);
        Assert.Equal(2, m2.Boss.Phases.Count);

        var m6 = MissionLoader.LoadFile(MissionPath("mission-06"));
        Assert.Equal("Last Light", m6.Title);
        Assert.Equal(9000, m6.ApproachLength);
        Assert.Equal(5, m6.Traffic!.CarsPerKm);
        Assert.Equal(2, m6.Police!.Presence);
        Assert.Equal(25, m6.Police.StartingHeat);
        Assert.Equal(4000, m6.Bonus);
        Assert.Equal("Obsidian Crown", m6.Boss!.Name);
        Assert.Equal(400, m6.Boss.Health);
        Assert.Equal(BossLook.Dome, m6.Boss.Look);
        Assert.Equal(3, m6.Boss.Phases.Count);
    }

    [Theory]
    [InlineData("mission-01")]
    [InlineData("mission-02")]
    [InlineData("mission-03")]
    [InlineData("mission-04")]
    [InlineData("mission-05")]
    [InlineData("mission-06")]
    public void Every_campaign_mission_runs_90_seconds_without_error(string id)
    {
        var run = new MissionRun(MissionLoader.LoadFile(MissionPath(id)), new FeelSettings(), 1);
        for (int i = 0; i < 90 * 60; i++)
            run.Update(new ControlFrame { Gas = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(run.World) }, 1 / 60.0);
        Assert.True(run.World.Player.Distance > 1000 || run.State is RunState.GameOver or RunState.Completed);
    }
}
