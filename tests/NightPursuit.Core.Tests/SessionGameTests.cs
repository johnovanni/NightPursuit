using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Tests;

public class SessionGameTests
{
    static MissionDefinition Def(string id, int bonus, LoadoutDefinition? loadout = null, PoliceDefinition? police = null) =>
        new(id, id, "Briefing", new TrackProfile(7), 300, 5, Array.Empty<WaveDefinition>(),
            StartingLoadout: loadout, Police: police, Bonus: bonus);

    static MissionDefinition[] Three() => new[]
    {
        Def("a", 1000),
        Def("b", 2000, new LoadoutDefinition(Missiles: 2), new PoliceDefinition(1, 30)),
        Def("c", 1000),
    };

    static Game NewGame(int count = 3, GameSettings? settings = null, bool practice = false) =>
        new(Three().Take(count).ToArray(), new FeelSettings(), settings ?? new GameSettings(), practice, 5);

    static void Tick(Game g) => g.Update(ControlFrame.Empty, 1 / 60.0);

    static void Complete(Game g)
    {
        g.Run!.World.Player.Distance = g.Mission.ApproachLength;
        Tick(g);
    }

    static void Wreck(Game g)
    {
        g.Run!.World.Player.Armor = 0;
        Tick(g);
        for (int i = 0; i < 20 && g.Run.State == RunState.Respawning; i++) g.Update(ControlFrame.Empty, 0.25);
    }

    [Fact]
    public void Score_and_spares_carry_and_bonus_is_added_once()
    {
        var g = NewGame();
        Assert.Equal(GameStage.Briefing, g.Stage);
        Assert.Null(g.Run);
        g.StartMission();
        g.Run!.World.Score = 700;
        g.Run.World.Player.Armor = 64;
        Complete(g);
        Assert.Equal(GameStage.MissionComplete, g.Stage);
        Assert.Equal(1000 + 640 + 1000, g.LastBonus!.Total);
        Assert.Equal(3340, g.Score);
        Tick(g);
        Assert.Equal(3340, g.Score);
        Assert.NotNull(g.Run);

        g.Continue();
        Assert.Equal(GameStage.Briefing, g.Stage);
        Assert.Null(g.Run);
        Assert.Equal(1, g.MissionIndex);
        g.StartMission();
        Assert.Equal(3340, g.Run!.World.Score);
        Assert.Equal(3, g.Run.World.Player.SpareCars);
    }

    [Fact]
    public void A_lost_car_costs_the_clean_bonus_and_carries()
    {
        var g = NewGame();
        g.StartMission();
        Wreck(g);
        Complete(g);
        Assert.False(g.LastBonus!.NoCarLost);
        Assert.Equal(0, g.LastBonus.CleanPoints);
        g.Continue();
        g.StartMission();
        Assert.Equal(2, g.Run!.World.Player.SpareCars);
        Assert.Equal(2, g.SpareCars);
    }

    [Fact]
    public void Each_mission_starts_with_full_armor_its_own_loadout_and_heat()
    {
        var g = NewGame();
        g.StartMission();
        g.Run!.World.Player.Armor = 10;
        while (g.Run.World.Loadout.Missiles.Charges > 0) g.Run.World.Loadout.Missiles.TryUse();
        Complete(g);
        g.Continue();
        g.StartMission();
        Assert.Equal(100, g.Run!.World.Player.Armor);
        Assert.Equal(2, g.Run.World.Loadout.Missiles.Charges);
        Assert.Equal(30, g.Run.World.Heat.Value);
    }

    [Fact]
    public void Game_over_records_the_mission_reached()
    {
        var g = NewGame();
        g.StartMission();
        Complete(g);
        g.Continue();
        g.StartMission();
        for (int i = 0; i < 4; i++) Wreck(g);
        Assert.Equal(GameStage.GameOver, g.Stage);
        Assert.Equal(2, g.MissionReached);
        Assert.Equal(g.Run!.World.Score, g.Score);
        Assert.False(g.FinishedAll);
    }

