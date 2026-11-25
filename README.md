# ZeroPad

**ZeroPad** is an open-source Android-to-Windows virtual gamepad project. The goal is a lightweight Android controller and Windows companion that can expose input as an Xbox-compatible virtual controller.

> **Development status: foundation stage.** The repository now contains an Android Compose touch-preview scaffold, a Windows .NET diagnostic listener, a draft protocol, and CI build definitions. End-to-end input transport, USB, authenticated Wi-Fi pairing, virtual Xbox output, and real-device testing are not implemented or verified. This branch is not yet a usable gamepad release.

## Repository layout

- `android/` — Kotlin + Jetpack Compose client, application ID `com.simbuild.zeropad`
- `windows/` — C#/.NET Windows companion foundation
- `protocol/` — versioned, transport-neutral controller state format
- `docs/` — architecture and driver evaluation
- `.github/workflows/` — Android and Windows CI

## Planned features

- Analog sticks, D-pad, ABXY, bumpers, triggers, Start/Select
- Customizable control layout, connection screen, diagnostics, and reconnection
- Wi-Fi and USB transports sharing one protocol
- Secure pairing, range validation, and state reset on disconnect
- Windows virtual-controller backend using a maintained and legally redistributable driver
- Minimal black-and-white design and low idle resource usage

## Build

Android requires JDK 17, Android SDK platform 35, and Gradle 8.11.1 or compatible. From the repository root, run `gradle -p android assembleDebug`.

Windows requires the .NET 8 SDK. Run `dotnet build windows/ZeroPad.sln --configuration Release`.

CI compilation is not proof of transport functionality, virtual-device support, latency, or Windows 10/11 hardware compatibility.

## Security and driver notes

The current Windows listener binds to loopback only and is diagnostic-only. It has no pairing/authentication and must not be exposed to a network. Read [architecture](docs/architecture.md), [protocol](protocol/README.md), and [driver evaluation](docs/driver-evaluation.md) before implementing network access or redistributing a driver.

The repository currently uses GPL-3.0. Third-party driver binaries and installers have separate licensing and redistribution conditions; do not bundle them without verification.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). No feature should be described as working until it has been implemented and tested on its target hardware and OS.
