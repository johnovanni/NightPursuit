using System.Diagnostics;
using System.Runtime.InteropServices;
using NightPursuit.Core.Control;

namespace NightPursuit.App.Input;

/// <summary>Xbox-style controller via XInput: analog triggers make a real throttle and brake.</summary>
public sealed class XInputPad
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

    const ushort DPadUp = 0x0001, DPadDown = 0x0002, Start = 0x0010,
        A = 0x1000, B = 0x2000, Y = 0x8000;

    /// <summary>Held pad values for one frame.</summary>
    public readonly record struct Reading(bool Connected, bool Fire, double Steer, double Gas, double Brake);

    readonly Stopwatch _clock = Stopwatch.StartNew();
    bool _available = true, _connected;
    double _retryAt;
    ushort _previous;

    public bool Connected => _connected;

    /// <summary>Polls the pad; queues press-edge actions into <paramref name="actions"/>.</summary>
    public Reading Poll(Queue<GameAction> actions)
    {
        if (!_available) return default;
        double now = _clock.Elapsed.TotalSeconds;
        if (!_connected && now < _retryAt) return default;   // polling an absent pad is slow; retry once a second

        State st;
        try
        {
            if (GetState(0, out st) != 0) { _connected = false; _previous = 0; _retryAt = now + 1; return default; }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
            _connected = false;
            return default;
        }

        _connected = true;
        var g = st.Pad;
        ushort pressed = (ushort)(g.Buttons & ~_previous);
        _previous = g.Buttons;
        if ((pressed & B) != 0) actions.Enqueue(new GameAction(ActionKind.DropRear));
        if ((pressed & Y) != 0) actions.Enqueue(new GameAction(ActionKind.Cruise));
        if ((pressed & DPadUp) != 0) actions.Enqueue(new GameAction(ActionKind.LeverUp));
        if ((pressed & DPadDown) != 0) actions.Enqueue(new GameAction(ActionKind.LeverDown));
        if ((pressed & Start) != 0) actions.Enqueue(new GameAction(ActionKind.Pause));

        return new Reading(true, (g.Buttons & A) != 0,
            Deadzone(Math.Clamp(g.ThumbLX / 32767.0, -1, 1), 0.18),
            g.RightTrigger / 255.0, g.LeftTrigger / 255.0);
    }

    static double Deadzone(double v, double dz) =>
        Math.Abs(v) < dz ? 0 : Math.Sign(v) * (Math.Abs(v) - dz) / (1 - dz);
}