    [Fact]
    public void Finishing_the_last_mission_reaches_Finished()
    {
        var g = NewGame();
        for (int i = 0; i < 3; i++)
        {
            g.StartMission();
            Complete(g);
            g.Continue();
        }
        Assert.Equal(GameStage.Finished, g.Stage);
        Assert.True(g.FinishedAll);
        Assert.Equal(3, g.MissionReached);
        Assert.NotNull(g.Run);
    }

    [Fact]
    public void Restart_is_ignored_in_a_campaign_and_works_in_practice()
    {
        var g = NewGame();
        g.StartMission();
        Tick(g);
        g.Handle(new GameAction(ActionKind.Pause));
        var world = g.Run!.World;
        double time = world.Time;
        g.Handle(new GameAction(ActionKind.Restart));
        g.RestartMission();
        Assert.Same(world, g.Run.World);
        Assert.Equal(time, g.Run.World.Time);

        var p = NewGame(practice: true);
        p.StartMission();
        Tick(p);
        p.Handle(new GameAction(ActionKind.Pause));
        var before = p.Run!.World;
        p.Handle(new GameAction(ActionKind.Restart));
        Assert.NotSame(before, p.Run.World);
    }

    [Fact]
    public void A_practice_restart_keeps_the_scheme_from_settings()
    {
        var settings = new GameSettings { SchemeIndex = 0 };
        var g = NewGame(settings: settings, practice: true);
        g.StartMission();
        settings.SchemeIndex = 1;
        g.Handle(new GameAction(ActionKind.Pause));
        g.Handle(new GameAction(ActionKind.Restart));
        Assert.Equal(1, g.Run!.World.SchemeIndex);
    }

    [Fact]
    public void Restart_while_playing_is_dropped_even_in_practice()
    {
        var g = NewGame(practice: true);
        g.StartMission();
        var world = g.Run!.World;
        g.Handle(new GameAction(ActionKind.Restart));
        Assert.Same(world, g.Run.World);
    }

    [Fact]
    public void Quit_ends_the_game_with_the_score_so_far()
    {
        var g = NewGame();
        g.StartMission();
        g.Run!.World.Score = 450;
        g.Quit();
        Assert.Equal(GameStage.GameOver, g.Stage);
        Assert.True(g.QuitEarly);
        Assert.Equal(450, g.Score);
        Assert.Equal(1, g.MissionReached);
    }

    [Fact]
    public void Quit_from_the_briefing_keeps_the_carried_score()
    {
        var g = NewGame();
        g.StartMission();
        Complete(g);
        int score = g.Score;
        g.Continue();
        g.Quit();
        Assert.Equal(score, g.Score);
        Assert.Equal(2, g.MissionReached);
    }

    [Fact]
    public void The_scheme_comes_from_settings()
    {
        var g = NewGame(settings: new GameSettings { SchemeIndex = 1 });
        g.StartMission();
        Assert.Equal(1, g.Run!.World.SchemeIndex);
    }

    [Fact]
    public void An_empty_mission_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Game(Array.Empty<MissionDefinition>(), new FeelSettings(), new GameSettings()));
    }

    [Fact]
    public void The_campaign_lists_six_missions_in_order()
    {
        Assert.Equal(6, Campaign.MissionIds.Length);
        Assert.Equal("mission-01", Campaign.MissionIds[0]);
    }

    [Fact]
    public void Practice_restart_works_from_the_end_stages_only()
    {
        var p = NewGame(practice: true);
        p.StartMission();
        Complete(p);
        Assert.Equal(GameStage.MissionComplete, p.Stage);
        var w = p.Run!.World;
        p.RestartMission();
        Assert.Equal(GameStage.Playing, p.Stage);
        Assert.NotSame(w, p.Run.World);

        var g = NewGame();
        g.StartMission();
        Complete(g);
        var cw = g.Run!.World;
        g.RestartMission();
        Assert.Equal(GameStage.MissionComplete, g.Stage);
        Assert.Same(cw, g.Run.World);
    }
}
