using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ZeroPad.Server;

/// <summary>
/// Xbox 360/XInput backend through a separately installed ViGEmBus driver.
/// The upstream driver is retired; see docs/driver-evaluation.md.
/// </summary>
internal sealed class VirtualController : IPadSink
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

    public void Apply(XInputReport r)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pad.SetButtonsFull(r.Buttons);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, r.LeftX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, r.LeftY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, r.RightX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, r.RightY);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, r.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, r.RightTrigger);
        _pad.SubmitReport();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pad.ResetReport();
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { Reset(); } catch { /* device may already have disconnected */ }
        try { _pad.Disconnect(); } finally { _client.Dispose(); _disposed = true; }
    }
}
