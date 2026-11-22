using System.Diagnostics;
using System.Runtime.InteropServices;
using NightPursuit.Core.Control;
using NightPursuit.Core.Session;

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

    const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, Start = 0x0010,
        Back = 0x0020, LeftShoulder = 0x0100, RightShoulder = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    /// <summary>Held pad values for one frame.</summary>
    public readonly record struct Reading(bool Connected, bool Fire, double Steer, double Gas, double Brake);

    readonly Stopwatch _clock = Stopwatch.StartNew();
    bool _available = true, _connected;
    double _retryAt;
    ushort _previous;
    bool _resync;

    public bool Connected => _connected;

    /// <summary>
    /// Call while the window is inactive: the next poll treats whatever is held then as already pressed,
    /// so a button held during alt-tab does not fire a press on return.
    /// </summary>
    public void SkipHeldButtons() => _resync = true;

    /// <summary>Polls the pad; queues press-edge game actions into <paramref name="actions"/> and menu keys into <paramref name="menu"/>.</summary>
    public Reading Poll(Queue<GameAction> actions, Queue<MenuKey> menu)
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
        if (_resync) { _previous = g.Buttons; _resync = false; }
        ushort pressed = (ushort)(g.Buttons & ~_previous);
        _previous = g.Buttons;
        if ((pressed & B) != 0) actions.Enqueue(new GameAction(ActionKind.DropRear));
        if ((pressed & X) != 0) actions.Enqueue(new GameAction(ActionKind.CycleRear));
        if ((pressed & RightShoulder) != 0) actions.Enqueue(new GameAction(ActionKind.FireMissile));
        if ((pressed & LeftShoulder) != 0) actions.Enqueue(new GameAction(ActionKind.FireStrike));
        if ((pressed & Y) != 0) actions.Enqueue(new GameAction(ActionKind.Cruise));
        if ((pressed & DPadUp) != 0) actions.Enqueue(new GameAction(ActionKind.LeverUp));
        if ((pressed & DPadDown) != 0) actions.Enqueue(new GameAction(ActionKind.LeverDown));
        if ((pressed & Start) != 0) actions.Enqueue(new GameAction(ActionKind.Pause));
        if ((pressed & Back) != 0) actions.Enqueue(new GameAction(ActionKind.Restart));
        if ((pressed & A) != 0) actions.Enqueue(new GameAction(ActionKind.SkipFinale));   // a new press only: held A is fire

        if ((pressed & DPadUp) != 0) menu.Enqueue(new MenuKey(MenuInput.Up));
        if ((pressed & DPadDown) != 0) menu.Enqueue(new MenuKey(MenuInput.Down));
        if ((pressed & DPadLeft) != 0) menu.Enqueue(new MenuKey(MenuInput.Left));
        if ((pressed & DPadRight) != 0) menu.Enqueue(new MenuKey(MenuInput.Right));
        if ((pressed & A) != 0) menu.Enqueue(new MenuKey(MenuInput.Confirm));
        if ((pressed & B) != 0) menu.Enqueue(new MenuKey(MenuInput.Back));
        if ((pressed & Start) != 0) menu.Enqueue(new MenuKey(MenuInput.Pause));
        if ((pressed & Back) != 0) menu.Enqueue(new MenuKey(MenuInput.Back));

        return new Reading(true, (g.Buttons & A) != 0,
            Deadzone(Math.Clamp(g.ThumbLX / 32767.0, -1, 1), 0.18),
            g.RightTrigger / 255.0, g.LeftTrigger / 255.0);
    }

    static double Deadzone(double v, double dz) =>
        Math.Abs(v) < dz ? 0 : Math.Sign(v) * (Math.Abs(v) - dz) / (1 - dz);
}
