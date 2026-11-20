using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

/// <summary>WorstDamage drives the windshield cracks: it follows armor down, never back up, and resets with a new car.</summary>
public class WindshieldDamageTests
{
    static MissionRun Run() => new(
        new MissionDefinition("test", "Test", "Briefing", new TrackProfile(7), 5000, 5, [],
            StartingLoadout: new LoadoutDefinition(5, 2, 2, 2, 1)),
        new FeelSettings(), 3);

    [Fact]
    public void A_new_car_has_no_damage()
    {
        Assert.Equal(0, Run().World.Player.WorstDamage);
    }

    [Fact]
    public void Worst_damage_follows_armor_down_and_stays_when_armor_comes_back()
    {
        var run = Run();
        var p = run.World.Player;
        p.Armor = 62;
        run.World.StepOnce(ControlFrame.Empty);
        Assert.Equal(38, p.WorstDamage, 6);

        p.Armor = 95;   // a supply crate
        run.World.StepOnce(ControlFrame.Empty);
        Assert.Equal(38, p.WorstDamage, 6);

        p.Armor = 30;
        run.World.StepOnce(ControlFrame.Empty);
        Assert.Equal(70, p.WorstDamage, 6);
    }

    [Fact]
    public void A_wreck_is_full_damage_and_the_respawned_car_starts_clean()
    {
        var run = Run();
        var p = run.World.Player;
        p.Armor = 0;
        run.Update(ControlFrame.Empty, World.Step);
        Assert.Equal(RunState.Respawning, run.State);
        Assert.Equal(100, p.WorstDamage, 6);

        for (int i = 0; i < 20 && run.State == RunState.Respawning; i++) run.Update(ControlFrame.Empty, 0.25);
        Assert.Equal(RunState.Playing, run.State);
        Assert.Equal(0, p.WorstDamage);
    }

    [Fact]
    public void Worst_damage_is_not_part_of_the_state_hash()
    {
        var run = Run();
        ulong before = run.World.StateHash();
        run.World.Player.WorstDamage = 55;
        Assert.Equal(before, run.World.StateHash());
    }
}
