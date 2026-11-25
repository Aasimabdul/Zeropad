# ZeroPad protocol v1 (draft)

The protocol is transport-neutral. The initial TCP prototype uses UTF-8 JSON, one object per line, with a maximum line length of 4096 bytes.

Example state:
{
  "version": 1,
  "type": "state",
  "sequence": 42,
  "buttons": ["A", "LB", "START"],
  "axes": {"leftX": -1.0, "leftY": 0.0, "rightX": 0.25, "rightY": -0.5},
  "triggers": {"left": 0.0, "right": 1.0}
}

Validate protocol version, message type, sequence, button allowlist, finite axis values in [-1,1], and trigger values in [0,1]. Reject malformed, oversized, stale, or unsupported messages. Always neutralize controller state when a session ends.

Security: this schema does not define authentication. Do not expose an unauthenticated listener to untrusted networks. Before remote Wi-Fi use, implement explicit pairing, protected/authenticated messages, replay protection, and revocation. Breaking changes require a version increment.