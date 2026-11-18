namespace NightPursuit.Core.Common;

/// <summary>Source of randomness for Core, so simulation stays deterministic and testable.</summary>
public interface IRng
{
    /// <summary>Returns a value in [0, 1).</summary>
    double NextDouble();

    /// <summary>Returns an integer in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
}

public sealed class SeededRng(int seed) : IRng
{
    private readonly Random _random = new(seed);

    public double NextDouble() => _random.NextDouble();

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
