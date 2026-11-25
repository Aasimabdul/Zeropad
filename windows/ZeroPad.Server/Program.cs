using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ZeroPad.Server;

internal static class Program
{
    private const int Port = 26760;
    private const int MaxLineBytes = 4096;

    public static async Task Main()
    {
        Console.WriteLine("ZeroPad Windows companion — foundation build");
        Console.WriteLine("No pairing/authentication or virtual-controller driver is configured.");
        Console.WriteLine("Listener binds to loopback only; remote Wi-Fi is disabled.");
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                _ = HandleClientAsync(client, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true))
        {
            Console.WriteLine("Diagnostic client connected.");
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line is null) break;
                if (Encoding.UTF8.GetByteCount(line) > MaxLineBytes)
                {
                    Console.WriteLine("Rejected oversized message.");
                    break;
                }
                try
                {
                    var state = ControllerState.Parse(line);
                    Console.WriteLine($"Accepted state #{state.Sequence}: {state.Buttons.Count} buttons.");
                }
                catch (JsonException) { Console.WriteLine("Rejected malformed JSON."); }
                catch (FormatException ex) { Console.WriteLine($"Rejected controller state: {ex.Message}"); }
                catch (InvalidOperationException) { Console.WriteLine("Rejected invalid field type."); }
            }
            Console.WriteLine("Diagnostic client disconnected.");
        }
    }
}
