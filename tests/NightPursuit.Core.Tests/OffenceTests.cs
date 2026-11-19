using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Tests;

public class OffenceTests
{
    static readonly ControlFrame Firing = new() { Fire = true };

    static World Straight()
    {
        var w = TestWorlds.World(track: TestWorlds.Straight());
        w.Player.Speed = 0;
        w.Player.SnapPrev();
        return w;
    }

    static AgentCar Car(World w, AgentKind kind, double distance, double lateral, double speed = 0, double? armor = null)
    {
        var car = AgentCar.Create(w.NextId(), kind, distance, lateral, speed);
        car.Driver = new StillDriver();
        if (armor is { } a) car.Armor = a;
        w.Agents.Add(car);
        return car;
    }

    static void Run(World w, int steps, ControlFrame? input = null) { for (int i = 0; i < steps; i++) w.StepOnce(input ?? ControlFrame.Empty); }

    /// <summary>Contact distance along the road between the player and <paramref name="kind"/>.</summary>
    static double ContactZ(World w, AgentKind kind) => (AgentCar.Create(0, kind, 0, 0, 0).Stats.Length + w.Player.Stats.Length) / 2;

    /// <summary>Puts <paramref name="car"/> just inside the player's nose, in the player's lane, so the player has rear-ended it.</summary>
    static void PlaceRearEnded(World w, AgentCar car)
    {
        car.Distance = w.Player.Distance + ContactZ(w, car.Kind) - 0.5;
        car.Lateral = w.Player.Lateral;
        car.ContactCooldown = 0;
    }

    static List<GameEvent> Collect(World w, int steps, ControlFrame? input = null)
    {
        int start = w.Events.Count;     // StepOnce does not clear the events (Advance does), so take what is new
        for (int i = 0; i < steps; i++) w.StepOnce(input ?? ControlFrame.Empty);
        return w.Events.Skip(start).ToList();
    }

    [Fact]
    public void Three_civilian_hits_reach_patrol_level()
    {
        var w = Straight();
        w.Heat.Set(25);
        for (int i = 0; i < 3; i++)
        {
            var civilian = Car(w, AgentKind.Civilian, 0, 0);
            PlaceRearEnded(w, civilian);
            Collisions.Resolve(w);
            w.Agents.Remove(civilian);
        }
        Assert.Equal(HeatLevel.Patrol, w.Heat.Level);
        Assert.Equal(55, w.Heat.Value, 1e-9);
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void Three_civilian_hits_from_zero_reach_watched()
    {
        var w = Straight();
        for (int i = 0; i < 3; i++)
        {
            var civilian = Car(w, AgentKind.Civilian, 0, 0);
            PlaceRearEnded(w, civilian);
            Collisions.Resolve(w);
            w.Agents.Remove(civilian);
        }
        Assert.Equal(HeatLevel.Watched, w.Heat.Level);
        Assert.Equal(30, w.Heat.Value, 1e-9);
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void Holding_fire_on_a_civilian_adds_at_most_10_per_second()
    {
        var w = Straight();
        var civilian = Car(w, AgentKind.Civilian, 40, 0, armor: 1000);
        var events = Collect(w, 360, Firing);
        Assert.InRange(w.Heat.Value, 20, 30 + 1e-9);
        Assert.InRange(events.OfType<Message>().Count(m => m.Text == "CIVILIAN HIT  +10 HEAT"), 1, 3);
        Assert.True(civilian.Armor < 1000);
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void Shooting_a_civilian_dead_raises_heat_and_leaves_a_wreck()
    {
        var w = Straight();
        var civilian = Car(w, AgentKind.Civilian, 40, 0);   // armor 4
        var events = Collect(w, 240, Firing);
        var destroyed = Assert.Single(events.OfType<VehicleDestroyed>());
        Assert.Equal(Faction.Civilian, destroyed.Faction);
        Assert.Equal(0, destroyed.Points);
        Assert.Contains(w.Hazards, h => h is Wreck wreck && Math.Abs(wreck.Lateral - civilian.Lateral) < 2.5);
        Assert.True(w.Heat.Value >= 25);
        Assert.Contains(events, e => e is Message { Text: "CIVILIAN DESTROYED  +25 HEAT", Tone: Tone.Bad });
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void A_car_hitting_you_costs_you_nothing()
    {
        var w = Straight();
        w.Player.Speed = 10;
        var civilian = Car(w, AgentKind.Civilian, 0, 0, speed: 20);
        civilian.Distance = w.Player.Distance - ContactZ(w, AgentKind.Civilian) + 0.5;
        double armor = w.Player.Armor;
        Collisions.Resolve(w);
        Assert.True(w.Player.Armor < armor);                                  // the normal contact damage
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "HIT FROM BEHIND" });

        var cruiser = Car(w, AgentKind.PoliceCruiser, 0, 1.5);                // on the player's right, moving in
        cruiser.LateralVel = -1;
        Collisions.Resolve(w);
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "SIDESWIPED" });

        Assert.Equal(0, w.Heat.Value);
        Assert.Equal(0, civilian.PlayerBlameUntil);
        Assert.Equal(0, cruiser.PlayerBlameUntil);
        Assert.DoesNotContain(w.Events, e => e is HeatChanged);
    }

