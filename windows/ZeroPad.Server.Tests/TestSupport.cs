using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ZeroPad.Server.Tests;

/// <summary>Stands in for the ViGEm virtual pad: remembers every report the games would have seen.</summary>
internal sealed class RecordingSink : IPadSink
{
    private readonly object _gate = new();
    public List<XInputReport> Reports { get; } = new();
    public int Resets;
    public bool Disposed;
    public bool ThrowOnApply;

    public void Apply(XInputReport report)
    {
        if (ThrowOnApply) throw new InvalidOperationException("driver gone");
        lock (_gate) Reports.Add(report);
    }
    public void Reset() { lock (_gate) { Resets++; Reports.Add(default); } }
    public void Dispose() => Disposed = true;
    public XInputReport? Last { get { lock (_gate) return Reports.Count == 0 ? null : Reports[^1]; } }
    public int Count { get { lock (_gate) return Reports.Count; } }
}

internal sealed class FakeClock
{
    public DateTimeOffset Now = DateTimeOffset.UtcNow;
    public DateTimeOffset Read() => Now;
    public void Advance(TimeSpan span) => Now += span;
}

/// <summary>Builds packets byte-for-byte the way the Android app does (see protocol/README.md).</summary>
internal sealed class FakePhone
{
    public static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private readonly byte[] _key;
    public Guid Session { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public Func<long> SentAt { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public FakePhone(byte[]? key = null) { _key = key ?? TestKey; }

    public byte[] Packet(float leftX = 0, float leftY = 0, float rightX = 0, float rightY = 0, string[]? buttons = null, float lt = 0, float rt = 0, long? sequence = null)
    {
        var seq = sequence ?? Sequence++;
        var payload = new JsonObject
        {
            ["version"] = 1,
            ["type"] = "state",
            ["sequence"] = seq,
            ["buttons"] = new JsonArray((buttons ?? Array.Empty<string>()).Select(b => (JsonNode)b).ToArray()),
            ["axes"] = new JsonObject { ["leftX"] = leftX, ["leftY"] = leftY, ["rightX"] = rightX, ["rightY"] = rightY },
            ["triggers"] = new JsonObject { ["left"] = lt, ["right"] = rt },
        }.ToJsonString();
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var sentAt = SentAt();
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{Session:D}\n{sentAt}\n{payloadBase64}"));
        return Encoding.UTF8.GetBytes(new JsonObject
        {
            ["session"] = Session.ToString("D"),
            ["sentAt"] = sentAt,
            ["payload"] = payloadBase64,
            ["mac"] = Convert.ToBase64String(mac),
        }.ToJsonString());
    }

    /// <summary>Checks an acknowledgement exactly like the phone does.</summary>
    public bool AckIsValid(byte[] ack, long sequence, out bool virtualOutput)
    {
        virtualOutput = false;
        var node = JsonNode.Parse(ack)!;
        if (node["type"]?.GetValue<string>() != "ack" || node["session"]?.GetValue<string>() != Session.ToString("D")) return false;
        if (node["sequence"]?.GetValue<long>() != sequence) return false;
        virtualOutput = node["virtualOutput"]!.GetValue<bool>();
        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"ack\n{Session:D}\n{sequence}\n{(virtualOutput ? "true" : "false")}"));
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(node["mac"]!.GetValue<string>()), expected);
    }
}

internal static class Wait
{
    public static async Task<bool> Until(Func<bool> condition, int milliseconds = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    public static async Task<byte[]?> Datagram(UdpClient client, int milliseconds = 1500)
    {
        using var cts = new CancellationTokenSource(milliseconds);
        try { return (await client.ReceiveAsync(cts.Token)).Buffer; }
        catch (OperationCanceledException) { return null; }
    }
}
