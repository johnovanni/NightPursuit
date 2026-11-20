namespace NightPursuit.Core.Session;

public enum MenuInput { Up, Down, Left, Right, Confirm, Back, Delete, Letter, Pause, Help }

/// <summary>One key press as the menus see it. <see cref="Letter"/> is set for a typed letter (any case); '\0' otherwise.</summary>
public readonly record struct MenuKey(MenuInput Input, char Letter = '\0');
