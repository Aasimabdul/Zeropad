# ZeroPad

**ZeroPad** is an open-source Android-to-Windows gamepad project. The Android client uses Kotlin + Jetpack Compose; the Windows companion is C#/.NET 8. The intended output is an Xbox 360/XInput-compatible virtual controller.

> **Status: early development prototype.** Android touch controls, authenticated Wi-Fi UDP snapshots, an ADB-reverse USB/TCP path, Windows state validation, an optional ViGEm adapter, RTT display, and protocol tests are present. End-to-end phone-to-PC behavior still needs real-device testing. QR pairing, analog-pressure triggers, tray UI/installer, and hardware certification are not complete.

## Project layout

- `android/` — Kotlin + Jetpack Compose client (`com.simbuild.zeropad`)
- `windows/` — .NET 8 Windows companion and optional virtual controller backend
- `windows/ZeroPad.Server.Tests/` — protocol and authentication tests
- `protocol/` — versioned controller state and authenticated message envelope
- `docs/` — architecture, reference research, and driver evaluation
- `.github/workflows/` — Android and Windows CI

## Current prototype features

- Press-and-release D-pad and ABXY/shoulder controls
- Drag-based analog sticks and input diagnostics
- Wi-Fi: authenticated UDP snapshots on port 26760
- USB cable: ADB reverse TCP path on loopback port 26761 (requires USB debugging and platform-tools)
- Random 32-byte pairing key, HMAC-SHA256 authentication, timestamp/session/sequence checks, authenticated acknowledgements
- Windows validation of controller fields and input timeout reset
- Network round-trip-time display (not total touch-to-game latency)
- Optional Xbox 360 virtual-controller adapter through ViGEmClient
- Automated Android compile and Windows build/protocol-test workflows

## Build

Android requires JDK 17 and Android SDK platform 35. From the repository root:

```bash
gradle -p android assembleDebug
```

Windows requires the .NET 8 SDK:

```powershell
dotnet build windows/ZeroPad.sln --configuration Release
dotnet test windows/ZeroPad.sln --configuration Release
dotnet run --project windows/ZeroPad.Server
```

The Windows companion generates a pairing key on first run and prints it to the console. Enter the Windows PC's private-LAN IP and key in Android. Keep the key private and use a trusted network only. The protocol authenticates packets but does not encrypt them; do not expose UDP port 26760 to the public internet.

For USB, enable USB debugging and run `adb reverse tcp:26761 tcp:26761` on Windows after connecting the phone. Select USB in the Android connection panel.

## Virtual controller driver

The optional backend requires a separate ViGEmBus driver installation. ViGEmBus is retired/archived; ZeroPad does not bundle its installer. Review [driver evaluation](docs/driver-evaluation.md) and [reference research](docs/reference-research.md). If the driver is unavailable, authenticated packets can be diagnosed but games will not see a virtual controller.

## Known limitations

- USB requires ADB and developer options; it is not native USB HID and needs physical-device verification.
- Trigger controls are currently on/off, not analog pressure.
- No end-to-end latency benchmark or 4 GB PC performance results have been measured.
- Wi-Fi/USB/driver behavior has not been certified on physical Windows 10/11 systems.
- A green CI build is compilation/test evidence only; it is not hardware certification.

## License and contribution

The repository uses GPL-3.0. Third-party dependencies retain their own licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [CONTRIBUTING.md](CONTRIBUTING.md).
