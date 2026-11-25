# ZeroPad architecture

## Components

- **Android client:** Kotlin + Jetpack Compose; captures touch input, manages connection settings, and sends normalized controller snapshots.
- **Wi-Fi transport:** authenticated UDP snapshots on port 26760.
- **USB transport:** newline-framed TCP to Windows loopback port 26761 through ADB reverse; requires USB debugging and platform-tools.
- **Protocol:** versioned, bounded controller-state payloads. Both endpoints validate protocol version, sequence, ranges, message size, HMAC, timestamp, and session.
- **Windows companion:** C#/.NET process; manages the pairing key, validates messages, detects stale input, resets state on timeout, and reports diagnostics.
- **Virtual-device backend:** optional adapter around ViGEmClient. It requires a separately installed ViGEmBus driver, which is retired; see [driver evaluation](driver-evaluation.md).

## Data flow

Android touch controls -> normalized snapshot -> HMAC-authenticated UDP (Wi-Fi) or line-framed TCP (ADB reverse USB) -> Windows envelope/state validation -> XInput mapping -> virtual-device backend.

## Reliability and security requirements

- Send complete snapshots at 20 Hz; changes cause the sender to restart the keepalive loop so updates are sent promptly.
- Include a random session ID, timestamp, sequence, protocol version, and HMAC.
- Verify HMAC in constant time and reject stale timestamps, sequences, malformed data, and out-of-range controls.
- Clear all held buttons and reset axes/triggers after one second without valid input.
- Only one controller session is active at a time.
- Pairing key is stored in the user's Local AppData and copied manually into Android. Traffic is authenticated but not encrypted; use a trusted private LAN only.
- Never expose UDP port 26760 through router forwarding or to public networks.
- RTT is measured from authenticated request/ack; it is not the same as end-to-end touch-to-game latency.

## USB limitations

USB mode relies on ADB reverse rather than a native USB HID device. Enable USB debugging, install Android platform-tools on Windows, connect the phone, then run `adb reverse tcp:26761 tcp:26761`. ADB availability, Android device authorization, and real-cable behavior still require testing.

## Current scope

Wi-Fi and ADB-reverse transport paths, HMAC validation, an optional virtual-device adapter, and protocol tests are implemented in the foundation branch. QR pairing, analog-pressure triggers, tray UI, installer, and hardware certification remain outstanding.
