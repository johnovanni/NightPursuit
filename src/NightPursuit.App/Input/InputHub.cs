using System.Windows.Input;
using NightPursuit.Core.Control;
using NightPursuit.Core.Session;

namespace NightPursuit.App.Input;

/// <summary>
/// Combines keyboard, mouse wheel and gamepad into held controls plus two queues of one-off presses: game actions (for play)
/// and menu keys (for the screens). Every press goes into both; the game loop reads one queue and drops the other each frame.
/// </summary>
public sealed class InputHub
{
    readonly Queue<GameAction> _actions = new();
    readonly Queue<MenuKey> _menu = new();
    readonly KeyboardReader _keys = new();
    readonly XInputPad _pad = new();
    int _wheel;

    public bool PadConnected => _pad.Connected;

    /// <summary>Enter is held now (it confirms screens but is not part of the control frame).</summary>
    public bool EnterHeld => _keys.EnterHeld;

    /// <summary>Returns true when the key belongs to the game (so WPF should not use it for focus etc.).</summary>
    public bool OnKeyDown(Key key, bool isRepeat)
    {
        _keys.KeyDown(key);
        GameAction? a = key switch
        {
            Key.F2 => new GameAction(ActionKind.SelectScheme, 0),
            Key.F3 => new GameAction(ActionKind.SelectScheme, 1),
            Key.C => new GameAction(ActionKind.Cruise),
            Key.X or Key.LeftCtrl or Key.RightCtrl => new GameAction(ActionKind.DropRear),
            // Tab only (D68): Z sat next to the X drop key, and five Shift presses open Windows' Sticky Keys prompt.
            // Handled here, so Tab never moves WPF focus.
            Key.Tab => new GameAction(ActionKind.CycleRear),
            Key.F => new GameAction(ActionKind.FireMissile),
            Key.V => new GameAction(ActionKind.FireStrike),
            >= Key.D1 and <= Key.D9 => new GameAction(ActionKind.LeverPreset, key - Key.D0),
            Key.D0 => new GameAction(ActionKind.LeverPreset, 10),
            Key.F1 => new GameAction(ActionKind.ToggleHelp),
            Key.P or Key.Escape => new GameAction(ActionKind.Pause),
            Key.R => new GameAction(ActionKind.Restart),
            Key.M => new GameAction(ActionKind.ToggleSound),
            _ => null,
        };
        if (a is GameAction ga && !isRepeat) _actions.Enqueue(ga);
        MenuKey? m = MenuKeyFor(key);
        // Holding a direction scrolls a menu or a letter; nothing else repeats.
        if (m is MenuKey mk && (!isRepeat || mk.Input is MenuInput.Up or MenuInput.Down or MenuInput.Left or MenuInput.Right))
            _menu.Enqueue(mk);
        return a is not null || m is not null;
    }

    /// <summary>The menu key for a keyboard key: arrows and W A S D move (W A S D also carry their letter, for typing initials), other letters type, Enter and Space confirm, Esc goes back.</summary>
    static MenuKey? MenuKeyFor(Key key) => key switch
    {
        Key.Up => new MenuKey(MenuInput.Up),
        Key.Down => new MenuKey(MenuInput.Down),
        Key.Left => new MenuKey(MenuInput.Left),
        Key.Right => new MenuKey(MenuInput.Right),
        Key.W => new MenuKey(MenuInput.Up, 'W'),
        Key.S => new MenuKey(MenuInput.Down, 'S'),
        Key.A => new MenuKey(MenuInput.Left, 'A'),
        Key.D => new MenuKey(MenuInput.Right, 'D'),
        >= Key.A and <= Key.Z => new MenuKey(MenuInput.Letter, (char)('A' + (key - Key.A))),
        Key.Enter or Key.Space => new MenuKey(MenuInput.Confirm),
        Key.Escape => new MenuKey(MenuInput.Back),
        Key.Back => new MenuKey(MenuInput.Delete),
        Key.F1 => new MenuKey(MenuInput.Help),
        _ => null,
    };

    public void OnKeyUp(Key key) => _keys.KeyUp(key);

    /// <summary>Forgets every held key (window lost focus, so KeyUp events will be missed).</summary>
    public void ReleaseAll() => _keys.ReleaseAll();

    /// <summary>One LeverUp/LeverDown per wheel notch (120 units).</summary>
    public void OnWheel(int delta)
    {
        _wheel += delta;
        while (_wheel >= 120) { _actions.Enqueue(new GameAction(ActionKind.LeverUp)); _wheel -= 120; }
        while (_wheel <= -120) { _actions.Enqueue(new GameAction(ActionKind.LeverDown)); _wheel += 120; }
    }

    public IEnumerable<GameAction> Drain()
    {
        while (_actions.Count > 0) yield return _actions.Dequeue();
    }

    /// <summary>The menu keys pressed since the last call, oldest first.</summary>
    public IEnumerable<MenuKey> DrainMenu()
    {
        while (_menu.Count > 0) yield return _menu.Dequeue();
    }

    /// <summary>Held controls for this frame. An inactive window gets an empty frame and forgets held keys.</summary>
    public ControlFrame Read(bool windowActive)
    {
        if (!windowActive)
        {
            _keys.ReleaseAll();
            _pad.SkipHeldButtons();
            return ControlFrame.Empty;
        }
        var p = _pad.Poll(_actions, _menu);
        return new ControlFrame
        {
            SteerDir = _keys.SteerDir, Gas = _keys.Gas, Brake = _keys.Brake, Fire = _keys.Fire,
            PadConnected = p.Connected, PadFire = p.Fire, PadSteer = p.Steer, PadGas = p.Gas, PadBrake = p.Brake,
        };
    }
}
