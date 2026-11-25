using ZeroPad.Server;
using System.Text.Json.Nodes;
using System.Text;
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

    [Fact]
    public void AuthenticatedPacketAcceptsValidMac()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1795630000000);
        var session = Guid.NewGuid();
        var payload = Valid;
        var packet = BuildPacket(key, session, now.ToUnixTimeMilliseconds(), payload);
        var parsed = AuthenticatedPacket.Parse(packet, key, now);
        Assert.Equal(session, parsed.Session);
        Assert.Equal(12, parsed.State.Sequence);
    }

    [Fact]
    public void AuthenticatedPacketRejectsTamperedMac()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var now = DateTimeOffset.UtcNow;
        var session = Guid.NewGuid();
        var node = JsonNode.Parse(BuildPacket(key, session, now.ToUnixTimeMilliseconds(), Valid))!;
        var originalMac = node["mac"]!.GetValue<string>();
        node["mac"] = (originalMac[0] == 'A' ? "B" : "A") + originalMac[1..];
        var packet = Encoding.UTF8.GetBytes(node.ToJsonString());
        Assert.Throws<FormatException>(() => AuthenticatedPacket.Parse(packet, key, now));
    }

    [Fact]
    public void AuthenticatedPacketRejectsExpiredTimestamp()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var now = DateTimeOffset.UtcNow;
        var session = Guid.NewGuid();
        var packet = BuildPacket(key, session, now.ToUnixTimeMilliseconds() - 60000, Valid);
        Assert.Throws<FormatException>(() => AuthenticatedPacket.Parse(packet, key, now));
    }

    private static byte[] BuildPacket(byte[] key, Guid session, long sentAt, string stateJson)
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(stateJson));
        var signed = System.Text.Encoding.UTF8.GetBytes($"{session:D}\\n{sentAt}\\n{payload}");
        var mac = System.Security.Cryptography.HMACSHA256.HashData(key, signed);
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            session = session.ToString("D"),
            sentAt,
            payload,
            mac = Convert.ToBase64String(mac)
        });
    }

}
