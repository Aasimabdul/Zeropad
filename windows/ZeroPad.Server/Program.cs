using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZeroPad.Server;

internal static class Program
{
    private const int Port = 26760;
    private const int MaxDatagramBytes = 4096;
    private static readonly TimeSpan MessageLifetime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(1);

    public static async Task Main()
    {
        Console.WriteLine("ZeroPad Windows companion — Wi-Fi transport prototype");
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
            Console.WriteLine($"Virtual controller unavailable: {ex.GetType().Name}. Install the separately licensed driver only if you accept its maintenance status.");
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
                UdpReceiveResult packet;
                try { packet = await udp.ReceiveAsync(stop.Token); }
                catch (OperationCanceledException) { break; }

                if (packet.Buffer.Length == 0 || packet.Buffer.Length > MaxDatagramBytes)
                {
                    Console.WriteLine("Rejected empty or oversized datagram.");
                    continue;
                }

                try
                {
                    var envelope = JsonDocument.Parse(packet.Buffer);
                    using (envelope)
                    {
                        var root = envelope.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Envelope must be an object.");
                        var sessionText = RequiredString(root, "session");
                        if (!Guid.TryParseExact(sessionText, "D", out var session)) throw new FormatException("Invalid session ID.");
                        var sentAt = RequiredInt64(root, "sentAt");
                        var payload = RequiredString(root, "payload");
                        var suppliedMac = Convert.FromBase64String(RequiredString(root, "mac"));

                        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if (Math.Abs(now - sentAt) > (long)MessageLifetime.TotalMilliseconds)
                            throw new FormatException("Expired message.");

                        var signedBytes = Encoding.UTF8.GetBytes($"{session:D}\n{sentAt}\n{payload}");
                        var expectedMac = HMACSHA256.HashData(key, signedBytes);
                        if (suppliedMac.Length != expectedMac.Length ||
                            !CryptographicOperations.FixedTimeEquals(suppliedMac, expectedMac))
                            throw new FormatException("Authentication failed.");

                        var stateJson = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                        var state = ControllerState.Parse(stateJson);

                        lock (gate)
                        {
                            var nowUtc = DateTimeOffset.UtcNow;
                            if (activeSession == session && state.Sequence <= lastSequence)
                                throw new FormatException("Stale sequence.");
                            if (activeSession is not null && activeSession != session &&
                                nowUtc - lastSeen <= SessionIdleTimeout)
                                throw new FormatException("Another controller session is active.");

                            activeSession = session;
                            lastSequence = state.Sequence;
                            lastSeen = nowUtc;
                            controller?.Apply(state);
                            inputNeutral = false;
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or CryptographicException)
                {
                    Console.WriteLine($"Rejected input: {ex.Message}");
                }
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

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new FormatException($"Missing or invalid field: {name}");
        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            throw new FormatException($"Missing or invalid field: {name}");
        return number;
    }
}
