using System.Windows.Input;
using NightPursuit.Core.Control;

namespace NightPursuit.App.Input;

/// <summary>Combines keyboard, mouse wheel and gamepad into held controls plus a queue of one-off actions.</summary>
public sealed class InputHub
{
    readonly Queue<GameAction> _actions = new();
    readonly KeyboardReader _keys = new();
    readonly XInputPad _pad = new();
    int _wheel;

    public bool PadConnected => _pad.Connected;

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
            Key.Z or Key.LeftShift or Key.RightShift => new GameAction(ActionKind.CycleRear),
            Key.F => new GameAction(ActionKind.FireMissile),
            Key.V => new GameAction(ActionKind.FireStrike),
            >= Key.D1 and <= Key.D9 => new GameAction(ActionKind.LeverPreset, key - Key.D0),
            Key.D0 => new GameAction(ActionKind.LeverPreset, 10),
            Key.F1 => new GameAction(ActionKind.ToggleHelp),
            Key.P or Key.Escape => new GameAction(ActionKind.Pause),
            Key.R => new GameAction(ActionKind.Restart),
            _ => null,
        };
        if (a is GameAction ga && !isRepeat) _actions.Enqueue(ga);
        return a is not null || key is Key.Left or Key.Right or Key.Up or Key.Down
                                     or Key.W or Key.A or Key.S or Key.D or Key.Space;
    }

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

    /// <summary>Held controls for this frame. An inactive window gets an empty frame and forgets held keys.</summary>
    public ControlFrame Read(bool windowActive)
    {
        if (!windowActive)
        {
            _keys.ReleaseAll();
            _pad.SkipHeldButtons();
            return ControlFrame.Empty;
        }
        var p = _pad.Poll(_actions);
        return new ControlFrame
        {
            SteerDir = _keys.SteerDir, Gas = _keys.Gas, Brake = _keys.Brake, Fire = _keys.Fire,
            PadConnected = p.Connected, PadFire = p.Fire, PadSteer = p.Steer, PadGas = p.Gas, PadBrake = p.Brake,
        };
    }
}
