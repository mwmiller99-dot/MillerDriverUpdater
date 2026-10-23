# Changelog

## 2.4.0
- **New look.** A new icon (a chip with an up arrow) in the window, taskbar, installer and HTML report, drawn from one
  design by `tools\ArtGen.cs`. Checkboxes and the scrollbar now match the light/dark theme instead of the stock
  Windows 7 controls. Shorter, plainer wording throughout; the "Needs attention" count is amber, not red.
- **Proper publisher details.** The exe and Setup name the publisher and copyright (MIT) instead of a slogan; Settings
  > Apps links to the project's page, issues and releases. The installer pictures carry the current name.
- **Report a Problem** offers to open a GitHub issue and says to share the log zip privately. The version number at the
  bottom right links to the project.
- README: screenshots (made-up sample data, light and dark), an "Is it safe?" section, release badges.

## 2.3.0
- **Hide an update.** Every driver and app update has a quiet **Hide** link; the NVIDIA driver has **Skip this
  version**. The choice is kept where it belongs: drivers are hidden in Windows Update itself (so Windows does not
  install them on its own either - the way to keep a bad driver away after Roll Back), apps are pinned in winget, and
  an NVIDIA version is skipped until a newer one comes out. A **Hidden updates** card lists them with **Unhide**.
- **History.** A collapsed card lists what the app installed, rolled back or hid on this PC, newest first.
- **What's new** link on the NVIDIA driver row (nvidia.com's release notes for that driver).
- Repository: MIT license, security policy, changelog, contributing guide, issue forms.

## 2.2.1
- Setup no longer looks stuck when an antivirus removes the app: it pauses on a Retry/Cancel message (allow the
  folder, click Retry) instead of stopping on its progress page.

## 2.2.0
- Updates itself from GitHub Releases: a banner offers a newer version; the download is checked against GitHub's
  SHA-256 checksum, installed quietly, and the app reopens. If an antivirus removes the new version, the current one is
  kept.

## 2.1.0
- Renamed from *Miller Driver Update* to **Miller Driver Updater**. Installing over an older version keeps its reports,
  driver backups and weekly check.

## 2.0.0
- Rewritten as a native Windows program (C#, .NET Framework 4.8, WPF): one exe, no PowerShell.
- Devices, Windows Update, winget and nvidia.com are checked at the same time (scan in about the time of Windows
  Update's search alone).
- Peripherals named by the product name they report; click anywhere on a row to tick it; Enter installs, F5 checks
  again; taskbar progress; single instance; winget's live download progress fixed when a console is attached.

## 1.x
- The original PowerShell version (Miller Driver Update): Windows Update drivers, winget, nvidia.com driver installs,
  restore points, per-driver backup and Roll Back, pre-flight checks, weekly check.
