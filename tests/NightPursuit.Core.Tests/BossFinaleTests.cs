using NightPursuit.Core.Common;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Tests;

public class BossFinaleTests
{
    const double Tol = 1e-6;

    public static TheoryData<FinaleKind, int> KindsAndSides()
    {
        var data = new TheoryData<FinaleKind, int>();
        foreach (var kind in Enum.GetValues<FinaleKind>())
            foreach (int side in new[] { -1, 1 })
                data.Add(kind, side);
        return data;
    }

    static BossFinale Start(FinaleKind kind, int side, int turns = 1, double spin = 0) =>
        new(new FinaleChoice(kind, side, turns), 7, 1000, -side * 3, 60, spin);

    static void StepTo(BossFinale f, double seconds)
    {
        while (f.Elapsed < seconds - 1e-9 && !f.Done) f.Step(World.Step);
    }

    [Theory]
    [MemberData(nameof(KindsAndSides))]
    public void Every_kind_ends_on_its_side_of_the_verge_and_comes_to_rest(FinaleKind kind, int side)
    {
        var f = Start(kind, side);
        double last = f.Distance;
        while (f.Elapsed < BossFinale.ExplodeAt - 1e-9)
        {
            f.Step(World.Step);
            Assert.True(f.Distance >= last, $"distance went back at {f.Elapsed}");
            last = f.Distance;
        }
        Assert.Equal(side, Math.Sign(f.Lateral));
        Assert.True(Math.Abs(f.Lateral) >= Track.RoadHalfWidth + 2, $"lateral {f.Lateral}");
        Assert.Equal(0, f.Speed, Tol);
    }

