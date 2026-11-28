namespace ZeroPad.Server;

/// <summary>What an Xbox 360 / XInput pad reports. Same numbers Windows games read from XInput.</summary>
public readonly record struct XInputReport(ushort Buttons, short LeftX, short LeftY, short RightX, short RightY, byte LeftTrigger, byte RightTrigger);

/// <summary>
/// Pure ControllerState -> XInput conversion, kept free of any driver so it can be tested anywhere.
/// Y axes are "up is positive", exactly like XInput.
/// </summary>
public static class XInputMap
{
    public const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008;
    public const ushort Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100, RightShoulder = 0x0200;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    private static readonly (string Name, ushort Mask)[] ButtonMasks =
    {
        ("UP", DPadUp), ("DOWN", DPadDown), ("LEFT", DPadLeft), ("RIGHT", DPadRight),
        ("START", Start), ("SELECT", Back), ("L3", LeftThumb), ("R3", RightThumb),
        ("LB", LeftShoulder), ("RB", RightShoulder),
        ("A", A), ("B", B), ("X", X), ("Y", Y),
    };

    public static XInputReport From(ControllerState state)
    {
        ushort buttons = 0;
        foreach (var (name, mask) in ButtonMasks)
            if (state.Buttons.Contains(name)) buttons |= mask;

        var (lx, ly) = Stick(state.LeftX, state.LeftY);
        var (rx, ry) = Stick(state.RightX, state.RightY);
        // "LT"/"RT" as buttons mean a fully pressed trigger; analog values win when larger.
        var lt = Math.Max(state.LeftTrigger, state.Buttons.Contains("LT") ? 1f : 0f);
        var rt = Math.Max(state.RightTrigger, state.Buttons.Contains("RT") ? 1f : 0f);
        return new XInputReport(buttons, lx, ly, rx, ry, Trigger(lt), Trigger(rt));
    }

    /// <summary>A stick is a disc: a diagonal must never report more than full deflection.</summary>
    public static (short X, short Y) Stick(float x, float y)
    {
        var length = MathF.Sqrt(x * x + y * y);
        if (length > 1f) { x /= length; y /= length; }
        return (Axis(x), Axis(y));
    }

    private static short Axis(float v) => (short)Math.Clamp(MathF.Round(v * 32767f), -32767f, 32767f);
    private static byte Trigger(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
}
