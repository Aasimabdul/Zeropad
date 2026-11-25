# Windows virtual-controller driver evaluation

A Windows socket server cannot itself make games see an Xbox controller. A virtual-device driver/API is required.

## ViGEmBus / ViGEmClient

The official [ViGEmBus repository](https://github.com/nefarius/ViGEmBus) is archived and read-only. The maintainer's end-of-life statement says it and the client libraries will no longer receive updates. The driver is BSD-3-Clause; the managed `Nefarius.ViGEm.Client` NuGet package is MIT-licensed and its latest listed release is from 2023.

The project historically supports Xbox 360/XInput output on Windows 10/11, but retirement makes it a compatibility backend, not a strong long-term maintenance choice. ZeroPad integrates the managed client as an optional backend but does not bundle the driver installer. Users must install a compatible driver separately and accept its maintenance/security limitations. Driver signing, Secure Boot, install, removal, and runtime enumeration still require real-device verification.

## Alternative evaluated: LuminalVGBus

[LuminalVGBus](https://github.com/NortheBridge/LuminalVGBus) describes itself as an alpha fork. Its project page states that the driver has not yet been signed or loaded on a machine and runtime behavior is unverified. That is not suitable as a production dependency yet.

## Decision

Keep the virtual-controller backend behind an adapter. The current ViGEm adapter is optional and may fail gracefully if the driver is absent. Do not silently install kernel drivers or download drivers at runtime. Before a stable release, reevaluate maintained alternatives and complete real Windows 10/11 installation, controller enumeration, XInput game tests, signing, uninstall/rollback, security, and redistribution review.