    [Fact]
    public void It_keeps_most_of_its_speed_through_the_spin_out_then_slows_to_rest()
    {
        var f = Start(FinaleKind.Flip, 1);
        StepTo(f, BossFinale.SpinOutSeconds);
        Assert.Equal(54, f.Speed, 1e-6);                                  // 90% of 60 when it trips
        Assert.Equal(1000 + 60 * 1.2 * 0.95, f.Distance, 1e-6);           // 68.4 m during the spin-out
        StepTo(f, BossFinale.ExplodeAt);
        Assert.Equal(1000 + 68.4 + 54 * 1.3 / 2, f.Distance, 1e-6);      // then 35.1 m more: 103.5 m in all
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Roll_turns_about_the_long_axis_and_flip_end_over_end(int side)
    {
        var roll = Start(FinaleKind.Roll, side, turns: 2);
        StepTo(roll, BossFinale.ExplodeAt);
        Assert.Equal(side * 2 * Math.Tau, roll.Roll, Tol);
        Assert.Equal(0, roll.Pitch);

        var flip = Start(FinaleKind.Flip, side, turns: 1);
        StepTo(flip, BossFinale.ExplodeAt);
        Assert.Equal(Math.Tau, flip.Pitch, Tol);
        Assert.Equal(0, flip.Roll);

        var spin = Start(FinaleKind.SpinOff, side);
        while (!spin.Done)
        {
            spin.Step(World.Step);
            Assert.Equal((0.0, 0.0), (spin.Roll, spin.Pitch));
        }
    }

    [Theory]
    [MemberData(nameof(KindsAndSides))]
    public void Lift_is_only_off_the_road_and_never_above_one_and_a_half_metres(FinaleKind kind, int side)
    {
        var f = Start(kind, side);
        double mid = (BossFinale.SpinOutSeconds + BossFinale.ExplodeAt) / 2;
        bool sawMid = false;
        while (!f.Done)
        {
            f.Step(World.Step);
            double t = f.Elapsed;
            if (kind == FinaleKind.SpinOff || t <= BossFinale.SpinOutSeconds + 1e-9 || t >= BossFinale.ExplodeAt - 1e-9)
                Assert.Equal(0, f.Lift);
            else Assert.InRange(f.Lift, 0, 1.5);
            if (kind != FinaleKind.SpinOff && Math.Abs(t - mid) < World.Step / 2)
            {
                Assert.True(f.Lift > 0, "a roll or flip leaves the ground mid-way");
                sawMid = true;
            }
        }
        Assert.Equal(kind != FinaleKind.SpinOff, sawMid);
    }

    [Theory]
    [InlineData(FinaleKind.Roll, -1)]
    [InlineData(FinaleKind.Roll, 1)]
    [InlineData(FinaleKind.Flip, -1)]
    [InlineData(FinaleKind.Flip, 1)]
    public void A_roll_or_flip_trips_square_on(FinaleKind kind, int side)
    {
        var f = Start(kind, side, spin: 0.4);
        StepTo(f, BossFinale.SpinOutSeconds);
        Assert.Equal(0.4 + side * Math.Tau, f.SpinAngle, Tol);
        StepTo(f, BossFinale.ExplodeAt);
        Assert.Equal(0.4 + side * Math.Tau, f.SpinAngle, Tol);
    }

    [Theory]
    [MemberData(nameof(KindsAndSides))]
    public void Step_reports_the_explosion_once_and_done_after_the_hold(FinaleKind kind, int side)
    {
        var f = Start(kind, side);
        int explosions = 0;
        double doneAt = double.NaN;
        for (int i = 0; i < 1000; i++)
        {
            bool before = f.Exploded;
            if (f.Step(World.Step))
            {
                explosions++;
                Assert.False(before);
                Assert.Equal(BossFinale.ExplodeAt, f.Elapsed, 1e-6);
            }
            if (f.Done && double.IsNaN(doneAt)) doneAt = f.Elapsed;
        }
        Assert.Equal(1, explosions);
        Assert.True(f.Exploded);
        Assert.Equal(BossFinale.TotalSeconds, doneAt, 1e-6);
        Assert.Equal(BossFinale.TotalSeconds, BossFinale.ExplodeAt + BossFinale.HoldSeconds, 1e-12);
    }

    [Fact]
    public void Skip_ends_it_without_an_explosion()
    {
        var f = Start(FinaleKind.Flip, 1);
        StepTo(f, 0.5);
        f.Skip();
        Assert.True(f.Done);
        Assert.True(f.Skipped);
        Assert.False(f.Exploded);
        var (d, l, e) = (f.Distance, f.Lateral, f.Elapsed);
        for (int i = 0; i < 600; i++) Assert.False(f.Step(World.Step));
        Assert.Equal((d, l, e), (f.Distance, f.Lateral, f.Elapsed));
        Assert.False(f.Exploded);
    }

    [Fact]
    public void Same_choice_and_start_give_the_same_poses()
    {
        var a = Start(FinaleKind.Roll, -1, turns: 2, spin: 1);
        var b = Start(FinaleKind.Roll, -1, turns: 2, spin: 1);
        while (!a.Done)
        {
            Assert.Equal(a.Step(World.Step), b.Step(World.Step));
            Assert.Equal((a.Distance, a.Lateral, a.Speed, a.SpinAngle), (b.Distance, b.Lateral, b.Speed, b.SpinAngle));
            Assert.Equal((a.Roll, a.Pitch, a.Lift), (b.Roll, b.Pitch, b.Lift));
        }
        Assert.True(b.Done);
    }

    [Fact]
    public void Interpolation_is_between_the_last_two_steps()
    {
        var f = Start(FinaleKind.Flip, 1, turns: 2);
        StepTo(f, 1.8);
        f.Step(World.Step);
        Assert.NotEqual(f.PrevPitch, f.Pitch);
        Assert.Equal(f.PrevPitch, f.PitchAt(0));
        Assert.Equal(f.Pitch, f.PitchAt(1));
        Assert.Equal((f.PrevPitch + f.Pitch) / 2, f.PitchAt(0.5), 1e-12);
        Assert.Equal(f.PrevLift, f.LiftAt(0));
        Assert.Equal(f.Lift, f.LiftAt(1));
        Assert.Equal((f.PrevLift + f.Lift) / 2, f.LiftAt(0.5), 1e-12);

        var r = Start(FinaleKind.Roll, -1);
        StepTo(r, 1.8);
        r.Step(World.Step);
        Assert.NotEqual(r.PrevRoll, r.Roll);
        Assert.Equal(r.PrevRoll, r.RollAt(0));
        Assert.Equal(r.Roll, r.RollAt(1));
        Assert.Equal((r.PrevRoll + r.Roll) / 2, r.RollAt(0.5), 1e-12);
    }

    [Fact]
    public void The_picker_never_repeats_kind_and_side_and_uses_every_combination()
    {
        var picker = new FinalePicker(new SeededRng(5));
        Assert.Null(picker.Last);
        var seen = new HashSet<(FinaleKind, int)>();
        var turns = new HashSet<int>();
        FinaleChoice? previous = null;
        for (int i = 0; i < 600; i++)
        {
            var c = picker.Next();
            Assert.Equal(c, picker.Last);
            Assert.Contains(c.Side, new[] { -1, 1 });
            if (previous is { } p) Assert.NotEqual((p.Kind, p.Side), (c.Kind, c.Side));
            seen.Add((c.Kind, c.Side));
            if (c.Kind == FinaleKind.SpinOff) Assert.Equal(0, c.Turns);
            else { Assert.InRange(c.Turns, 1, 2); turns.Add(c.Turns); }
            previous = c;
        }
        Assert.Equal(6, seen.Count);
        Assert.Equal(new[] { 1, 2 }, turns.Order().ToArray());
    }

    [Fact]
    public void A_spin_off_choice_has_no_turns_and_sides_are_plus_or_minus_one()
    {
        Assert.Equal(new FinaleChoice(FinaleKind.SpinOff, -1, 0), new FinaleChoice(FinaleKind.SpinOff, -5, 2));
        Assert.Equal(new FinaleChoice(FinaleKind.Roll, 1, 1), FinaleChoice.Default);
    }
}
