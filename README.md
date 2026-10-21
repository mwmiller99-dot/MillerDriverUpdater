# Miller Driver Updater (MDU)

One scan for everything on a Windows PC that can be updated from an official source: device drivers, GPU drivers,
peripheral firmware/software, and installed programs. Since 2.0 it is a real Windows program,
`MillerDriverUpdater.exe`, with no PowerShell involved anywhere. (Until 2.1 it was called *Miller Driver Update*;
installing 2.1 replaces the old version and keeps its reports, driver backups and weekly check.)

It is deliberately **not** a Driver Easy clone under the hood. Driver Easy's product is a private driver database; this
tool only uses sources you can trust:

| Source | What it covers |
|---|---|
| Windows Update driver catalog (WUA API) | Signed drivers for chipset, audio, network, USB, input, some firmware |
| winget | Installed programs, and the vendor apps themselves |
| nvidia.com's own driver service | The newest GeForce driver, downloaded straight from NVIDIA and signature-checked - no NVIDIA App, no account |
| Vendor apps (AMD Adrenalin, Intel DSA, G HUB, Synapse, iCUE, SteelSeries GG, NGENUITY...) | AMD/Intel GPU drivers and peripheral firmware (these makers publish no service the app could use) |

## Install it (what to tell friends)

Either:

- **Setup:** double-click `MillerDriverUpdater-2.2.0-Setup.exe`, then Next, Next, Install. It installs for your user
  account only (`%LOCALAPPDATA%\Programs\Miller Driver Updater`), puts **Miller Driver Updater** in the Start menu, offers
  a desktop shortcut, and can switch on the Monday check. Uninstall from Settings > Apps.
- **Portable:** just run `MillerDriverUpdater.exe` from anywhere. It is the whole app (about 300 KB).

If Windows says "Windows protected your PC", click *More info*, then *Run anyway*: that screen appears for every program
that is not code-signed. Some antivirus programs (Malwarebytes, for example) quarantine new unsigned programs
on a machine-learning guess ("MachineLearning/Anomalous"). Setup notices when that happens, changes nothing, and says
what to do: restore the exe from the antivirus' quarantine and always allow it, then run Setup again. Report it to the
vendor as a false positive too. Code signing is the real fix (see below).

Say Yes to the "make changes to your device" prompt when the app opens: installing drivers needs it.

## Use it

Open it. One UAC prompt, then it scans on its own: devices, Windows Update, winget and nvidia.com are checked **at the
same time**, so a scan takes about as long as Windows Update's search alone (around 10 s on a fast PC, up to a couple of
minutes on a slow one). The headline shows the percentage, paced by how long each step took on this PC last time.

- Drivers come ticked (optional ones unticked); apps are listed but unticked. **Click anywhere on a row** to tick it,
  or use **Check all / Uncheck all** on the card. **Enter** installs what is ticked, **F5** checks again.
- Click **Install N Selected**. Every row shows its own progress bar with size, speed and percent; the bar under the
  header (and the taskbar icon) shows the whole run. The window flashes in the taskbar when a scan or install finishes.
- **Before you start** appears at the top when something is likely to get in the way, each with a fix button: no
  network, Windows Update service switched off, winget missing, System Protection off, a restart pending, on battery,
  low disk space, a metered connection. Nothing in it blocks you.
- **Needs attention** lists devices Windows cannot drive. When an update on the same screen covers one, it says so;
  otherwise **Look Up Driver** searches the Microsoft Update Catalog for it.
