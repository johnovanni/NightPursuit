using NightPursuit.Core.Control;
using NightPursuit.Core.Police;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class HeatTests
{
    const double Dt = World.Step;

    static World Straight() => TestWorlds.World(track: TestWorlds.Straight());

    static void Run(World w, int steps) { for (int i = 0; i < steps; i++) w.StepOnce(ControlFrame.Empty); }

    static HeatLevel? UpdateSteps(HeatMeter m, int steps, bool policeNear)
    {
        HeatLevel? last = null;
        for (int i = 0; i < steps; i++) if (m.Update(Dt, policeNear) is { } c) last = c;
        return last;
    }

    /// <summary>A police car with a still driver (not PatrolDriver.Instance, so it counts as chasing).</summary>
    static AgentCar Police(World w, double distance)
    {
        var car = AgentCar.Create(w.NextId(), AgentKind.PolicePatrol, distance, 0, 0);
        car.Driver = new StillDriver();
        w.Agents.Add(car);
        return car;
    }

    [Theory]
    [InlineData(0, HeatLevel.Calm)]
    [InlineData(24.9, HeatLevel.Calm)]
    [InlineData(25, HeatLevel.Watched)]
    [InlineData(50, HeatLevel.Patrol)]
    [InlineData(70, HeatLevel.Pursuit)]
    [InlineData(90, HeatLevel.Manhunt)]
    [InlineData(100, HeatLevel.Manhunt)]
    public void Levels_follow_the_table(double heat, HeatLevel level) => Assert.Equal(level, HeatRules.LevelOf(heat));

    [Fact]
    public void Level_names_are_words()
    {
        Assert.Equal("CALM", HeatRules.LevelName(HeatLevel.Calm));
        Assert.Equal("WATCHED", HeatRules.LevelName(HeatLevel.Watched));
        Assert.Equal("PATROL", HeatRules.LevelName(HeatLevel.Patrol));
        Assert.Equal("PURSUIT", HeatRules.LevelName(HeatLevel.Pursuit));
        Assert.Equal("MANHUNT", HeatRules.LevelName(HeatLevel.Manhunt));
    }

    [Fact]
    public void Heat_stays_between_0_and_100()
    {
        var m = new HeatMeter();
        m.Add(80); m.Add(80);
        Assert.Equal(100, m.Value);
        m.Add(-500);
        Assert.Equal(0, m.Value);
        m.Set(3);
        UpdateSteps(m, 1200, policeNear: true);
        Assert.Equal(0, m.Value);
    }

    [Fact]
    public void Decay_waits_5_s_then_falls_2_per_s_with_police_near()
    {
        // Update tests the 5 s wait before it counts the step, so decay starts on step 601.
        var m = new HeatMeter();
        m.Add(60);
        UpdateSteps(m, 600, policeNear: true);
        Assert.Equal(60, m.Value, 1e-9);
        UpdateSteps(m, 600, policeNear: true);
        Assert.Equal(50, m.Value, 1e-6);
    }

    [Fact]
    public void Decay_is_4_per_s_with_no_police_near()
    {
        var m = new HeatMeter();
        m.Add(60);
        UpdateSteps(m, 600, policeNear: true);
        UpdateSteps(m, 600, policeNear: false);
        Assert.Equal(40, m.Value, 1e-6);
    }

    [Fact]
    public void A_new_offence_restarts_the_wait()
    {
        var m = new HeatMeter();
        m.Add(60);
        UpdateSteps(m, 700, policeNear: true);
        double before = m.Value;
        Assert.True(before < 60);
        m.Add(1);
        Assert.Equal(0, m.SinceOffence);
        UpdateSteps(m, 600, policeNear: true);
        Assert.Equal(before + 1, m.Value, 1e-9);
    }

    [Fact]
    public void Rise_flash_is_set_on_a_rise_and_counts_down()
    {
        var m = new HeatMeter();
        m.Add(10);
        Assert.Equal(1, m.RiseFlash);
        UpdateSteps(m, 240, policeNear: true);
        Assert.Equal(0, m.RiseFlash);
    }

    [Fact]
    public void Rising_a_level_publishes_announces_and_messages()
    {
        var w = Straight();
        w.AddHeat(55, "TEST");
        Assert.Contains(w.Events, e => e is HeatChanged { Amount: 55, Value: 55, Reason: "TEST" });
        Assert.Contains(w.Events, e => e is HeatLevelChanged { From: HeatLevel.Calm, To: HeatLevel.Patrol });
        Assert.Equal("POLICE PATROLS", w.Announcement?.Title);
        Assert.Equal("SPEED TRAPS AND ROADBLOCKS AHEAD", w.Announcement?.Detail);
        Assert.Contains(w.Events, e => e is Message { Text: "HEAT: PATROL", Tone: Tone.Bad });
        Assert.Equal(1, w.Heat.RiseFlash);
    }

    [Fact]
    public void Each_rising_level_has_its_banner_and_one_message()
    {
        var w = Straight();
        w.AddHeat(30, "A");
        var only = Assert.Single(w.Events.OfType<Message>());
        Assert.Equal("HEAT: WATCHED - THE POLICE ARE TAKING NOTE", only.Text);
        Assert.Equal(Tone.Info, only.Tone);

        w = Straight(); w.AddHeat(75, "B");
        Assert.Equal(("POLICE PURSUIT", "DON'T LET THEM STOP YOU"), (w.Announcement!.Title, w.Announcement.Detail));
        Assert.Single(w.Events.OfType<Message>(), e => e.Text == "HEAT: PURSUIT" && e.Tone == Tone.Bad);

        w = Straight(); w.AddHeat(95, "C");
        Assert.Equal(("MANHUNT", "THEY WILL RAM YOU - SHAKE THEM WITH SMOKE"), (w.Announcement!.Title, w.Announcement.Detail));
        Assert.Single(w.Events.OfType<Message>(), e => e.Text == "HEAT: MANHUNT" && e.Tone == Tone.Bad);
    }

    [Fact]
    public void A_rise_under_an_urgent_banner_still_shows_a_message()
    {
        var w = Straight();
        w.Announce("BOSS", "WATCH OUT", 5, urgent: true);
        w.AddHeat(55, "TEST");
        Assert.Equal("BOSS", w.Announcement?.Title);
        Assert.Contains(w.Events, e => e is Message { Text: "HEAT: PATROL" });
    }

    [Fact]
    public void A_heat_banner_does_not_replace_a_banner_under_a_second_old()
    {
        var w = Straight();
        w.Announce("STRIKE: 3", "GONE", 2);
        w.AddHeat(55, "TEST");
        Assert.Equal("STRIKE: 3", w.Announcement?.Title);
        Assert.Contains(w.Events, e => e is Message { Text: "HEAT: PATROL" });
        Run(w, 130);   // over a second later
        w.AddHeat(20, "MORE");
        Assert.Equal("POLICE PURSUIT", w.Announcement?.Title);
    }

    [Fact]
    public void Levels_hold_20_s_and_fall_5_below_the_threshold()
    {
        var m = new HeatMeter();
        m.Add(75);
        Assert.Equal(HeatLevel.Pursuit, m.Level);
        // No police near: -4/s from step 601. Step 870 is 7.25 s: 75 - 4 * 270/120 = 66.
        UpdateSteps(m, 870, policeNear: false);
        Assert.Equal(66, m.Value, 1e-6);
        Assert.Equal(HeatLevel.Pursuit, m.Level);
        // Below 65 now, but the 20 s hold keeps Pursuit until step 2400.
        UpdateSteps(m, 2399 - 870, policeNear: false);
        Assert.Equal(HeatLevel.Pursuit, m.Level);
        Assert.Equal(HeatLevel.Patrol, m.Update(Dt, false));   // step 2400: 75 - 4 * 1800/120 = 15
        Assert.Equal(15, m.Value, 1e-6);
        Assert.Equal(HeatLevel.Patrol, m.Level);
        Assert.Equal(0, m.LevelTime, 1e-9);
    }

    [Fact]
    public void A_heat_of_66_after_20_s_stays_pursuit()
    {
        var m = new HeatMeter();
        m.Add(75);
        UpdateSteps(m, 2300, policeNear: false);       // 75 - 4 * 1700/120
        Assert.Equal(75 - 1700 / 30.0, m.Value, 1e-6);
        m.Add(48);                                     // about 66.3: not below 65
        Assert.Equal(HeatLevel.Pursuit, m.Level);
        Assert.Null(UpdateSteps(m, 200, policeNear: false));
        Assert.Equal(HeatLevel.Pursuit, m.Level);
        Assert.True(m.LevelTime >= 20);
    }

    [Fact]
    public void Falling_drops_one_level_per_step()
    {
        var m = new HeatMeter();
        m.Add(95);                                       // Manhunt
        UpdateSteps(m, 2399, policeNear: true);          // held 20 s needs step 2400
        m.Add(-100);                                     // value 0, but the hold is one step short
        Assert.Equal(HeatLevel.Manhunt, m.Level);
        Assert.Equal(HeatLevel.Pursuit, m.Update(Dt, true));
        Assert.Equal(HeatLevel.Pursuit, m.Level);
        Assert.Equal(0, m.LevelTime, 1e-9);
        // Pursuit must be held 20 s too.
        Assert.Null(UpdateSteps(m, 2399, policeNear: true));
        Assert.Equal(HeatLevel.Patrol, m.Update(Dt, true));
        UpdateSteps(m, 2399, policeNear: true);
        Assert.Equal(HeatLevel.Watched, m.Update(Dt, true));
        // Watched has no hold: Calm on the next step.
        Assert.Equal(HeatLevel.Calm, m.Update(Dt, true));
    }

    [Fact]
    public void Falling_a_level_says_so()
    {
        var w = Straight();
        w.AddHeat(51, "TEST");
        Assert.Equal(HeatLevel.Patrol, w.Heat.Level);
        int stepsRun = 0;
        while (w.Heat.Level == HeatLevel.Patrol && stepsRun < 4000) { Run(w, 1); stepsRun++; }
        Assert.Equal(2400, stepsRun);
        var fall = Assert.Single(w.Events.OfType<HeatLevelChanged>(), e => e.From == HeatLevel.Patrol);
        Assert.Equal(HeatLevel.Watched, fall.To);
        Assert.Single(w.Events.OfType<Message>(), e => e.Text == "HEAT DOWN: WATCHED" && e.Tone == Tone.Good);
    }

    [Fact]
    public void Three_hits_of_10_from_25_reach_patrol()
    {
        var m = new HeatMeter();
        m.Set(25);
        m.Add(10); m.Add(10); m.Add(10);
        Assert.Equal(HeatLevel.Patrol, m.Level);
        Assert.Equal(55, m.Value);
    }

    [Fact]
    public void Set_overrides_the_hold_and_returns_the_change()
    {
        var m = new HeatMeter();
        m.Add(95);
        Assert.Equal(HeatLevel.Patrol, m.Set(50));
        Assert.Equal(HeatLevel.Patrol, m.Level);
        Assert.Null(m.Set(55));
        Assert.Equal(HeatLevel.Manhunt, m.Set(100));
        Assert.Equal(HeatLevel.Calm, m.Set(0));
        Assert.Equal(0, m.LevelTime, 1e-9);
    }

    [Fact]
    public void Police_near_slows_the_decay_and_a_wreck_does_not_count()
    {
        var w = Straight();
        Police(w, 200);
        w.AddHeat(60, "TEST");
        Run(w, 1200);
        Assert.Equal(50, w.Heat.Value, 1e-6);

        var w2 = Straight();
        Police(w2, 200).State = VehicleState.Wrecked;
        w2.AddHeat(60, "TEST");
        Run(w2, 1200);
        Assert.Equal(40, w2.Heat.Value, 1e-6);
    }

    [Fact]
    public void A_far_chaser_counts_unless_blinded()
    {
        var w = Straight();
        Police(w, 1400);          // beyond 300 m but chasing (Driver is not PatrolDriver)
        w.AddHeat(60, "TEST");
        Run(w, 1200);
        Assert.Equal(50, w.Heat.Value, 1e-6);

        var w2 = Straight();
        Police(w2, 1400).Blinded = 1000;
        w2.AddHeat(60, "TEST");
        Run(w2, 1200);
        Assert.Equal(40, w2.Heat.Value, 1e-6);
    }

    [Fact]
    public void Without_police_or_offences_heat_stays_0_and_publishes_nothing()
    {
        var w = Straight();
        Run(w, 1500);
        Assert.Equal(0, w.Heat.Value);
        Assert.Equal(HeatLevel.Calm, w.Heat.Level);
        Assert.DoesNotContain(w.Events, e => e is HeatChanged or HeatLevelChanged);
    }
}
