using NightPursuit.Core.Common;

namespace NightPursuit.Core.Control;

/// <summary>Turns held keys or the analog stick into one steering value from -1 (left) to +1 (right).</summary>
public sealed class SteeringInput
{
    public double Value { get; private set; }
    public bool UsingStick { get; private set; }

    public void Update(ControlFrame input, FeelSettings f, double dt)
    {
        UsingStick = input.PadConnected && input.SteerDir == 0 && Math.Abs(input.PadSteer) > 0;
        if (UsingStick)
        {
            Value = Math.Clamp(input.PadSteer, -1, 1);
            return;
        }
        int target = input.SteerDir;
        if (target == 0)
            Value = MathX.MoveToward(Value, 0, f.SteerReturn * dt);
        else
        {
            bool reversing = Math.Sign(Value) == -target;
            double rate = reversing ? f.SteerReturn + f.SteerRampUp : f.SteerRampUp;
            Value = MathX.MoveToward(Value, target, rate * dt);
        }
    }

    public void Reset()
    {
        Value = 0;
        UsingStick = false;
    }
}
