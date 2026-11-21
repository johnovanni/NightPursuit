namespace NightPursuit.Core.Control;

public enum ActionKind { SelectScheme, Cruise, LeverPreset, LeverUp, LeverDown, DropRear, Pause, ToggleHelp, Restart, CycleRear, FireMissile, FireStrike, ToggleSound }

/// <summary>A one-off press (not a held control).</summary>
public readonly record struct GameAction(ActionKind Kind, int Value = 0);
