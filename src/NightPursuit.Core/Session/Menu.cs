namespace NightPursuit.Core.Session;

public sealed record MenuEntry(string Id, string Label, string? Value = null);

/// <summary>A titled list of items with one selected (named MenuModel so it does not clash with WPF's Menu).</summary>
public sealed class MenuModel
{
    public MenuModel(string title, IReadOnlyList<string> lines, IReadOnlyList<MenuEntry> items, int selected = 0)
    {
        if (items.Count == 0) throw new ArgumentException("A menu needs at least one item.", nameof(items));
        Title = title;
        Lines = lines;
        Items = items;
        Selected = Math.Clamp(selected, 0, items.Count - 1);
    }

    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<MenuEntry> Items { get; }
    public int Selected { get; private set; }
    public MenuEntry Current => Items[Selected];

    public void Move(int delta)
    {
        int n = Items.Count;
        Selected = ((Selected + delta) % n + n) % n;
    }
}
