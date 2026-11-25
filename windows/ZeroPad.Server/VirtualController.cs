using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ZeroPad.Server;

/// <summary>
/// Optional Xbox 360/XInput backend. Requires a separately installed ViGEmBus driver.
/// The upstream driver is retired; see docs/driver-evaluation.md before using in production.
/// </summary>
internal sealed class VirtualController : IDisposable
{
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _pad;
    private bool _disposed;

    public VirtualController()
    {
        _client = new ViGEmClient();
        try
        {
            _pad = _client.CreateXbox360Controller();
            _pad.AutoSubmitReport = false;
            _pad.Connect();
            Reset();
        }
        catch
        {
            _client.Dispose();
            throw;
        }
    }

    public void Apply(ControllerState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Set(Xbox360Button.A, state, "A");
        Set(Xbox360Button.B, state, "B");
        Set(Xbox360Button.X, state, "X");
        Set(Xbox360Button.Y, state, "Y");
        Set(Xbox360Button.Up, state, "UP");
        Set(Xbox360Button.Down, state, "DOWN");
        Set(Xbox360Button.Left, state, "LEFT");
        Set(Xbox360Button.Right, state, "RIGHT");
        Set(Xbox360Button.LeftShoulder, state, "LB");
        Set(Xbox360Button.RightShoulder, state, "RB");
        Set(Xbox360Button.Start, state, "START");
        Set(Xbox360Button.Back, state, "SELECT");
        Set(Xbox360Button.LeftThumb, state, "L3");
        Set(Xbox360Button.RightThumb, state, "R3");

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(state.LeftX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxis(state.LeftY));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, ToAxis(state.RightX));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, ToAxis(state.RightY));
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, ToTrigger(state.LeftTrigger));
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, ToTrigger(state.RightTrigger));
        _pad.SubmitReport();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pad.ResetReport();
        _pad.SubmitReport();
    }

    private void Set(Xbox360Button button, ControllerState state, string name) =>
        _pad.SetButtonState(button, state.Buttons.Contains(name));

    private static short ToAxis(float value) =>
        (short)Math.Clamp(MathF.Round(value * 32767f), short.MinValue, short.MaxValue);

    private static byte ToTrigger(float value) =>
        (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    public void Dispose()
    {
        if (_disposed) return;
        try { Reset(); } catch { /* Device may already have disconnected. */ }
        try { _pad.Disconnect(); } finally { _client.Dispose(); _disposed = true; }
    }
}
