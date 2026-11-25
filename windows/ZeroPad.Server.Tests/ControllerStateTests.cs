using ZeroPad.Server;
using Xunit;

namespace ZeroPad.Server.Tests;

public sealed class ControllerStateTests
{
    private const string Valid = """
        {
          "version": 1, "type": "state", "sequence": 12,
          "buttons": ["A", "LB"],
          "axes": {"leftX": -1, "leftY": 0, "rightX": 0.5, "rightY": 1},
          "triggers": {"left": 0, "right": 1}
        }
        """;

    [Fact]
    public void ParsesValidStateAndBounds()
    {
        var state = ControllerState.Parse(Valid);
        Assert.Equal(12, state.Sequence);
        Assert.Contains("A", state.Buttons);
        Assert.Equal(-1f, state.LeftX);
        Assert.Equal(1f, state.RightTrigger);
    }

    [Theory]
    [InlineData("""{"version":2,"type":"state","sequence":0,"buttons":[],"axes":{"leftX":0,"leftY":0,"rightX":0,"rightY":0},"triggers":{"left":0,"right":0}}""")]
    [InlineData("""{"version":1,"type":"hello","sequence":0,"buttons":[],"axes":{"leftX":0,"leftY":0,"rightX":0,"rightY":0},"triggers":{"left":0,"right":0}}""")]
    [InlineData("""{"version":1,"type":"state","sequence":-1,"buttons":[],"axes":{"leftX":0,"leftY":0,"rightX":0,"rightY":0},"triggers":{"left":0,"right":0}}""")]
    [InlineData("""{"version":1,"type":"state","sequence":0,"buttons":["EVIL"],"axes":{"leftX":0,"leftY":0,"rightX":0,"rightY":0},"triggers":{"left":0,"right":0}}""")]
    [InlineData("""{"version":1,"type":"state","sequence":0,"buttons":[],"axes":{"leftX":2,"leftY":0,"rightX":0,"rightY":0},"triggers":{"left":0,"right":0}}""")]
    [InlineData("""{"version":1,"type":"state","sequence":0,"buttons":[],"axes":{"leftX":0,"leftY":0,"rightX":0,"rightY":0},"triggers":{"left":0,"right":1.1}}""")]
    public void RejectsInvalidStates(string json)
    {
        Assert.Throws<FormatException>(() => ControllerState.Parse(json));
    }

    [Fact]
    public void RejectsMalformedJson()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ControllerState.Parse("{"));
    }
}
