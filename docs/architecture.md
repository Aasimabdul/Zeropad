# ZeroPad architecture

## Components

- **Android client:** Kotlin + Jetpack Compose; captures touch input, manages connection settings, and sends normalized controller snapshots.
- **Wi-Fi transport:** authenticated UDP snapshots using a manually paired 32-byte HMAC-SHA256 key, session ID, timestamp, and monotonically increasing sequence number.
- **Protocol:** versioned, bounded controller-state payloads. Both endpoints validate protocol version, sequence, input ranges, and message size.
- **Windows companion:** C#/.NET process; manages the pairing key, validates authenticated datagrams, detects stale input, resets state on timeout, and reports diagnostics.
- **Virtual-device backend:** optional adapter around ViGEmClient. It requires a separately installed ViGEmBus driver, which is retired; see [driver evaluation](driver-evaluation.md).

## Data flow

Android touch controls -> normalized controller snapshot -> HMAC-authenticated UDP -> Windows envelope and state validation -> XInput mapping -> virtual-device backend.

## Reliability and security requirements

- Send complete snapshots at 20 Hz; changes cause the sender to restart the keepalive loop so updates are sent promptly.
- Include a random session ID, timestamp, sequence, protocol version, and HMAC.
- Verify HMAC in constant time and reject stale timestamps, sequences, malformed data, and out-of-range controls.
- Clear all held buttons and reset axes/triggers after one second without valid input.
- Only one controller session is active at a time.
- Pairing key is stored in the user's Local AppData and copied manually into Android. Traffic is authenticated but not encrypted; use a trusted private LAN only.
- Never expose the port through router forwarding or to public networks.

## USB caveat

A USB cable does not automatically provide an arbitrary phone-to-PC data channel. ADB reverse forwarding is a practical design candidate, but Android developer options, ADB availability, and Windows platform-tools need testing. USB transport is not implemented.

## Current scope

Wi-Fi snapshot sending and Windows HMAC validation plus an optional virtual-device adapter are implemented in the foundation branch. USB, QR pairing, desktop tray UI, installer, full diagnostics, latency measurement, and hardware certification remain outstanding.
