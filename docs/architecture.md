# ZeroPad architecture

Status: foundation only. This document does not claim working end-to-end controller output.

Android (Kotlin/Compose) captures controller state. A transport-neutral protocol carries normalized state to a C#/.NET Windows companion. The Windows companion validates messages, maps controls, and forwards state to a separately maintained virtual-device backend.

## Reliability requirements
- Send complete controller snapshots with sequence numbers and protocol version.
- Clear all held buttons and reset axes/triggers on disconnect, timeout, or malformed sessions.
- Bound message size and validate every button and numeric range.
- Keep transport, mapping, and virtual-device backends independent.
- Log lifecycle and errors, never secrets or authentication material.

## USB caveat
A USB cable does not automatically provide an arbitrary phone-to-PC data channel. USB device/accessory mode, Android restrictions, Windows behavior, and hardware need real testing. USB transport is not implemented in this foundation.

## Virtual controller
Windows games need a compatible virtual-device driver/API; a TCP listener alone cannot create an Xbox-compatible controller. Evaluate maintenance, Windows 10/11 compatibility, license, redistribution rights, signing, admin requirements, and uninstall behavior before bundling a driver.