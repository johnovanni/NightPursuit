using System.Windows.Input;

namespace NightPursuit.App.Input;

/// <summary>Tracks held keys (from KeyDown/KeyUp events) and maps them to the drive controls.</summary>
public sealed class KeyboardReader
{
    readonly HashSet<Key> _held = new();

    public void KeyDown(Key key) => _held.Add(key);
    public void KeyUp(Key key) => _held.Remove(key);
    public void ReleaseAll() => _held.Clear();

    public int SteerDir => (Held(Key.Right, Key.D) ? 1 : 0) - (Held(Key.Left, Key.A) ? 1 : 0);
    public bool Gas => Held(Key.Up, Key.W);
    public bool Brake => Held(Key.Down, Key.S);
    public bool Fire => Held(Key.Space);

    bool Held(Key a, Key? b = null) => _held.Contains(a) || (b is Key k && _held.Contains(k));
}
