namespace NightPursuit.Core.Common;

public static class MathX
{
    /// <summary>Moves <paramref name="value"/> toward <paramref name="target"/> by at most <paramref name="step"/> without overshooting.</summary>
    public static double MoveToward(double value, double target, double step)
    {
        double delta = target - value;
        if (Math.Abs(delta) <= step) return target;
        return value + Math.Sign(delta) * step;
    }
}
