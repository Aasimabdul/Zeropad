# ZeroPad protocol v1 (draft)

The controller state model is transport-neutral. Wi-Fi uses authenticated UDP datagrams on port **26760**. USB cable mode uses newline-framed authenticated JSON over TCP on Windows loopback port **26761**, reached from Android via `adb reverse tcp:26761 tcp:26761`. Both paths validate the same envelope and state payload.

## Pairing key

On first launch, Windows generates a random 32-byte key and saves its Base64 representation under the current user's Local AppData folder. It prints the key for manual entry into Android. Treat it as a password. Reset pairing by stopping the companion and deleting `%LOCALAPPDATA%\ZeroPad\pairing.key`, then restarting it.

Wi-Fi requires a trusted private LAN. Packets are authenticated, not encrypted; never port-forward UDP 26760 to the public internet. USB mode requires Android USB debugging, ADB platform-tools, and the reverse mapping command above.

## Authenticated envelope

The envelope is UTF-8 JSON, maximum 4096 bytes:

```json
{
  "session": "random-guid",
  "sentAt": 1795630000000,
  "payload": "BASE64_OF_UTF8_STATE_JSON",
  "mac": "BASE64_HMAC_SHA256"
}
```

HMAC-SHA256 covers UTF-8 bytes of `session + newline + sentAt + newline + payload`, using the 32-byte pairing key. The payload field is Base64 of the state JSON. Windows verifies the MAC in constant time, rejects timestamps outside ±10 seconds, and rejects stale sequence numbers within the active session. Only one active controller session is accepted at a time. A valid message is acknowledged with a MAC-authenticated response.

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

The host responds with `type=ack`, the same session ID and state sequence, `virtualOutput` (whether a virtual controller backend is currently available), and a Base64 HMAC-SHA256 of `ack + newline + session + newline + sequence + newline + virtualOutput`, where the boolean is lowercase `true` or `false`. Android uses the authenticated acknowledgement to display host response status and network round-trip time.

Breaking changes require a version increment and compatibility notes.
