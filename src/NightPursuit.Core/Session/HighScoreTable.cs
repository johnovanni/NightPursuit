namespace NightPursuit.Core.Session;

public sealed record HighScoreEntry(string Initials, int Score, int MissionReached, bool Finished);

/// <summary>The best 10 scores, best first. An equal score goes below the existing ones.</summary>
public sealed class HighScoreTable
{
    public const int Size = 10;
    readonly List<HighScoreEntry> _entries = new();

    public IReadOnlyList<HighScoreEntry> Entries => _entries;

    public bool Qualifies(int score) => RankFor(score) > 0;

    /// <summary>The 1-based rank a new score would take, or 0 if it does not qualify.</summary>
    public int RankFor(int score)
    {
        if (score <= 0) return 0;
        int index = 0;
        while (index < _entries.Count && _entries[index].Score >= score) index++;
        return index >= Size ? 0 : index + 1;
    }

    /// <summary>Inserts the entry and returns its rank (0, and no insert, if it does not qualify).</summary>
    public int Add(HighScoreEntry e)
    {
        int rank = RankFor(e.Score);
        if (rank == 0) return 0;
        _entries.Insert(rank - 1, e);
        if (_entries.Count > Size) _entries.RemoveRange(Size, _entries.Count - Size);
        return rank;
    }
}
