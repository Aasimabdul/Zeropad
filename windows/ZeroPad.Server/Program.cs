using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ZeroPad.Server;

internal static class Program
{
    public static int Main()
    {
        Console.Title = "ZeroPad Companion";
        try { return Run(); }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(ex is SocketException
                ? "Could not open the ZeroPad ports (26760 UDP / 26761 TCP). Is ZeroPad already running?"
                : $"ZeroPad stopped: {ex.Message}");
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
            return 1;
        }
    }

    private static int Run()
    {
        Console.WriteLine("ZeroPad Companion 0.1.0");
        Console.WriteLine("Use only on a trusted private network. Never forward UDP 26760 to the internet.");
        Console.WriteLine();

        var key = PairingKey.LoadOrCreate();
        IPadSink? pad = null;
        try
        {
            pad = new VirtualController();
            Console.WriteLine("Virtual Xbox 360 controller: READY (check joy.cpl).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Virtual Xbox 360 controller: NOT AVAILABLE ({ex.GetType().Name}).");
            Console.WriteLine("Install the ViGEmBus driver, then restart ZeroPad: https://github.com/nefarius/ViGEmBus/releases");
            Console.WriteLine("Until then input is accepted for diagnostics only; games will not see a controller.");
        }

        var host = new ControllerHost(key, pad, log: line => Console.WriteLine(line));
        using var server = new ZeroPadServer(host, log: line => Console.WriteLine(line));
        server.Start();

        Console.WriteLine();
        Console.WriteLine("Enter these in the ZeroPad phone app:");
        var addresses = LocalAddresses();
        Console.WriteLine($"  PC address : {(addresses.Count > 0 ? string.Join("  or  ", addresses) : "(no network found)")}");
        Console.WriteLine($"  Pairing key: {Convert.ToBase64String(key)}");
        Console.WriteLine();
        Console.WriteLine($"Wi-Fi: UDP {server.WifiPort}. USB: run  adb reverse tcp:{server.UsbPort} tcp:{server.UsbPort}");
        Console.WriteLine("Press Ctrl+C to quit.");

        using var quit = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
        quit.Wait();
        return 0;
    }

    private static List<string> LocalAddresses()
    {
        var list = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address) && !ua.Address.ToString().StartsWith("169.254."))
                    list.Add(ua.Address.ToString());
        }
        return list;
    }
}
