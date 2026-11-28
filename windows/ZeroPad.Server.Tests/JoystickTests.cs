using Xunit;

namespace ZeroPad.Server.Tests;

/// <summary>What a game sees from the virtual Xbox pad: the stick numbers.</summary>
public sealed class JoystickTests
{
    private static XInputReport Report(float lx = 0, float ly = 0, float rx = 0, float ry = 0, params string[] buttons)
    {
        var json = "{\"version\":1,\"type\":\"state\",\"sequence\":1,\"buttons\":[" + string.Join(",", buttons.Select(b => $"\"{b}\"")) +
                   $"],\"axes\":{{\"leftX\":{lx.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"leftY\":{ly.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"\"rightX\":{rx.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"rightY\":{ry.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}," +
                   "\"triggers\":{\"left\":0,\"right\":0}}";
        return XInputMap.From(ControllerState.Parse(json));
    }

    [Fact]
    public void CentredStickReportsZero()
    {
        var r = Report();
        Assert.Equal(0, r.LeftX); Assert.Equal(0, r.LeftY); Assert.Equal(0, r.RightX); Assert.Equal(0, r.RightY);
        Assert.Equal(0, (int)r.Buttons);
    }

    [Fact]
    public void FullDeflectionHitsTheEnds()
    {
        Assert.Equal(32767, Report(lx: 1).LeftX);
        Assert.Equal(-32767, Report(lx: -1).LeftX);
        Assert.Equal(32767, Report(ry: 1).RightY);
        Assert.Equal(-32767, Report(ry: -1).RightY);
    }

    [Fact]
    public void UpIsPositiveLikeXInput()
    {
        // The phone sends +1 when the knob is pushed UP the screen; XInput also treats up as positive.
        Assert.True(Report(ly: 0.5f).LeftY > 0);
        Assert.True(Report(ly: -0.5f).LeftY < 0);
    }

    [Fact]
    public void LeftAndRightSticksAreIndependent()
    {
        var r = Report(lx: 1, ry: -1);
        Assert.Equal(32767, r.LeftX); Assert.Equal(0, r.LeftY);
        Assert.Equal(0, r.RightX); Assert.Equal(-32767, r.RightY);
    }

    [Fact]
    public void DiagonalNeverExceedsTheCircle()
    {
        var r = Report(lx: 1, ly: 1);
        var length = Math.Sqrt((double)r.LeftX * r.LeftX + (double)r.LeftY * r.LeftY);
        Assert.True(length <= 32767.5, $"diagonal length {length}");
        Assert.True(Math.Abs(r.LeftX - r.LeftY) <= 1); // still exactly 45 degrees
    }

    [Fact]
    public void FullCircleSweepIsSmoothAndInRange()
    {
        // Spin a finger around the rim of the stick, then through the middle: what a player does.
        for (var degrees = 0; degrees < 360; degrees += 5)
        {
            var a = degrees * Math.PI / 180;
            foreach (var radius in new[] { 0.25f, 0.5f, 1f, 1.4f })
            {
                var x = (float)(radius * Math.Cos(a)); var y = (float)(radius * Math.Sin(a));
                var cx = Math.Clamp(x, -1f, 1f); var cy = Math.Clamp(y, -1f, 1f); // Android clamps to [-1,1] per axis
                var r = Report(lx: cx, ly: cy);
                var length = Math.Sqrt((double)r.LeftX * r.LeftX + (double)r.LeftY * r.LeftY);
                Assert.True(length <= 32768, $"{degrees} deg r={radius}: {length}");
                if (radius <= 1f) // inside the disc: direction must match the finger
                {
                    var expectedLength = radius * 32767.0;
                    Assert.True(Math.Abs(length - expectedLength) <= 2.0, $"{degrees} deg r={radius}: {length} vs {expectedLength}");
                }
            }
        }
    }

    [Fact]
    public void EveryButtonHasItsOwnXInputBit()
    {
        var names = new[] { "A", "B", "X", "Y", "LB", "RB", "START", "SELECT", "UP", "DOWN", "LEFT", "RIGHT", "L3", "R3" };
        var masks = names.Select(n => Report(buttons: n).Buttons).ToArray();
        Assert.Equal(names.Length, masks.Distinct().Count());
        Assert.Equal(XInputMap.A, Report(buttons: "A").Buttons);
        Assert.Equal(XInputMap.Y, Report(buttons: "Y").Buttons);
        Assert.Equal(XInputMap.Back, Report(buttons: "SELECT").Buttons);
        Assert.Equal(XInputMap.DPadLeft, Report(buttons: "LEFT").Buttons);
        Assert.Equal((ushort)(XInputMap.A | XInputMap.LeftShoulder), Report(buttons: new[] { "A", "LB" }).Buttons);
    }

    [Fact]
    public void TriggerButtonsAndAnalogTriggers()
    {
        var pressed = XInputMap.From(ControllerState.Parse("{\"version\":1,\"type\":\"state\",\"sequence\":1,\"buttons\":[\"LT\"],\"axes\":{\"leftX\":0,\"leftY\":0,\"rightX\":0,\"rightY\":0},\"triggers\":{\"left\":0,\"right\":0.5}}"));
        Assert.Equal(255, pressed.LeftTrigger);
        Assert.Equal(128, pressed.RightTrigger);
    }
}