    [Fact]
    public void Interceptor_ram_costs_you_no_heat()
    {
        var w = Straight();
        w.Heat.Set(95);
        var interceptor = Car(w, AgentKind.Interceptor, 0, 1.5);
        interceptor.LateralVel = -1.5;
        Collisions.Resolve(w);
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "SIDESWIPED" });
        Assert.Equal(95, w.Heat.Value);
        Run(w, 60);
        Assert.Equal(60 * World.Step, w.Heat.SinceOffence, 1e-9);   // it kept growing through the ram: not reset
        Assert.Equal(95, w.Heat.Value);
        Assert.Equal(0, interceptor.PlayerBlameUntil);
    }

    [Fact]
    public void Police_bullets_cost_5_heat_each()
    {
        var w = Straight();
        var police = Car(w, AgentKind.PolicePatrol, 40, 0, armor: 1000);
        int steps = 0;
        while (police.Armor > 990 && steps++ < 600) w.StepOnce(Firing);
        Assert.Equal(990, police.Armor);
        Assert.Equal(50, w.Heat.Value, 1e-9);
    }

    [Fact]
    public void Police_shot_message_is_shown_at_most_once_per_second()
    {
        var w = Straight();
        Car(w, AgentKind.PolicePatrol, 40, 0, armor: 1000);
        var events = Collect(w, 240, Firing);
        int shot = events.OfType<Message>().Count(m => m.Text == "POLICE CAR SHOT  +5 HEAT");
        int hits = events.OfType<HeatChanged>().Count(h => h.Reason == "SHOT A POLICE CAR");
        Assert.InRange(shot, 1, 2);
        Assert.True(hits > shot);
    }

    [Fact]
    public void Ramming_a_police_car_costs_15_once_per_second()
    {
        var w = Straight();
        var police = Car(w, AgentKind.PolicePatrol, 0, 0, armor: 1000);
        void Bump()
        {
            w.Player.Speed = 20;
            police.Speed = 0;
            PlaceRearEnded(w, police);
            w.StepOnce(ControlFrame.Empty);
        }
        Bump();
        Assert.Equal(15, w.Heat.Value, 1e-9);
        Run(w, 60);                                    // 0.5 s later
        Bump();
        Assert.Equal(15, w.Heat.Value, 1e-9);
        Run(w, 132);                                   // 1.1 s after the first
        Bump();
        Assert.Equal(30, w.Heat.Value, 1e-9);
    }

    [Fact]
    public void Rear_ending_a_slow_police_car_is_not_your_fault()
    {
        var w = Straight();
        w.Player.Speed = 25;
        var police = Car(w, AgentKind.PoliceCruiser, 0, 0, speed: 30, armor: 1000);   // cruise speed 30
        police.Speed = 20;                                                            // held back below it
        PlaceRearEnded(w, police);
        Collisions.Resolve(w);
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "RAMMED" });
        Assert.Equal(0, w.Heat.Value);
        Assert.True(police.PlayerBlameUntil > w.Time);   // no heat for the touch, but a death within 5 s is yours
    }

    [Fact]
    public void Killing_a_slow_police_car_by_rear_ending_it_counts_against_you()
    {
        var w = Straight();
        w.Player.Speed = 25;
        var police = Car(w, AgentKind.PoliceCruiser, 0, 0, speed: 30, armor: 0.5);
        police.Speed = 20;
        PlaceRearEnded(w, police);
        Collisions.Resolve(w);
        Assert.Contains(w.Events, e => e is Message { Text: "POLICE CAR DESTROYED  +40 HEAT", Tone: Tone.Bad });
        Assert.Equal(40, w.Heat.Value, 1e-9);
        Assert.Equal(0, w.Score);
    }

    [Theory]
    [InlineData(AgentKind.Civilian, 10)]
    [InlineData(AgentKind.PolicePatrol, 15)]
    public void Steering_into_a_car_beside_you_is_your_fault(AgentKind kind, double heat)
    {
        var w = Straight();
        w.Player.LateralVel = 2;                       // your own steering, no shove
        var car = Car(w, kind, 0, 1.5, armor: 1000);
        Collisions.Resolve(w);
        Assert.Contains(w.Events, e => e is PlayerDamaged { Reason: "SIDESWIPED" });
        Assert.Equal(heat, w.Heat.Value, 1e-9);
        Assert.True(car.PlayerBlameUntil > w.Time);
    }

    [Fact]
    public void Being_shoved_into_a_car_by_an_enemy_is_not_your_fault()
    {
        var w = Straight();
        var civilian = Car(w, AgentKind.Civilian, 0, 1.5);         // on your right
        w.Player.PushVel = 3;                                      // an earlier Runner sideswipe (or enemy oil) shoved you toward it; LateralVel includes the shove
        w.Player.LateralVel = 3;
        Collisions.Resolve(w);
        Assert.Equal(0, w.Heat.Value);
        Assert.Equal(0, civilian.PlayerBlameUntil);
        Assert.DoesNotContain(w.Events, e => e is HeatChanged);
    }

    [Fact]
    public void Enemy_caused_civilian_crash_costs_nothing()
    {
        var w = Straight();
        var runner = Car(w, AgentKind.Runner, 100, 3.6);                     // first id: it is "a" in the pair
        var civilian = Car(w, AgentKind.Civilian, 100, 5.0, speed: 0);
        runner.LateralVel = 6;
        Collisions.Resolve(w);
        Assert.Equal(VehicleState.Spinning, civilian.State);
        var events = Collect(w, 360);
        var destroyed = Assert.Single(events.OfType<VehicleDestroyed>(), d => d.VehicleId == civilian.Id);
        Assert.Equal(Faction.Civilian, destroyed.Faction);
        Assert.Equal(0, destroyed.Points);
        Assert.Contains(events, e => e is Message { Text: "CIVILIAN CRASHED", Tone: Tone.Info });
        Assert.Equal(0, w.Score);
        Assert.Equal(0, w.Heat.Value);
        Assert.DoesNotContain(events, e => e is HeatChanged);
    }

    [Fact]
    public void Your_oil_crash_counts_against_you()
    {
        // The oil's spin direction comes from the world's Rng: pick a seed that slides the civilian outwards.
        int seed = Enumerable.Range(1, 200).First(s => new SeededRng(s).Next(2) == 1);
        var w = TestWorlds.World(seed, TestWorlds.Straight());
        var civilian = Car(w, AgentKind.Civilian, 100, 5.8, speed: 0);
        w.Hazards.Add(new OilSlick { Distance = 100, Lateral = 5.8 });         // yours: not hostile
        var events = Collect(w, 600);
        Assert.Contains(events, e => e is VehicleDestroyed { Cause: KillCause.RanOffRoad, Faction: Faction.Civilian, Points: 0 });
        Assert.Contains(events, e => e is Message { Text: "CIVILIAN DESTROYED  +25 HEAT", Tone: Tone.Bad });
        Assert.True(w.Heat.Value >= 25);
        Assert.Equal(0, w.Score);
        Assert.Contains(events, e => e is Message { Text: "CIVILIAN HIT THE OIL", Tone: Tone.Info });
    }

    [Fact]
    public void Enemy_oil_does_not_blame_you()
    {
        var w = Straight();
        var civilian = Car(w, AgentKind.Civilian, 100, 5.8, speed: 0);
        w.Hazards.Add(new OilSlick { Distance = 100, Lateral = 5.8, Hostile = true });
        Run(w, 5);
        Assert.Equal(VehicleState.Spinning, civilian.State);
        Assert.Equal(0, civilian.PlayerBlameUntil);
    }

    [Fact]
    public void Your_caltrops_puncture_a_civilian_as_information_and_blame_you()
    {
        var w = Straight();
        var civilian = Car(w, AgentKind.Civilian, 100, 0, speed: 0);
        var police = Car(w, AgentKind.PolicePatrol, 100, 5, speed: 0);
        w.Hazards.Add(new Caltrops { Distance = 100, Lateral = 0 });
        w.Hazards.Add(new Caltrops { Distance = 100, Lateral = 5 });
        var events = Collect(w, 2);
        Assert.Contains(events, e => e is Message { Text: "CIVILIAN PUNCTURED", Tone: Tone.Info });
        Assert.Contains(events, e => e is Message { Text: "POLICE PUNCTURED", Tone: Tone.Info });
        Assert.True(civilian.PlayerBlameUntil > w.Time);
        Assert.True(police.PlayerBlameUntil > w.Time);
    }

    [Fact]
    public void Spin_messages_for_non_hostiles_are_information()
    {
        var w = Straight();
        var runner = Car(w, AgentKind.Runner, 100, 0);
        var civilian = Car(w, AgentKind.Civilian, 200, 0);
        w.Spin(runner, 1, "RUNNER HIT THE OIL");
        w.Spin(civilian, 1, "CIVILIAN HIT THE OIL");
        var messages = w.Events.OfType<Message>().ToList();
        Assert.Equal(Tone.Good, messages[0].Tone);
        Assert.Equal(Tone.Info, messages[1].Tone);
    }

    [Fact]
    public void Blame_wears_off_after_5_s()
    {
        var w = Straight();
        var civilian = Car(w, AgentKind.Civilian, 0, 0);
        PlaceRearEnded(w, civilian);
        Collisions.Resolve(w);                         // you rear-ended it
        Assert.True(civilian.PlayerBlameUntil > w.Time);
        w.Player.Speed = 0;
        civilian.Distance = 100;
        var events = Collect(w, 612);                  // 5.1 s
        int start = w.Events.Count;
        w.Destroy(civilian, KillCause.RanOffRoad);     // a Runner rams it off the road
        events.AddRange(w.Events.Skip(start));
        Assert.DoesNotContain(events, e => e is HeatChanged { Reason: "CIVILIAN DESTROYED" });
        Assert.DoesNotContain(events, e => e is Message { Text: "CIVILIAN DESTROYED  +25 HEAT" });
        Assert.Contains(events, e => e is Message { Text: "CIVILIAN CRASHED" });
    }

    [Fact]
    public void Civilians_and_police_never_change_the_score()
    {
        var w = Straight();
        w.Score = 700;
        var civilian = Car(w, AgentKind.Civilian, 0, 0);
        PlaceRearEnded(w, civilian);
        Collisions.Resolve(w);                         // civilian hit
        Assert.Equal(700, w.Score);
        w.Destroy(civilian, KillCause.Gunfire);        // blamed civilian destroyed
        Assert.Equal(700, w.Score);
        var police = Car(w, AgentKind.PolicePatrol, 60, 0, armor: 1000);
        Collect(w, 30, Firing);                        // police shot
        Assert.True(police.Armor < 1000);
        Assert.Equal(700, w.Score);
        w.BlamePlayer(police);
        w.Destroy(police, KillCause.Gunfire);          // police destroyed
        Assert.Equal(700, w.Score);
        Assert.True(w.Heat.Value > 0);
        Assert.Contains(w.Events, e => e is Message { Text: "POLICE CAR DESTROYED  +40 HEAT", Tone: Tone.Bad });
    }

    [Fact]
    public void Police_destroyed_without_blame_is_a_crash()
    {
        var w = Straight();
        var police = Car(w, AgentKind.PoliceCruiser, 100, 0);
        w.Destroy(police, KillCause.RanOffRoad);
        Assert.Contains(w.Events, e => e is Message { Text: "POLICE CAR CRASHED", Tone: Tone.Info });
        Assert.Equal(0, w.Heat.Value);
        Assert.Contains(w.Events, e => e is VehicleDestroyed { Faction: Faction.Police, Points: 0 });
    }

    [Fact]
    public void Hostile_scoring_is_unchanged()
    {
        var w = Straight();
        var runner = Car(w, AgentKind.Runner, 40, 0, armor: 1);
        var events = Collect(w, 120, Firing);
        Assert.Contains(events, e => e is VehicleDestroyed { Kind: AgentKind.Runner, Faction: Faction.Hostile });
        Assert.Equal(VehicleStats.Runner.Points, w.Score);
        Assert.Contains(events, e => e is Message { Text: var t } && t == $"RUNNER DESTROYED  +{VehicleStats.Runner.Points}");
        Assert.Equal(0, w.Heat.Value);

        // D54: an enemy killing an enemy still scores.
        var w2 = Straight();
        var a = Car(w2, AgentKind.Runner, 100, 0, speed: 20);
        var b = Car(w2, AgentKind.Runner, 100 + 3, 0, speed: 10, armor: 0.4);
        Collisions.Resolve(w2);
        Assert.Equal(VehicleStats.Runner.Points, w2.Score);
    }

    [Fact]
    public void State_hash_covers_blame_and_hit_cooldown()
    {
        var w = Straight();
        var car = Car(w, AgentKind.Civilian, 100, 0);
        ulong before = w.StateHash();
        w.BlamePlayer(car);
        ulong blamed = w.StateHash();
        Assert.NotEqual(before, blamed);
        car.HitHeatCooldown = 0.5;
        Assert.NotEqual(blamed, w.StateHash());
    }
}
