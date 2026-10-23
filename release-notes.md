## Miller Driver Updater 2.4.0

A cleaner, more professional look. No change to what gets installed or how.

- **New icon** in the window, taskbar, Start menu, installer and report.
- **Theme-matched controls:** checkboxes and the scrollbar follow light/dark mode instead of using the stock Windows 7
  look. Shorter, plainer wording throughout.
- **Publisher details:** the app and Setup now show the publisher and MIT copyright, and Settings > Apps links to the
  project page, issues and releases.
- **Report a Problem** offers to open a GitHub issue; the version number at the bottom right links to the project.

**Install:** download `MillerDriverUpdater-2.4.0-Setup.exe` below and run it. Copies on 2.2.0 or later offer this update
themselves. The installer is not code-signed yet, so Windows may show "Windows protected your PC" (*More info*, then
*Run anyway*). To confirm the file is the official build:
`gh attestation verify MillerDriverUpdater-2.4.0-Setup.exe --repo mwmiller99-dot/MillerDriverUpdater`.
