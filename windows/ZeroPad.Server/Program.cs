using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZeroPad.Server;

internal static class Program
{
    private const int Port = 26760;
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(1);

    public static async Task Main()
    {
        Console.WriteLine("ZeroPad Windows companion — authenticated UDP prototype");
        Console.WriteLine("Use only on a trusted private LAN. Do not forward this port to the internet.");

        var key = LoadOrCreatePairingKey();
        Console.WriteLine($"Pairing key (enter this in Android): {Convert.ToBase64String(key)}");
        Console.WriteLine($"Listening for authenticated UDP input on port {Port}.");

        VirtualController? controller = null;
        try
        {
            controller = new VirtualController();
            Console.WriteLine("Virtual Xbox 360 controller connected.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Virtual controller unavailable: {ex.GetType().Name}. Authenticated input will be diagnostic-only until a compatible driver is installed.");
        }

        using var stop = new CancellationTokenSource();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        var gate = new object();
        Guid? activeSession = null;
        long lastSequence = -1;
        var lastSeen = DateTimeOffset.MinValue;
        var inputNeutral = true;

        var watchdog = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            try
            {
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    lock (gate)
                    {
                        if (inputNeutral || DateTimeOffset.UtcNow - lastSeen <= InputTimeout) continue;
                        try { controller?.Reset(); } catch { }
                        inputNeutral = true;
                        activeSession = null;
                        lastSequence = -1;
                        Console.WriteLine("Input timeout: controller state reset.");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, stop.Token);

        try
        {
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult datagram;
                try { datagram = await udp.ReceiveAsync(stop.Token); }
                catch (OperationCanceledException) { break; }

                try
                {
                    var packet = AuthenticatedPacket.Parse(datagram.Buffer, key, DateTimeOffset.UtcNow);
                    var state = packet.State;
                    lock (gate)
                    {
                        var now = DateTimeOffset.UtcNow;
                        if (activeSession == packet.Session && state.Sequence <= lastSequence)
                            throw new FormatException("Stale sequence.");
                        if (activeSession is not null && activeSession != packet.Session &&
                            now - lastSeen <= SessionIdleTimeout)
                            throw new FormatException("Another controller session is active.");

                        activeSession = packet.Session;
                        lastSequence = state.Sequence;
                        lastSeen = now;
                        controller?.Apply(state);
                        inputNeutral = false;
                    }

                    var ackMac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"ack\n{packet.Session:D}\n{state.Sequence}"));
                    var ack = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        type = "ack",
                        session = packet.Session.ToString("D"),
                        sequence = state.Sequence,
                        mac = Convert.ToBase64String(ackMac)
                    });
                    await udp.SendAsync(ack, datagram.RemoteEndPoint, stop.Token);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or CryptographicException)
                {
                    Console.WriteLine($"Rejected input: {ex.Message}");
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"Input processing error: {ex.GetType().Name}");
                }
            }
        }
        finally
        {
            stop.Cancel();
            try { await watchdog; } catch (OperationCanceledException) { }
            lock (gate) { try { controller?.Reset(); } catch { } }
            controller?.Dispose();
        }
    }

    private static byte[] LoadOrCreatePairingKey()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroPad");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pairing.key");
        if (File.Exists(path))
        {
            var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (existing.Length != 32) throw new InvalidDataException("Pairing key file must contain a 32-byte key. Delete it to generate a new key.");
            return existing;
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(path, Convert.ToBase64String(key));
        return key;
    }
}
