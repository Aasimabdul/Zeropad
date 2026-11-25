# Contributing to ZeroPad

Keep changes focused, lightweight, and testable. Preserve separation between Android, Windows, and protocol layers.

Do not commit pairing secrets, private keys, personal network addresses, or sensitive logs. Do not claim support unless implemented and tested on the relevant hardware and OS.

Validation:
- Android: build with Gradle and run unit tests.
- Windows: build/test with a supported .NET SDK.
- Protocol: test malformed messages, out-of-range values, disconnects, and compatibility.
- Hardware claims require actual hardware evidence; CI compilation alone is not hardware validation.

Pull requests should state purpose, implementation details, tests actually run, limitations, and user-visible changes.