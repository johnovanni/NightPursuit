using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ControlsLab;

public enum ActionKind
{
    NextScheme, SelectScheme, ShiftUp, ShiftDown, Cruise, DropOil, WheelUp, WheelDown, LeverPreset,
    ToggleHelp, TogglePause, ToggleTuning, Reset,
}

public readonly record struct GameAction(ActionKind Kind, int Value = 0);

/// <summary>Held controls for one frame. One-off presses arrive separately as <see cref="GameAction"/>s.</summary>
public sealed class InputFrame
{
    public int SteerDir;
    public bool Gas, Brake, Fire;
    public bool PadConnected, PadFire;
    public double PadSteer, PadGas, PadBrake;
}

public sealed class InputReader
{
    readonly Queue<GameAction> _actions = new();
    readonly XInputPad _pad = new();
    int _wheel;

    /// <summary>Returns true when the key belongs to the game (so WPF should not use it for focus etc.).</summary>
    public bool OnKeyDown(Key key, bool isRepeat)
    {
        GameAction? a = key switch
        {
            Key.Tab => new GameAction(ActionKind.NextScheme),
            Key.F2 => new GameAction(ActionKind.SelectScheme, 0),
            Key.F3 => new GameAction(ActionKind.SelectScheme, 1),
            Key.F4 => new GameAction(ActionKind.SelectScheme, 2),
            Key.F5 => new GameAction(ActionKind.SelectScheme, 3),
            Key.E or Key.PageUp => new GameAction(ActionKind.ShiftUp),
            Key.Q or Key.PageDown => new GameAction(ActionKind.ShiftDown),
            Key.C => new GameAction(ActionKind.Cruise),
            Key.X or Key.LeftCtrl or Key.RightCtrl => new GameAction(ActionKind.DropOil),
            >= Key.D1 and <= Key.D9 => new GameAction(ActionKind.LeverPreset, key - Key.D0),
            Key.D0 => new GameAction(ActionKind.LeverPreset, 10),
            Key.F1 => new GameAction(ActionKind.ToggleHelp),
            Key.P or Key.Escape => new GameAction(ActionKind.TogglePause),
            Key.F12 or Key.T => new GameAction(ActionKind.ToggleTuning),
            Key.R => new GameAction(ActionKind.Reset),
            _ => null,
        };
        if (a is GameAction ga && !isRepeat) _actions.Enqueue(ga);
        return a is not null || key is Key.Left or Key.Right or Key.Up or Key.Down
                                     or Key.W or Key.A or Key.S or Key.D or Key.Space;
    }

    public void OnWheel(int delta)
    {
        _wheel += delta;
        while (_wheel >= 120) { _actions.Enqueue(new GameAction(ActionKind.WheelUp)); _wheel -= 120; }
        while (_wheel <= -120) { _actions.Enqueue(new GameAction(ActionKind.WheelDown)); _wheel += 120; }
    }

    public IEnumerable<GameAction> Drain()
    {
        while (_actions.Count > 0) yield return _actions.Dequeue();
    }

    public InputFrame Read(bool windowActive)
    {
        var f = new InputFrame();
        if (windowActive)
        {
            if (Keyboard.IsKeyDown(Key.Left) || Keyboard.IsKeyDown(Key.A)) f.SteerDir -= 1;
            if (Keyboard.IsKeyDown(Key.Right) || Keyboard.IsKeyDown(Key.D)) f.SteerDir += 1;
            f.Gas = Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.W);
            f.Brake = Keyboard.IsKeyDown(Key.Down) || Keyboard.IsKeyDown(Key.S);
            f.Fire = Keyboard.IsKeyDown(Key.Space);
        }
        _pad.Poll(f, _actions);
        return f;
    }
}

/// <summary>Xbox-style controller via XInput: analog triggers make a real throttle and brake.</summary>
sealed class XInputPad
{
    [StructLayout(LayoutKind.Sequential)]
    struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct State
    {
        public uint Packet;
        public Gamepad Pad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern uint GetState(uint index, out State state);

    const ushort DPadUp = 0x0001, DPadDown = 0x0002, Start = 0x0010, Back = 0x0020,
        LeftShoulder = 0x0100, RightShoulder = 0x0200, A = 0x1000, B = 0x2000, Y = 0x8000;

    readonly Stopwatch _clock = Stopwatch.StartNew();
    bool _available = true, _connected;
    double _retryAt;
    ushort _previous;

    public void Poll(InputFrame f, Queue<GameAction> actions)
    {
        if (!_available) return;
        double now = _clock.Elapsed.TotalSeconds;
        if (!_connected && now < _retryAt) return;   // polling an absent pad is slow; retry once a second

        State st;
        try
        {
            if (GetState(0, out st) != 0) { _connected = false; _retryAt = now + 1; return; }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
            return;
        }

        _connected = true;
        var g = st.Pad;
        f.PadConnected = true;
        f.PadSteer = Deadzone(Math.Clamp(g.ThumbLX / 32767.0, -1, 1), 0.18);
        f.PadGas = g.RightTrigger / 255.0;
        f.PadBrake = g.LeftTrigger / 255.0;
        f.PadFire = (g.Buttons & A) != 0;

        ushort pressed = (ushort)(g.Buttons & ~_previous);
        _previous = g.Buttons;
        if ((pressed & B) != 0) actions.Enqueue(new GameAction(ActionKind.DropOil));
        if ((pressed & Y) != 0) actions.Enqueue(new GameAction(ActionKind.Cruise));
        if ((pressed & LeftShoulder) != 0) actions.Enqueue(new GameAction(ActionKind.ShiftDown));
        if ((pressed & RightShoulder) != 0) actions.Enqueue(new GameAction(ActionKind.ShiftUp));
        if ((pressed & DPadUp) != 0) actions.Enqueue(new GameAction(ActionKind.WheelUp));
        if ((pressed & DPadDown) != 0) actions.Enqueue(new GameAction(ActionKind.WheelDown));
        if ((pressed & Back) != 0) actions.Enqueue(new GameAction(ActionKind.NextScheme));
        if ((pressed & Start) != 0) actions.Enqueue(new GameAction(ActionKind.TogglePause));
    }

    static double Deadzone(double v, double dz) =>
        Math.Abs(v) < dz ? 0 : Math.Sign(v) * (Math.Abs(v) - dz) / (1 - dz);
}
