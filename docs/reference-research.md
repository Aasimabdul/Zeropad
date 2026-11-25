# Reference project and driver research

Research checked on 2026-11-25. This is an engineering review, not an endorsement of any third-party project.

## Open-source reference projects

- [OpenGamepad](https://github.com/Alexprox7/OpenGamepad): useful architectural reference for a split Android client/PC companion, transport separation, protocol documentation, and ADB reverse forwarding for USB. Its published README describes a Windows companion that uses ViGEmBus.
- [PadConnect](https://github.com/PadConnectApp/PadConnect): useful reference for Kotlin + Jetpack Compose touch controls and UDP state streaming. Its documented Windows path also depends on ViGEmBus.
- ZeroPad is being implemented independently. Do not copy source code or assets without following each project's license and attribution terms. These references are used for architecture review only.

## Virtual Xbox driver assessment

### ViGEmBus / ViGEmClient
- The official [ViGEmBus repository](https://github.com/nefarius/ViGEmBus) is archived and read-only; the maintainer's [end-of-life statement](https://docs.nefarius.at/projects/ViGEm/End-of-Life/) says the driver/client are retired and will receive no further updates.
- The driver repository lists BSD-3-Clause licensing and Windows 10/11 support for its final releases. The managed [Nefarius.ViGEm.Client NuGet package](https://www.nuget.org/packages/Nefarius.ViGEm.Client/) is MIT-licensed, but its latest listed release is from 2023.
- A retired driver can still work on some machines, but it is not a strong long-term default for a new project that prioritizes maintenance and security. Its installer, signing requirements, update behavior, and driver runtime must be tested separately.

### LuminalVGBus
- [LuminalVGBus](https://github.com/NortheBridge/LuminalVGBus) describes itself as an alpha fork. Its project page states that its driver has not yet been signed or loaded on a machine and runtime behavior is unverified. That is not suitable as a production dependency yet.

## Decision

No third-party driver binary is bundled, and no driver backend is claimed as supported by this foundation. The driver integration remains a release gate. A candidate must pass a real Windows 10/11 install, controller enumeration, XInput game test, uninstall/rollback, signing, security, and license/redistribution review before it is selected for releases.

## ZeroPad implementation rules

- Keep a virtual-controller interface so a driver can be replaced without rewriting transport or UI code.
- Do not download drivers at runtime or silently install kernel drivers.
- Clearly tell the user when a separate driver is required and provide an uninstall path.
- Do not enable remote input until authenticated pairing and message validation are implemented.
