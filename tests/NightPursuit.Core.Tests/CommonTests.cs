using NightPursuit.Core.Common;

namespace NightPursuit.Core.Tests;

public class CommonTests
{
    [Fact] public void Mph_round_trips() => Assert.Equal(155, Units.MsToMph(Units.MphToMs(155)), 9);

    [Fact]
    public void MoveToward_does_not_overshoot()
    {
        Assert.Equal(1.0, MathX.MoveToward(0.9, 1.0, 0.5));
        Assert.Equal(0.4, MathX.MoveToward(0.9, 0.0, 0.5), 9);
    }

    [Fact]
    public void SeededRng_is_repeatable()
    {
        var a = new SeededRng(7);
        var b = new SeededRng(7);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }
}