- **Graphics and peripherals** names each device by the product name it reports ("Logitech G502 X", not "USB
  Composite Device") with an **Open App** button for its vendor app, or **Get App** (installs it through winget, or
  opens the vendor's page) when the app is missing.
- **Roll Back** appears on every driver this app replaced; earlier backups are listed in their own card.
- **Check weekly and notify me** (bottom) registers a silent Monday-morning check that shows a notification only when
  something is found; clicking the notification opens the app.
- **Report a Problem** (bottom right) zips the app's logs and the latest report onto the desktop for sending.
- It follows your Windows light/dark setting and accent colour, dark title bar included.

Errors come out in plain words ("Another installation is already running - wait for it to finish, then try again")
with the raw message after "Details:". Nothing restarts the PC on its own.

## It updates itself

When a newer version is published on the project's GitHub Releases page, a one-line banner appears at the bottom of
the window ("Miller Driver Updater 2.3.0 is available") with **What's New** and **Update**. The weekly check mentions it
in its notification too. Update:

1. downloads `MillerDriverUpdater-<version>-Setup.exe` from that release (github.com only),
2. checks it against the SHA-256 checksum GitHub lists for the file - and, once releases are code-signed, the
   publisher signature - and deletes it if anything is off,
3. closes the app and runs the Setup quietly; Setup keeps the current exe until the new one is known to run (an
   antivirus that removes the new one gets the old one put back), then opens the new version.

Nothing updates without that click, and never during an install. The repository is set in `Updater.Repo`
(app/Updater.cs); while it is empty, no check is made.

Publishing a version: bump it (Core.cs, app.manifest, installer/mdu.nsi), write `release-notes.md`, run `release.cmd`.
It builds, tests, makes the Setup and `SHA256SUMS.txt`, and creates the GitHub release with the GitHub CLI
(`winget install --id GitHub.cli --exact`, then `gh auth login` once). Without the CLI it prints the two-minute manual
steps for github.com.

## What Install does, in order

1. Creates a system restore point (or notes the one Windows made today). If that fails it asks whether to continue.
2. Backs up the current driver of every device about to be updated (`pnputil /export-driver`).
3. Installs the Windows Update drivers - reusing the scan's results, so there is no second 1-3 minute search.
4. Downloads the NVIDIA driver from nvidia.com, checks NVIDIA Corporation's signature, unpacks only the driver parts
   (7-Zip, installed through winget if missing) and runs NVIDIA's setup silently. Deletes the 1 GB package afterwards.
5. Upgrades the ticked programs through winget, one at a time, inside a hidden console so winget's live download counter
   shows. Each app is held to a watchdog: no output and no download growth for 6 minutes (4 if its installer opened a
   window and is waiting for a click) and its process tree is stopped, the row says why, and the run moves on.
   Apps that update themselves (Battle.net, Steam, Discord, browsers, Spotify...) are never run through winget; they
   get an **Open** button instead. "Version unknown" rows are left out of Check all and run last.
6. Shows a **Restart to finish** banner when needed. It never restarts for you.

## Command line

```
MillerDriverUpdater.exe                   the window
MillerDriverUpdater.exe --check           quiet scan; a notification when something is found (what the weekly task runs)
MillerDriverUpdater.exe --schedule        register the weekly check (--unschedule removes it)
MillerDriverUpdater.exe --support-bundle  zip the logs onto the desktop
MillerDriverUpdater.exe --no-elevate      the window without the UAC prompt (scanning works, installing does not)
MillerDriverUpdater.exe --ping            start and exit (Setup uses it to check an antivirus lets the exe run)
```

Reports, logs, scan timings and driver backups live in `%LOCALAPPDATA%\Miller Driver Updater\` (`reports\mdu-*.html`
opens in a browser; `reports\mdu.log` records how each step went).

## Build it

```
build.cmd
```

Compiles `dist\MillerDriverUpdater.exe` with the C# compiler that ships inside Windows (.NET Framework 4.8 - present on
every Windows 10 1903+ and Windows 11 PC), then runs the tests. No Visual Studio, no SDK, no downloads. The code is
C# 5 because that is what that compiler speaks. `build.cmd notest` skips the tests.

The Setup.exe needs NSIS 3 (`winget install --id NSIS.NSIS --exact`). After `build.cmd`:
`cd installer` then `"%ProgramFiles(x86)%\NSIS\makensis.exe" mdu.nsi` → `dist\MillerDriverUpdater-2.2.0-Setup.exe`
(it packs `dist\MillerDriverUpdater.exe`).

## Layout

```
app/Program.cs          entry point: single instance, UAC relaunch, --check / --schedule / --support-bundle
app/MainWindow.cs       the window (WPF, built in code) + MainWindow.xaml (chrome and styles, embedded resource)
app/Scanner.cs          the parallel scan and its honest percentage
app/InstallJob.cs       restore point -> backups -> Windows Update -> NVIDIA -> winget, with per-row progress
app/Inventory.cs        devices, problem devices, GPUs, peripherals (vendor maps live here)
app/WindowsUpdate.cs    WUA search/download/install with live progress
app/Winget.cs           winget table parser, upgrade with pseudo-console progress and watchdog
app/Nvidia.cs           nvidia.com lookup, download, signature check, driver-only install
app/Drivers.cs          restore point, pnputil backup/rollback
app/Preflight.cs        "Before you start" checks + plain-language errors
app/Report.cs           HTML/JSON report, support bundle, weekly task, notification
app/Updater.cs          self-update: GitHub Releases check, download, SHA-256/signature check, Setup /UPDATE handoff
release.cmd             build + Setup + checksums + GitHub release; release-notes.md is its text
app/Native.cs           ConPTY, process trees, WinVerifyTrust, SetupAPI, window helpers, WUA callbacks
app/Core.cs             app identity, data folder, logging, formatting, process helper
app/app.manifest        asInvoker, Windows 10/11, per-monitor DPI
tests/Tests.cs          tests for the pure logic (run by build.cmd)
installer/mdu.nsi       NSIS script for MillerDriverUpdater-<version>-Setup.exe
assets/                 MDU icon (.ico is compiled into the exe)
```

## Honest limits

- Peripheral firmware: the tool reads the version your mouse/keyboard/headset reports, but only the vendor's own app knows if a newer one exists. There is no public API for that, so the tool gets you to the app rather than guessing.
- AMD and Intel GPUs: neither publishes a driver-lookup service like NVIDIA's, so those still go through the vendor app.
- Bluetooth headphones/earbuds update through their phone app.
- BIOS/UEFI updates come from your motherboard or PC vendor's tool.

## Giving it to other people

Send the Setup.exe (or just the exe). Two things to know:

- Without a code-signing certificate, SmartScreen warns once and some antivirus heuristics may quarantine the exe.
  Signing removes both; Azure Trusted Signing is the cheapest route for an individual (about $10/month).
- When a friend says "it doesn't work", ask for the zip from **Report a Problem**: `mdu.log`, `gui-error.log`, the
  last report, and a `system.txt` with Windows/winget versions, pre-flight findings and the last scan's timings.
