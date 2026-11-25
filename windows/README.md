# Windows companion

Target framework: .NET 8. The companion receives authenticated UDP controller snapshots on port 26760 and optionally maps them to an Xbox 360/XInput virtual controller.

## Build

From the repository root:

```powershell
dotnet build windows/ZeroPad.sln --configuration Release
dotnet test windows/ZeroPad.sln --configuration Release
dotnet run --project windows/ZeroPad.Server
```

On first launch, the companion generates a 32-byte pairing key and prints it to the console. Enter that key and the Windows PC's private-LAN IPv4 address in the Android app. The key is stored at `%LOCALAPPDATA%\ZeroPad\pairing.key`. Keep it private. Delete that file and restart the companion to rotate the key.

Allow UDP port 26760 only on Windows Private networks if the firewall prompts. Do not expose the port to Public networks or forward it through a router.

## Virtual controller driver

The optional Xbox 360 backend uses the managed `Nefarius.ViGEm.Client` package. A separate ViGEmBus driver installation is required. The upstream driver was retired/archived in 2023, so ZeroPad does not bundle its installer and does not claim it is a long-term maintained dependency. Review [driver evaluation](../docs/driver-evaluation.md) before use.

If the driver is absent or unavailable, the server still accepts authenticated input for diagnostics but cannot expose a virtual controller to games. The console reports this condition explicitly.

## Current limitations

- Wi-Fi UDP transport and authenticated snapshots are implemented in the foundation branch, but real-device end-to-end tests are still required.
- Pairing uses a manually copied shared key; packets are authenticated but not encrypted.
- USB/ADB transport, QR pairing, a tray UI/installer, analog-pressure triggers, and automated driver installation are not implemented.
- Hardware latency and Windows 10/11 compatibility have not been certified.
