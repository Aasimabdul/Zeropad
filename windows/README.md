# Windows companion

Target framework: .NET 8. The companion receives authenticated Wi-Fi UDP controller snapshots on port 26760 and authenticated USB/ADB-reverse TCP frames on loopback port 26761. It optionally maps state to an Xbox 360/XInput virtual controller.

## Get a test build

The Windows CI workflow builds and tests the solution, then publishes a self-contained Windows x64 glass-style desktop companion and its server backend as the `zeropad-windows-x64` workflow artifact. Launch `ZeroPad.Desktop.exe`, press **Start companion**, copy the pairing key, and enter the displayed PC IP plus pairing key in Android. The `server` folder must remain beside the desktop executable. Open the latest successful [Windows CI run](https://github.com/Aasimabdul/Zeropad/actions/workflows/windows.yml), select the run for the `dev/zeropad-foundation` branch, and download the artifact. It is a short-lived developer test build, not a signed installer.

## Build and run from source

From the repository root:

```powershell
dotnet build windows/ZeroPad.sln --configuration Release
dotnet test windows/ZeroPad.sln --configuration Release
dotnet run --project windows/ZeroPad.Server
```

On first launch, the companion generates a 32-byte pairing key and prints it to the console. Enter that key and the Windows PC's private-LAN IPv4 address in the Android app. The key is stored at `%LOCALAPPDATA%\ZeroPad\pairing.key`. Keep it private. Delete that file and restart the companion to rotate the key.

Allow UDP port 26760 only on Windows Private networks if the firewall prompts. Do not expose the port to Public networks or forward it through a router.

## USB mode

Enable USB debugging on Android, connect the cable, authorize the computer, and run:

```powershell
adb reverse tcp:26761 tcp:26761
```

Select USB in the Android connection panel. The USB path is TCP forwarded by ADB to a loopback-only listener; it is not native USB HID. The ADB command must be re-established when needed.

## Virtual controller driver

The optional Xbox 360 backend uses the managed `Nefarius.ViGEm.Client` package. A separate ViGEmBus driver installation is required. The upstream driver was retired/archived in 2023, so ZeroPad does not bundle its installer and does not claim it is a long-term maintained dependency. Review [driver evaluation](../docs/driver-evaluation.md) before use.

If the driver is absent or unavailable, the server still accepts authenticated input for diagnostics but cannot expose a virtual controller to games. The console reports this condition explicitly.

## Current limitations

- Wi-Fi and USB/ADB-reverse transport code has not yet been verified on physical devices.
- Pairing uses a manually copied shared key; packets are authenticated but not encrypted.
- QR pairing, tray UI/installer, analog-pressure triggers, and automated driver installation are not implemented.
- Hardware latency and Windows 10/11 compatibility have not been certified.
