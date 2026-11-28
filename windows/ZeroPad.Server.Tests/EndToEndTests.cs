using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace ZeroPad.Server.Tests;

/// <summary>Real sockets, real server loops, a fake phone and a recording pad instead of ViGEm.</summary>
public sealed class EndToEndTests
{
    private static (ZeroPadServer server, RecordingSink sink, List<string> log) Start()
    {
        var sink = new RecordingSink();
        var log = new List<string>();
        var host = new ControllerHost(FakePhone.TestKey, sink);
        var server = new ZeroPadServer(host, wifiPort: 0, usbPort: 0, log: line => { lock (log) log.Add(line); });
        server.Start();
        return (server, sink, log);
    }

    [Fact]
    public async Task WifiStickDragReachesTheVirtualPad()
    {
        var (server, sink, _) = Start();
        using var _s = server;
        using var udp = new UdpClient();
        udp.Connect(IPAddress.Loopback, server.WifiPort);
        var phone = new FakePhone();

        // Drag the left stick from the centre out to the right edge in 10 steps, like a thumb.
        for (var step = 0; step <= 10; step++)
        {
            var x = step / 10f;
            var seq = phone.Sequence;
            await udp.SendAsync(phone.Packet(leftX: x));
            var ack = await Wait.Datagram(udp);
            Assert.NotNull(ack);
            Assert.True(phone.AckIsValid(ack!, seq, out var virtualOutput));
            Assert.True(virtualOutput);
            Assert.Equal((short)Math.Round(x * 32767f), sink.Last!.Value.LeftX);
        }
        // Let go: the app sends a centred stick.
        await udp.SendAsync(phone.Packet());
        Assert.NotNull(await Wait.Datagram(udp));
        Assert.Equal(0, sink.Last!.Value.LeftX);
    }

    [Fact]
    public async Task BothSticksAndButtonsAtOnce()
    {
        var (server, sink, _) = Start();
        using var _s = server;
        using var udp = new UdpClient();
        udp.Connect(IPAddress.Loopback, server.WifiPort);
        var phone = new FakePhone();
        await udp.SendAsync(phone.Packet(leftX: -1, rightY: 1, buttons: new[] { "A", "RB" }, rt: 1));
        Assert.NotNull(await Wait.Datagram(udp));
        var r = sink.Last!.Value;
        Assert.Equal(-32767, r.LeftX);
        Assert.Equal(32767, r.RightY);
        Assert.Equal((ushort)(XInputMap.A | XInputMap.RightShoulder), r.Buttons);
        Assert.Equal(255, r.RightTrigger);
    }

    [Fact]
    public async Task SixtyUpdatesPerSecondKeepUp()
    {
        var (server, sink, _) = Start();
        using var _s = server;
        using var udp = new UdpClient();
        udp.Connect(IPAddress.Loopback, server.WifiPort);
        var phone = new FakePhone();
        for (var i = 0; i < 120; i++) // two seconds of a circling thumb at 60 Hz
        {
            var a = i * 6 * Math.PI / 180;
            await udp.SendAsync(phone.Packet(leftX: (float)Math.Cos(a), leftY: (float)Math.Sin(a)));
            await Task.Delay(16);
        }
        Assert.True(await Wait.Until(() => sink.Count >= 118), $"only {sink.Count} of 120 reached the pad");
    }

    [Fact]
    public async Task StopSendingAndThePadReleases()
    {
        var (server, sink, _) = Start();
        using var _s = server;
        using var udp = new UdpClient();
        udp.Connect(IPAddress.Loopback, server.WifiPort);
        var phone = new FakePhone();
        await udp.SendAsync(phone.Packet(leftX: 1, buttons: new[] { "B" }));
        Assert.NotNull(await Wait.Datagram(udp));
        Assert.Equal(32767, sink.Last!.Value.LeftX);
        // Phone vanishes (app killed, Wi-Fi lost): within ~1 s the stick must come back to centre.
        Assert.True(await Wait.Until(() => sink.Resets >= 1, 2500), "controller was not released");
        Assert.Equal(default, sink.Last!.Value);
    }

    [Fact]
    public async Task ForgedAndGarbagePacketsAreIgnored()
    {
        var (server, sink, log) = Start();
        using var _s = server;
        using var udp = new UdpClient();
        udp.Connect(IPAddress.Loopback, server.WifiPort);
        await udp.SendAsync(new FakePhone(Enumerable.Repeat((byte)9, 32).ToArray()).Packet(leftX: 1));
        await udp.SendAsync(Encoding.UTF8.GetBytes("not json"));
        await udp.SendAsync(new byte[5000]);
        Assert.Null(await Wait.Datagram(udp, 500)); // no ack for any of them
        Assert.Equal(0, sink.Count);
        // ...and a genuine packet still works afterwards.
        var phone = new FakePhone();
        await udp.SendAsync(phone.Packet(leftY: 1));
        Assert.NotNull(await Wait.Datagram(udp));
        Assert.Equal(32767, sink.Last!.Value.LeftY);
        Assert.True(log.Count >= 2);
    }

    [Fact]
    public async Task UsbTcpPathWorksToo()
    {
        var (server, sink, _) = Start();
        using var _s = server;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.UsbPort);
        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var phone = new FakePhone();
        for (var i = 0; i < 5; i++)
        {
            var seq = phone.Sequence;
            var bytes = phone.Packet(rightX: i / 4f);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes) + "\n"));
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(line);
            Assert.True(phone.AckIsValid(Encoding.UTF8.GetBytes(line!), seq, out _));
            Assert.Equal((short)Math.Round(i / 4f * 32767f), sink.Last!.Value.RightX);
        }
    }

    [Fact]
    public async Task OversizedUsbFrameDropsTheConnection()
    {
        var (server, sink, _) = Start();
        using var _s = server;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.UsbPort);
        using var stream = tcp.GetStream();
        await stream.WriteAsync(new byte[6000].Select(_ => (byte)'x').ToArray());
        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, read); // server closed us
        Assert.Equal(0, sink.Count);
    }
}
