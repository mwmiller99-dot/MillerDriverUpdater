## Miller Driver Updater 2.3.0

- **Hide an update.** Every driver and app update has a small **Hide** link; the NVIDIA driver has **Skip this
  version**. The choice is kept by the source itself: a driver is hidden in Windows Update (so Windows won't install it
  on its own either - handy after a Roll Back), an app is pinned in winget, and an NVIDIA version is skipped until a
  newer one comes out. A **Hidden updates** card lists them with **Unhide**.
- **History.** A card lists what the app installed, rolled back or hid on this PC, newest first.
- **What's new** on the NVIDIA driver row opens nvidia.com's release notes for that driver.
- The repository now has a security policy, a changelog, a contributing guide and issue forms.

Install: download `MillerDriverUpdater-2.3.0-Setup.exe` below and run it. Windows may say "Windows protected your PC":
click *More info*, then *Run anyway* (the app is not code-signed yet). Copies on 2.2.0 or later offer this update themselves.
