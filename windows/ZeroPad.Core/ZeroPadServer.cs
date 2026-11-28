using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZeroPad.Server;

/// <summary>Wi-Fi (UDP) and USB/ADB-reverse (loopback TCP) listeners feeding one ControllerHost.</summary>
public sealed class ZeroPadServer : IDisposable
{
    public const int DefaultWifiPort = 26760;
    public const int DefaultUsbPort = 26761;

    private readonly ControllerHost _host;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly UdpClient _udp;
    private readonly TcpListener _tcp;
    private readonly List<Task> _tasks = new();

    public int WifiPort { get; }
    public int UsbPort { get; }

    /// <param name="wifiPort">0 picks a free port (tests).</param>
    public ZeroPadServer(ControllerHost host, int wifiPort = DefaultWifiPort, int usbPort = DefaultUsbPort, Action<string>? log = null)
    {
        _host = host;
        _log = log ?? (_ => { });
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, wifiPort));
        _tcp = new TcpListener(IPAddress.Loopback, usbPort);
        try { _tcp.Start(); }
        catch { _udp.Dispose(); throw; }
        WifiPort = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        UsbPort = ((IPEndPoint)_tcp.LocalEndpoint).Port;
    }

    public void Start()
    {
        _tasks.Add(Task.Run(WatchdogAsync));
        _tasks.Add(Task.Run(UdpLoopAsync));
        _tasks.Add(Task.Run(AcceptLoopAsync));
    }

    private static bool IsRejection(Exception ex) =>
        ex is JsonException or FormatException or ArgumentException or InvalidOperationException or CryptographicException;

    private async Task WatchdogAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try { while (await timer.WaitForNextTickAsync(_stop.Token)) _host.Tick(); }
        catch (OperationCanceledException) { }
    }

    private async Task UdpLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult datagram;
            try { datagram = await _udp.ReceiveAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (_stop.IsCancellationRequested) { return; }
            catch (SocketException) { continue; } // e.g. ICMP "port unreachable" echoes on Windows

            try
            {
                var ack = _host.Process(datagram.Buffer);
                await _udp.SendAsync(ack, datagram.RemoteEndPoint, _stop.Token);
            }
            catch (Exception ex) when (IsRejection(ex)) { _log($"Rejected Wi-Fi input: {ex.Message}"); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex) { _log($"Wi-Fi input processing error: {ex.GetType().Name}"); }
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _tcp.AcceptTcpClientAsync(_stop.Token);
                lock (_tasks) _tasks.Add(Task.Run(() => HandleClientAsync(client)));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            client.NoDelay = true;
            var line = new List<byte>(512);
            var buffer = new byte[1024];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0) break;
                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] != (byte)'\n') { line.Add(buffer[i]); }
                        else
                        {
                            var frame = line.ToArray();
                            line.Clear();
                            if (frame.Length > 0 && frame[^1] == (byte)'\r') frame = frame[..^1];
                            if (frame.Length == 0) continue;
                            try
                            {
                                var ack = _host.Process(frame);
                                var reply = new byte[ack.Length + 1];
                                ack.CopyTo(reply, 0);
                                reply[^1] = (byte)'\n';
                                await stream.WriteAsync(reply, _stop.Token);
                            }
                            catch (Exception ex) when (IsRejection(ex)) { _log($"Rejected USB/ADB input: {ex.Message}"); }
                        }
                        if (line.Count > AuthenticatedPacket.MaximumBytes)
                        {
                            _log("Rejected oversized USB/ADB frame.");
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _tcp.Stop();
        _udp.Dispose();
        Task[] all;
        lock (_tasks) all = _tasks.ToArray();
        try { Task.WaitAll(all, TimeSpan.FromSeconds(2)); } catch { }
        _stop.Dispose();
        _host.Shutdown();
    }
}
