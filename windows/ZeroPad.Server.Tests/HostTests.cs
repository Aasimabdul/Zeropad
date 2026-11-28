using Xunit;

namespace ZeroPad.Server.Tests;

public sealed class HostTests
{
    private static (ControllerHost host, RecordingSink sink, FakeClock clock, FakePhone phone) Make()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink();
        var host = new ControllerHost(FakePhone.TestKey, sink, clock.Read);
        return (host, sink, clock, new FakePhone { SentAt = () => clock.Now.ToUnixTimeMilliseconds() });
    }

    [Fact]
    public void AppliesStateAndReturnsAValidAck()
    {
        var (host, sink, _, phone) = Make();
        var ack = host.Process(phone.Packet(leftX: 1, buttons: new[] { "A" }));
        Assert.Equal(32767, sink.Last!.Value.LeftX);
        Assert.Equal(XInputMap.A, sink.Last!.Value.Buttons);
        Assert.True(phone.AckIsValid(ack, 0, out var virtualOutput));
        Assert.True(virtualOutput);
    }

    [Fact]
    public void RejectsStaleAndReplayedSequences()
    {
        var (host, sink, _, phone) = Make();
        host.Process(phone.Packet(sequence: 5));
        Assert.Throws<FormatException>(() => host.Process(phone.Packet(sequence: 5)));
        Assert.Throws<FormatException>(() => host.Process(phone.Packet(sequence: 4)));
        host.Process(phone.Packet(sequence: 6));
        Assert.Equal(2, sink.Count);
    }

    [Fact]
    public void ReplayAfterTimeoutIsStillRejected()
    {
        var (host, sink, clock, phone) = Make();
        var captured = phone.Packet(rightX: 1, sequence: 1);
        host.Process(captured);
        clock.Advance(TimeSpan.FromSeconds(2));
        host.Tick(); // controller released
        var before = sink.Count;
        Assert.Throws<FormatException>(() => host.Process(captured));
        Assert.Equal(before, sink.Count);
    }

    [Fact]
    public void WrongKeyNeverReachesTheController()
    {
        var (host, sink, _, _) = Make();
        var intruder = new FakePhone(Enumerable.Repeat((byte)7, 32).ToArray());
        Assert.Throws<FormatException>(() => host.Process(intruder.Packet(leftX: 1)));
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void OnlyOnePhoneAtATime()
    {
        var (host, sink, clock, first) = Make();
        var second = new FakePhone { SentAt = () => clock.Now.ToUnixTimeMilliseconds() };
        host.Process(first.Packet(leftX: 1));
        Assert.Throws<FormatException>(() => host.Process(second.Packet(leftX: -1)));
        Assert.Equal(32767, sink.Last!.Value.LeftX);
        clock.Advance(TimeSpan.FromSeconds(4)); // first phone went quiet
        host.Process(second.Packet(leftX: -1));
        Assert.Equal(-32767, sink.Last!.Value.LeftX);
    }

    [Fact]
    public void ReleasesEverythingWhenInputStops()
    {
        var (host, sink, clock, phone) = Make();
        host.Process(phone.Packet(leftX: 1, leftY: 1, buttons: new[] { "A", "RB" }, rt: 1));
        clock.Advance(TimeSpan.FromMilliseconds(900));
        host.Tick();
        Assert.Equal(0, sink.Resets); // still within the 1 s window
        clock.Advance(TimeSpan.FromMilliseconds(200));
        host.Tick();
        Assert.Equal(1, sink.Resets);
        Assert.Equal(default, sink.Last!.Value);
        host.Tick();
        Assert.Equal(1, sink.Resets); // only once
    }

    [Fact]
    public void ReconnectAfterTimeoutWorksWithTheSameSession()
    {
        var (host, sink, clock, phone) = Make();
        host.Process(phone.Packet(leftX: 1));
        clock.Advance(TimeSpan.FromSeconds(5));
        host.Tick();
        host.Process(phone.Packet(leftX: 0.5f)); // phone woke up again, sequence keeps counting
        Assert.Equal(16384, sink.Last!.Value.LeftX);
    }

    [Fact]
    public void DriverFailureDisablesOutputButKeepsAcknowledging()
    {
        var (host, sink, _, phone) = Make();
        sink.ThrowOnApply = true;
        var ack = host.Process(phone.Packet(leftX: 1));
        Assert.True(sink.Disposed);
        Assert.True(phone.AckIsValid(ack, 0, out var virtualOutput));
        Assert.False(virtualOutput);
        Assert.False(host.VirtualOutput);
    }

    [Fact]
    public void WorksWithoutAnyDriver()
    {
        var clock = new FakeClock();
        var host = new ControllerHost(FakePhone.TestKey, null, clock.Read);
        var phone = new FakePhone { SentAt = () => clock.Now.ToUnixTimeMilliseconds() };
        var ack = host.Process(phone.Packet(leftX: 1));
        Assert.True(phone.AckIsValid(ack, 0, out var virtualOutput));
        Assert.False(virtualOutput);
    }

    [Fact]
    public void GoldenVectorsMatchTheAndroidApp()
    {
        // Same constants live in android/.../ProtocolTest.kt. If either side changes the protocol, both tests fail.
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var signed = System.Text.Encoding.UTF8.GetBytes("00000000-0000-0000-0000-000000000001\n1795630000000\neyJ2IjoxfQ==");
        Assert.Equal("h4Etjz3TmB668vST5Fs5EpIVWSE8viQINRB5HCAxF5s=", Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(key, signed)));
        var ack = System.Text.Encoding.UTF8.GetBytes("ack\n00000000-0000-0000-0000-000000000001\n7\ntrue");
        Assert.Equal("7NwqBjtPUkGCAeJ8ztt1iCpG4b4xnh1c1ijuIP2UaFQ=", Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(key, ack)));
    }
}
