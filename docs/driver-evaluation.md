# Virtual controller driver evaluation

A Windows socket server cannot itself make games see an Xbox controller. A virtual-device driver/API is required.

Before choosing or redistributing a backend, verify current maintenance, Windows 10/11 support, Xbox/XInput output, license of the API and installer, redistribution permission, driver signing, administrator requirements, Secure Boot behavior, architecture support, security advisories, and uninstall/rollback path.

ViGEmBus is a known historical option, but its project has been retired; do not adopt it by default just because older projects use it. This foundation does not bundle any driver. No Xbox-compatible virtual output is claimed until a supported backend is implemented and tested.