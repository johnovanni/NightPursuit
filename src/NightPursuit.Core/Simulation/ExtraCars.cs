namespace NightPursuit.Core.Simulation;

/// <summary>
/// An extra spare car for every 25,000 points of total score (25,000, 50,000, ...), from any source; at most 5 spare
/// cars (owner, 2026-11-23).
/// </summary>
public static class ExtraCars
{
    public const int Every = 25000, MaxSpare = 5;

    /// <summary>How many 25,000-point marks a score passed going from <paramref name="scoreBefore"/> to <paramref name="scoreAfter"/>.</summary>
    public static int Earned(int scoreBefore, int scoreAfter) =>
        scoreAfter <= scoreBefore ? 0 : Math.Max(0, scoreAfter / Every - Math.Max(0, scoreBefore) / Every);

    /// <summary>How many of <paramref name="earned"/> cars fit with <paramref name="spareCars"/> already held.</summary>
    public static int Kept(int earned, int spareCars) => Math.Clamp(MaxSpare - spareCars, 0, Math.Max(0, earned));
}
