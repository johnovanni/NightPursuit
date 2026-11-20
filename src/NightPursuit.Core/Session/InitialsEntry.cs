namespace NightPursuit.Core.Session;

/// <summary>Three letters A-Z for a high-score entry, with a cursor.</summary>
public sealed class InitialsEntry
{
    readonly char[] _letters = { 'A', 'A', 'A' };

    public InitialsEntry(string start)
    {
        int n = 0;
        foreach (char c in start ?? "")
        {
            char u = char.ToUpperInvariant(c);
            if (u is < 'A' or > 'Z') continue;
            _letters[n++] = u;
            if (n == 3) break;
        }
    }

    public string Text => new(_letters);
    public int Cursor { get; private set; }

    public void Up() => _letters[Cursor] = _letters[Cursor] == 'Z' ? 'A' : (char)(_letters[Cursor] + 1);
    public void Down() => _letters[Cursor] = _letters[Cursor] == 'A' ? 'Z' : (char)(_letters[Cursor] - 1);
    public void Left() => Cursor = Math.Max(0, Cursor - 1);
    public void Right() => Cursor = Math.Min(2, Cursor + 1);

    public void Type(char c)
    {
        char u = char.ToUpperInvariant(c);
        if (u is < 'A' or > 'Z') return;
        _letters[Cursor] = u;
        Right();
    }

    public void Delete() => Left();
}
