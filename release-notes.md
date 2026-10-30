## Miller Driver Updater 2.4.1

- **Intel network, Wi-Fi and Bluetooth chips** now appear under *Graphics and peripherals* with Intel Driver & Support
  Assistant (**Open App**, or **Get App** to install it through winget). Windows Update often stops short of Intel's
  newest driver for a chip; Intel's own assistant knows which one Intel recommends for yours.

**Install:** download `MillerDriverUpdater-2.4.1-Setup.exe` below and run it. Copies on 2.2.0 or later offer this update
themselves. The installer is not code-signed yet, so Windows may show "Windows protected your PC" (*More info*, then
*Run anyway*). To confirm the file is the official build:
`gh attestation verify MillerDriverUpdater-2.4.1-Setup.exe --repo mwmiller99-dot/MillerDriverUpdater`.
