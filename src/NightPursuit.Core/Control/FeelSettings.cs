using System.ComponentModel;
using System.Runtime.CompilerServices;
using NightPursuit.Core.Common;

namespace NightPursuit.Core.Control;

/// <summary>Live-tunable feel values (speed, throttle, steering). Raises PropertyChanged so a tuning panel can bind to it.</summary>
public sealed class FeelSettings : INotifyPropertyChanged
{
    double _throttleRampUp = 1.5, _throttleRampDown = 3.0, _maxSpeedMph = 155, _maxAccel = 9.0,
        _brakeDecel = 14, _turnRateDeg = 90, _maxHeadingDeg = 20, _steerRampUp = 4, _steerReturn = 7,
        _straighten = 8, _leverRateMph = 30, _maxSideSpeed = 10;
    bool _speedSensitiveSteering = true, _assistWhileSteering = true;

    /// <summary>How fast the keyboard throttle builds from 0 to 1 (per second).</summary>
    public double ThrottleRampUp { get => _throttleRampUp; set => Set(ref _throttleRampUp, value); }
    /// <summary>How fast the throttle falls back when the gas key is released (per second).</summary>
    public double ThrottleRampDown { get => _throttleRampDown; set => Set(ref _throttleRampDown, value); }
    public double MaxSpeedMph { get => _maxSpeedMph; set => Set(ref _maxSpeedMph, value); }
    /// <summary>Full-throttle acceleration from a standstill, m/s².</summary>
    public double MaxAccel { get => _maxAccel; set => Set(ref _maxAccel, value); }
    public double BrakeDecel { get => _brakeDecel; set => Set(ref _brakeDecel, value); }
    /// <summary>How fast full steering turns the car's nose, degrees per second (at low and middle speeds).</summary>
    public double TurnRateDeg { get => _turnRateDeg; set => Set(ref _turnRateDeg, value); }
    /// <summary>Furthest the nose can point away from the road's direction, degrees.</summary>
    public double MaxHeadingDeg { get => _maxHeadingDeg; set => Set(ref _maxHeadingDeg, value); }
    /// <summary>
    /// Fastest the car may slide sideways from its nose angle, m/s. The nose limit shrinks with speed to
    /// keep within it, so at top speed a held key cannot swing the car across the road in a second.
    /// </summary>
    public double MaxSideSpeed { get => _maxSideSpeed; set => Set(ref _maxSideSpeed, value); }
    public double SteerRampUp { get => _steerRampUp; set => Set(ref _steerRampUp, value); }
    public double SteerReturn { get => _steerReturn; set => Set(ref _steerReturn, value); }
    /// <summary>
    /// Straightening assist, per second (also while steering if AssistWhileSteering). On a bend the car drifts outward by
    /// about speed² × curvature ÷ this value metres per second, so higher = less drift.
    /// </summary>
    public double Straighten { get => _straighten; set => Set(ref _straighten, value); }
    /// <summary>How fast the speed lever moves while up/down are held, mph per second.</summary>
    public double LeverRateMph { get => _leverRateMph; set => Set(ref _leverRateMph, value); }
    /// <summary>
    /// When on, the straightening assist also works while a key is held, so a held key settles the nose at
    /// a steady angle and letting go stops the slide quickly. When off, it only works with no input.
    /// </summary>
    public bool AssistWhileSteering { get => _assistWhileSteering; set => Set(ref _assistWhileSteering, value); }
    public bool SpeedSensitiveSteering { get => _speedSensitiveSteering; set => Set(ref _speedSensitiveSteering, value); }

    /// <summary>Top speed in m/s.</summary>
    public double MaxSpeed => MaxSpeedMph / Units.MphPerMs;

    /// <summary>Largest nose angle at speed <paramref name="v"/>, radians: the set maximum, or less at speed (see MaxSideSpeed).</summary>
    public double MaxHeadingAt(double v)
    {
        double limit = MaxHeadingDeg * Math.PI / 180;
        return v > MaxSideSpeed ? Math.Min(limit, Math.Asin(MaxSideSpeed / v)) : limit;
    }

    /// <summary>Nose turn rate at speed <paramref name="v"/>, rad/s: weak when crawling, lighter at top speed if enabled.</summary>
    public double TurnRateAt(double v)
    {
        double r = Math.Min(1, v / MaxSpeed);
        return TurnRateDeg * Math.PI / 180 * Math.Clamp(v / 8, 0, 1) * (SpeedSensitiveSteering ? 1 - 0.5 * r : 1);
    }

    public void Reset()
    {
        var d = new FeelSettings();
        ThrottleRampUp = d.ThrottleRampUp; ThrottleRampDown = d.ThrottleRampDown; MaxSpeedMph = d.MaxSpeedMph;
        MaxAccel = d.MaxAccel; BrakeDecel = d.BrakeDecel; TurnRateDeg = d.TurnRateDeg; MaxHeadingDeg = d.MaxHeadingDeg;
        MaxSideSpeed = d.MaxSideSpeed; SteerRampUp = d.SteerRampUp; SteerReturn = d.SteerReturn;
        Straighten = d.Straighten; LeverRateMph = d.LeverRateMph;
        SpeedSensitiveSteering = d.SpeedSensitiveSteering; AssistWhileSteering = d.AssistWhileSteering;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
