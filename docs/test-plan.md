# ZeroPad verification plan

This checklist distinguishes automated validation from hardware testing. A passing CI build does not imply that the phone, network, driver, and game work together.

## Automated checks

- [x] Windows solution compiles on GitHub Actions.
- [x] Windows protocol tests cover valid state, invalid version/type, negative sequence, unknown button, out-of-range axes/triggers, malformed JSON, HMAC verification, and timestamp expiry.
- [ ] Latest Android build and unit-test workflow must pass before a release.
- [ ] Add tests for transport reconnect, timeout reset, and acknowledgement authentication.

## Manual acceptance matrix

| Scenario | Required evidence | Status |
|---|---|---|
| Windows 10 x64 | App starts, driver installs, virtual Xbox controller appears in `joy.cpl`, buttons and sticks respond | Not run |
| Windows 11 x64 | Same as Windows 10; test with Secure Boot enabled | Not run |
| Wi-Fi | Phone and PC on trusted private LAN; valid key receives authenticated ACK | Not run |
| USB | Enable USB debugging, authorize device, run `adb reverse tcp:26761 tcp:26761`, connect from Android USB mode | Not run |
| Button mapping | A/B/X/Y, D-pad, LB/RB, Start/Select, LT/RT trigger values | Not run |
| Analog mapping | Full-range axes, direction, center return, diagonal behavior | Not run |
| Disconnect | Kill app, stop companion, unplug USB, disable Wi-Fi; virtual input returns to neutral within one second | Not run |
| Reconnect | Restore transport and verify state resumes without stuck inputs | Not run |
| Latency | Record at least 1000 RTT samples on Wi-Fi and USB; report median, p95, packet loss, and conditions | Not run |
| Low-end PC | Measure idle and active CPU/RAM on a 4 GB RAM Windows PC | Not run |
| Stability | 30-minute continuous input session, repeated reconnects, no crash or stuck button | Not run |

## Latency reporting

The Android diagnostic reports authenticated request/ack round-trip time. It is not a direct measurement of touch-to-game response. Do not claim "zero latency"; report a measured distribution with hardware, network, driver, and game details.

## Release gate

Do not publish a stable release until the latest Android/Windows CI passes, the driver decision is reviewed, all required manual scenarios are run on actual Windows 10/11 hardware, and failures are fixed and retested.
