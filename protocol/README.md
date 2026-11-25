# ZeroPad protocol v1 (draft)

The protocol is transport-neutral at the state-model level. Current Wi-Fi transport uses authenticated UDP datagrams on port **26760**. USB is not implemented.

## Pairing key

On first launch, the Windows companion generates a random 32-byte key and saves its Base64 representation under the current user's Local AppData folder. It prints the key once per launch for manual entry into the Android app. Treat it as a password. Reset pairing by stopping the companion and deleting `%LOCALAPPDATA%\ZeroPad\pairing.key`, then restarting it.

The server listens on all local interfaces but requires a valid HMAC-SHA256 on every packet. Use a trusted private LAN, allow the port only on Windows Private networks, and never port-forward it to the public internet. Packets are authenticated, not encrypted; this protocol is intended for a trusted LAN, not an untrusted network.

## Datagram envelope

The UDP datagram is UTF-8 JSON, maximum 4096 bytes:

```json
{
  "session": "random-guid",
  "sentAt": 1795630000000,
  "payload": "BASE64_OF_UTF8_STATE_JSON",
  "mac": "BASE64_HMAC_SHA256"
}
```

The HMAC is computed over the UTF-8 bytes of `session + newline + sentAt + newline + payload`, using the 32-byte pairing key. The `payload` field is Base64 of the state JSON. Windows verifies the MAC in constant time, rejects timestamps outside ±10 seconds, and rejects stale sequence numbers within the active session. Only one active controller session is accepted at a time. A valid message is acknowledged with a MAC-authenticated response.

## State payload

```json
{
  "version": 1,
  "type": "state",
  "sequence": 42,
  "buttons": ["A", "LB", "START"],
  "axes": {
    "leftX": -1.0,
    "leftY": 0.0,
    "rightX": 0.25,
    "rightY": -0.5
  },
  "triggers": {"left": 0.0, "right": 1.0}
}
```

- `version` must equal 1 and `type` must equal `state`.
- `sequence` is a non-negative monotonically increasing integer per session.
- Allowed buttons: `A, B, X, Y, LB, RB, LT, RT, START, SELECT, UP, DOWN, LEFT, RIGHT, L3, R3`. Duplicate/unknown buttons are rejected.
- All four axes are required finite numbers in [-1, 1].
- Both triggers are required finite numbers in [0, 1].
- Missing fields, invalid types, unsupported versions, malformed JSON, and oversized messages are rejected.
- If input stops for one second, Windows resets the virtual controller to neutral.

## Acknowledgement

The host responds with `type=ack`, the same session ID and state sequence, and a Base64 HMAC-SHA256 of `ack + newline + session + newline + sequence`. The Android client uses the authenticated acknowledgement to display host response status.

## Versioning

Breaking changes require a protocol version increment and compatibility notes. USB transport must reuse the same state model and validation rules.
