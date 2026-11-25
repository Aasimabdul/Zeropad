# Windows companion foundation

Target framework: .NET 8. This diagnostic-only prototype binds to 127.0.0.1:26760 and accepts newline-delimited protocol v1 JSON for local parser smoke tests.

It is not a production server: no pairing, authentication, Wi-Fi exposure, USB transport, full schema validation, Xbox-compatible virtual output, tray UI, or installer is included. Do not change the listener to bind to all interfaces until authenticated pairing and validation are implemented.

Build from repository root: dotnet build windows/ZeroPad.sln --configuration Release
