using NightPursuit.Core.Common;

namespace NightPursuit.Core.Missions;

/// <summary>
/// Picks each boss crash: any kind and side except the last one's, with equal odds, and 1 or 2 turns for a roll or
/// flip. One picker serves a whole game, so a restarted mission ends differently too.
/// </summary>
public sealed class FinalePicker(IRng rng)
{
    static readonly (FinaleKind Kind, int Side)[] All =
    {
        (FinaleKind.SpinOff, -1), (FinaleKind.SpinOff, 1), (FinaleKind.Roll, -1),
        (FinaleKind.Roll, 1), (FinaleKind.Flip, -1), (FinaleKind.Flip, 1),
    };

    public FinaleChoice? Last { get; private set; }

    public FinaleChoice Next()
    {
        var options = Last is { } last ? All.Where(o => o != (last.Kind, last.Side)).ToArray() : All;
        var (kind, side) = options[rng.Next(options.Length)];
        int turns = kind == FinaleKind.SpinOff ? 0 : 1 + rng.Next(2);
        var choice = new FinaleChoice(kind, side, turns);
        Last = choice;
        return choice;
    }
}
