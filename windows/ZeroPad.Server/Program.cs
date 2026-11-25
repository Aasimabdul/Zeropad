using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZeroPad.Server;

internal static class Program
{
    private const int WifiPort = 26760;
    private const int UsbTcpPort = 26761;
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(1);

    public static async Task Main()
    {
        Console.WriteLine("ZeroPad Windows companion — authenticated Wi-Fi + ADB-reverse prototype");
        Console.WriteLine("Use only on a trusted private LAN. Do not forward UDP port 26760 to the internet.");

        var key = LoadOrCreatePairingKey();
        Console.WriteLine($"Pairing key (enter this in Android): {Convert.ToBase64String(key)}");
        Console.WriteLine($"Wi-Fi: authenticated UDP on port {WifiPort}.");
        Console.WriteLine($"USB: run adb reverse tcp:{UsbTcpPort} tcp:{UsbTcpPort} with USB debugging enabled.");

        VirtualController? controller = null;
        try
        {
            controller = new VirtualController();
            Console.WriteLine("Virtual Xbox 360 controller connected.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Virtual controller unavailable: {ex.GetType().Name}. Input will be diagnostic-only until a compatible driver is installed.");
        }

        using var stop = new CancellationTokenSource();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, WifiPort));
        var tcpListener = new TcpListener(IPAddress.Loopback, UsbTcpPort);
        tcpListener.Start();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        var gate = new object();
        Guid? activeSession = null;
        long lastSequence = -1;
        var lastSeen = DateTimeOffset.MinValue;
        var inputNeutral = true;

        byte[] ProcessPacket(byte[] bytes)
        {
            var packet = AuthenticatedPacket.Parse(bytes, key, DateTimeOffset.UtcNow);
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
                try { controller?.Apply(state); }
                catch (Exception ex)
                {
                    Console.WriteLine($"Virtual controller failed: {ex.GetType().Name}; disabling virtual output.");
                    try { controller?.Dispose(); } catch { }
                    controller = null;
                }
                inputNeutral = false;
            }

            var virtualOutput = controller is not null;
            var ackMac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"ack\n{packet.Session:D}\n{state.Sequence}\n{virtualOutput.ToString().ToLowerInvariant()}"));
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "ack",
                session = packet.Session.ToString("D"),
                sequence = state.Sequence,
                mac = Convert.ToBase64String(ackMac),
                virtualOutput
            });
        }

        var tcpTask = AcceptTcpClientsAsync(tcpListener, ProcessPacket, stop.Token);
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
                    var ack = ProcessPacket(datagram.Buffer);
                    await udp.SendAsync(ack, datagram.RemoteEndPoint, stop.Token);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or CryptographicException)
                {
                    Console.WriteLine($"Rejected Wi-Fi input: {ex.Message}");
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"Wi-Fi input processing error: {ex.GetType().Name}");
                }
            }
        }
        finally
        {
            stop.Cancel();
            tcpListener.Stop();
            try { await watchdog; } catch (OperationCanceledException) { }
            try { await tcpTask; } catch (OperationCanceledException) { } catch (SocketException) { }
            lock (gate) { try { controller?.Reset(); } catch { } }
            controller?.Dispose();
        }
    }

    private static async Task AcceptTcpClientsAsync(TcpListener listener, Func<byte[], byte[]> processPacket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleTcpClientAsync(client, processPacket, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private static async Task HandleTcpClientAsync(TcpClient client, Func<byte[], byte[]> processPacket, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line is null) break;
                    if (Encoding.UTF8.GetByteCount(line) > AuthenticatedPacket.MaximumBytes)
                    {
                        Console.WriteLine("Rejected oversized USB/ADB frame.");
                        break;
                    }

                    try
                    {
                        var ack = processPacket(Encoding.UTF8.GetBytes(line));
                        await writer.WriteLineAsync(Encoding.UTF8.GetString(ack));
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or CryptographicException)
                    {
                        Console.WriteLine($"Rejected USB/ADB input: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (IOException) { }
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
