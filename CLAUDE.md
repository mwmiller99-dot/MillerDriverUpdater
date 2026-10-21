# Miller Driver Updater — project brief

One-stop Windows tool that scans this PC and updates everything in one pass: device drivers, GPU drivers, peripheral firmware/software (keyboard, mouse, headset), and installed software. Think Driver Easy, but built only on official sources.

## The one rule that shapes everything

Driver Easy's actual product is its proprietary driver database. We are NOT building a driver database and NOT scraping vendor websites. Every update this tool applies must come from one of these official channels:

1. **Windows Update's driver catalog** (via the Windows Update Agent COM API) — signed drivers for chipset, audio, network, USB, input devices, etc.
2. **winget** — for software updates and for installing/updating the vendor apps themselves.
3. **A vendor's own public driver service**, when one exists: nvidia.com's `lookupValueSearch` product table +
   `AjaxDriverService.php?func=DriverManualLookup` (what the nvidia.com download page itself calls), with the package
   fetched from `*.nvidia.com` and its Authenticode signature (NVIDIA Corporation) verified before it runs.
4. **Vendor apps** (AMD Adrenalin, Intel DSA, Logitech G Hub, Razer Synapse, Corsair iCUE, SteelSeries GG, HyperX NGENUITY, ...) — for everything without such a service. No public API drives their update flows, so: detect the hardware → make sure the matching app is installed → launch it and tell the user why.

The value we add is the single pane: one scan, one report, one "apply" step, with safety rails. We're orchestrating, not replacing.

## What the scan can and can't determine

| Category | Current version | Latest available | Apply |
|---|---|---|---|
| Device drivers (chipset, audio, network, USB, input) | Yes — `Win32_PnPSignedDriver` | Yes — Windows Update API | Yes |
| GPU driver | Yes | NVIDIA: nvidia.com; others: WU (usually older) / vendor app | NVIDIA direct; WU; otherwise launch vendor app |
| Installed software | Yes — winget | Yes — winget | Yes |
| Firmware Windows Update ships as driver packages (DriverClass `Firmware`) | Yes | Yes | Yes |
| Peripheral firmware (the chip inside a mouse/keyboard/headset) | Partly — `REV_xxxx` (bcdDevice) | **No** — only the vendor's own app knows | Launch vendor app |

Never scrape or reverse-engineer vendor update servers.

## Target & stack (2.0, 2026-10-21: rewritten from PowerShell to a native exe)

- Windows 10 (1903+) / 11, 64-bit. **One file: `MillerDriverUpdater.exe`**, C# on .NET Framework 4.8 (preinstalled on every
  supported Windows, so nothing to install). WPF window. No PowerShell anywhere — the user asked for "an actual
  program", not something connected to PowerShell.
- **Built with the csc.exe inside Windows** (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) by `build.cmd`.
  That compiler only speaks **C# 5**: no `$""` interpolation, `?.`, `nameof`, expression-bodied members, auto-property
  initializers, `out var`, tuples, pattern matching, local functions, `catch when`, `await` in catch/finally. Use
  `string.Format`, plain properties/fields, lambdas assigned to `Action`/`Func`. Optional/named args, LINQ, `dynamic`,
  async/await are fine. Old csc also fails on very long paths (CS2021) — keep build paths short.
- No XAML compilation: `app/MainWindow.xaml` is an embedded resource loaded with `XamlReader.Load`; cards/rows are built
  in code. Icon is `/win32icon` + embedded `Mdu.mdu.ico`. `[assembly: TargetFramework(".NETFramework,Version=v4.8")]` is
  set by hand in Core.cs (csc does not stamp it; without it the runtime applies 4.0 quirks such as old TLS defaults).
- Manifest: `asInvoker` (the weekly `--check` task runs unelevated); the window relaunches itself with `runas` once at
  launch. Per-monitor-v2 DPI, Common Controls v6.
- Data: `%LOCALAPPDATA%\Miller Driver Updater\` → `reports\` (mdu-*.html/json, mdu.log, gui-error.log,
  scan-timing.json, driver-backups\), `downloads\` (NVIDIA package; deleted after a successful install).
- Layout: see README "Layout". `tests/Tests.cs` compiles against the exe (`/r:dist\MillerDriverUpdater.exe`) and runs from
  `build.cmd`; keep it passing and add a case for every parsing bug found on real output.
- **Name history**: 1.x (PowerShell) and 2.0 were "Miller Driver Update" (`MillerDriverUpdate.exe`, task "Miller Driver
  Update - weekly check", data `%LOCALAPPDATA%\Miller Driver Update`, install `Programs\Miller Driver Update`). 2.1
  renamed everything to **Miller Driver Updater** (owner's choice; MDU initials unchanged). Carry-over lives in
  `AppInfo.OldName/OldTaskNames/AllBackupDirs/MigrateOldData` (2.0 data folder moved on first run; old backup folders
  still listed for Roll Back), `Schedule.CheckAndRepair`/`Register` (old-named task replaced), and mdu.nsi (old program
  files, shortcuts, uninstall key removed; old reports kept). Keep those until nobody runs 1.x/2.0.

## Status (2026-10-21)

2.0.0 built and verified on a Windows 11 test PC with an NVIDIA GPU and USB peripherals: 95 tests pass; real scan
about 10 s (parallel); nvidia.com lookup (newer driver found and sized); winget table (kinds right); problem device
(a GPU's HDMI audio with no driver, code 28 → "Fix is below"); peripheral product names via SetupAPI; window renders and
selection works (rendered headless through a dev harness); WinVerifyTrust on an NVIDIA-signed exe; ConPTY capture and
winget's live "x MB / y MB" counter (via `winget download`); Start-menu app lookup; schtasks XML create/delete; support
bundle zip.
**Not verified (needs a real install run, elevated):** WUA BeginDownload/BeginInstall with the WuaSink callbacks,
restore point creation via WMI, pnputil export/restore, NVIDIA setup switches/exit codes and 7-Zip layout, a winget
upgrade end to end through the watchdog, the UAC relaunch, the `--check` tray notification, running the installer.
The installer IS built (NSIS 3.13 from winget, x86 stub — the Windows NSIS build has no amd64 stubs, so mdu.nsi picks
the target by which stub exists; paths in mdu.nsi must use backslashes there, `File "../x"` finds nothing): 211 KB,
contents checked with 7-Zip (exe byte-identical to dist, Read me first, README, Uninstall.exe).
**Antivirus:** a consumer antivirus's machine-learning heuristic (reported as `MachineLearning/Anomalous`) quarantines
freshly built, unsigned exes on first launch, on copy, and right after Setup writes or runs them; a folder allow-list
entry did not reliably stop it, and restoring from quarantine does not add an allow entry (Malwarebytes keeps
them in RtpConfig.json `rtpExclusions`). Microsoft Defender did not flag it. Setup runs `--ping` and checks the exe
survived before touching an earlier version; if not it pauses on Retry/Cancel (2.2.1: 2.2.0 just stopped, which looked
like a stuck download): Retry copies the exe in again and pings again, Cancel puts any `.old` back. Real fix: code signing (Azure Trusted
Signing) and false-positive reports to the vendor. Do not tweak the binary to dodge a detector.
2.1.0 (rename): built and tested the same way.
2.2.0 (self-update): `Updater.cs` + update banner + Setup `/UPDATE`; 109 tests pass (14 for release parsing); download +
SHA-256 + signature check verified live against a real GitHub release asset (and a tampered digest refused); banner
layout rendered. `Updater.Repo` = `mwmiller99-dot/MillerDriverUpdater` (public; source + releases). Not verified:
Setup's /UPDATE path (wait, .old keep/restore, relaunch) end to end. Commits use the account's GitHub noreply
address, never a personal email; never commit reports, logs, screenshots or anything that names a real PC.

## Safety rails (non-negotiable)

- Nothing installs until Install is clicked and confirmed. Scanning never changes anything.
- Restore point before any driver install (WMI `SystemRestore.CreateRestorePoint`, type 12, event 100). If it fails, ask
  before continuing.
- Per-driver backup (`pnputil /export-driver`) before Windows Update replaces a driver; Roll Back on the row.
- Only drivers Windows Update offers, or the NVIDIA package after its NVIDIA Corporation signature verifies. Never a
  driver from an arbitrary URL.
- Log every action to `reports\mdu.log`; JSON + HTML report per scan.
- Never auto-reboot. Show the restart banner.

## How it works (what was learned building 1.x, kept in 2.0)

- **Scan** (`Scanner.cs`): three branches at once — main (pre-flight → `Win32_PnPSignedDriver` + `Win32_PnPEntity` →
  GPUs/peripherals → nvidia.com), wu (WUA search), winget (`winget upgrade` → verify winget ids of missing vendor apps).
  Percent = elapsed / (elapsed + longest remaining branch), each phase's remaining time falling linearly to 10% of its
  expected time, then decaying; expected times learned in `scan-timing.json` (0.3 old + 0.7 actual).
- **Devices**: a device with no driver comes back named by its class word ("MEDIA") — `Inventory.HardwareDescription`
  names it from the hardware id (case-SENSITIVE `^[A-Z0-9]{3,}$` test; 1.x used PowerShell's case-insensitive match and
  would have renamed "Volume" etc.). Code 22 (disabled on purpose) is not a problem device. Peripherals use the
  device's own product string (`DEVPKEY_Device_BusReportedDeviceDesc` via SetupAPI) when Windows says "USB Composite
  Device".
- **Problem hints**: WU driver by hardware-id prefix; HDAUDIO VEN_10DE → the NVIDIA driver update; 1002/8086/10EC → vendor.
  Catalog lookup queries `bus\VEN_xxxx&DEV_xxxx` only.
- **Windows Update**: GPU display drivers from WU are deferred when nvidia.com answered or the GPU vendor app is installed.
  Install reuses the scan's IUpdate objects (all WUA calls on MTA thread-pool threads), re-searching once only if that
  fails. Progress: `BeginDownload/BeginInstall` with `WuaSink` (DISPID 0 Invoke + the four callback interfaces, fixed
  IIDs verified in HKCR\Interface), polling `GetProgress()` every 400 ms; download = first half of a row, install = second.
- **winget**: newer winget prints no progress to a pipe, so upgrades run in a hidden ConPTY (`PtyProcess`).
  `STARTF_USESTDHANDLES` with null handles is required — without it a child of a console process writes to that
  console instead of the pseudo-console (1.x had this bug whenever it ran with a console). Fallback: pipe + watching
  `%TEMP%\WinGet` grow. Watchdog (`Winget.Upgrade`): idle 360 s, window-waiting 240 s, max 1500 s → kill tree, exit -3,
  "[mdu] stopped: why". Rows tagged self / unknown / normal (`Winget.ResolveKind`); self-updating apps get Open, never run.
  HRESULT exit codes → words (`Errors.WingetExitText`, 0x8A15xxxx table), installer's own code wins when printed.
  If the table cuts ids with "…", the listing is re-run in a 400-column pseudo-console.
- **NVIDIA**: product table → psid/pfid (exact, notebook-tolerant, else generic 929/120) → DriverManualLookup JSON
  (Version, DownloadURL, Name, DownloadURLFileSize). Download host must be `*.nvidia.com`. 7-Zip extracts
  Display.Driver/HDAudio/NVI2/PhysX + setup.cfg minus EULA/consent entries → `setup.exe -passive -noreboot -noeula
  -nofinish -s`; exit 0 ok, 1 ok+reboot.
- **Weekly check**: scheduled task "Miller Driver Updater - weekly check" (schtasks /XML, user, LeastPrivilege,
  Mondays 10:00) runs `MillerDriverUpdater.exe --check`, which scans and shows a tray-balloon notification (click opens
  the app). The window re-points an existing task (from 1.x/2.0, under either name) at the running exe
  (`Schedule.CheckAndRepair`, only when the exe is named MillerDriverUpdater.exe, so test harnesses change nothing).
- **Window UX**: whole row toggles its checkbox; Enter = primary button, F5 = Check Again; only one "Check Again" visible;
  taskbar progress + flash when done; closing during an install asks first; single instance (second launch brings the
  window forward without a UAC prompt); after an install, rows not installed are re-enabled; restore-point failure →
  "install drivers anyway?" retries exactly the drivers that were skipped (1.x lost them).
- **Self-update** (`Updater.cs`): `api.github.com/repos/{Repo}/releases/latest` on window open (and in `--check`);
  offered only for a final, newer release whose `MillerDriverUpdater-<ver>-Setup.exe` is served from
  `https://github.com/{Repo}/releases/download/` and has a `digest: sha256:...` (GitHub lists one per asset). Download
  → SHA-256 must match → Authenticode: if `ExpectedSigner` is set the signature must be valid and match, otherwise
  unsigned (0x800B0100) is accepted but a present-and-broken signature is not → `Setup /S /UPDATE` → app shuts down.
  Setup /UPDATE waits for the exe to be free, keeps it as `.old`, installs, `--ping` + check; if the antivirus took the
  new exe it pauses on Retry/Cancel (shown even in /S): Retry re-copies and re-pings, Cancel puts the `.old` back and
  reopens it; on success `.old` is deleted and the new exe started.
  The relaunched app inherits Setup's (= the old app's) elevation, so no second UAC prompt. Publishing: `release.cmd`
  (reads Version from Core.cs and Repo from Updater.cs with findstr; gh CLI or printed manual steps).
  Unsigned releases will be quarantined by Malwarebytes-style ML on every update; signing first is the real fix.
- Design intent: Apple Software Update, not a dashboard. Resist tabs, sidebars, settings pages or logs in the window.

- **Hide / history (2.3)**: Hide on a row → the source keeps the choice: WUA `IUpdate.IsHidden = true`
  (`WindowsUpdate.SetHidden`, admin, MTA thread; the search no longer filters `IsHidden=0`, hidden ones go to
  `ScanResult.HiddenDrivers`), `winget pin add` (pinned apps drop out of `winget upgrade`, verified; `Winget.Pins`
  parses `winget pin list`), NVIDIA version → `reports\skipped.json` (`Skips`). "Hidden updates" card with Unhide.
  `History` (`reports\history.json`, newest 500) gets every install, rollback, hide and unhide; collapsed card.
  Design came from a survey of UniGetUI, Winget-AutoUpdate, wumgr, TinyNvidiaUpdateChecker, DriverStoreExplorer,
  topgrade (2026-10-21).

## Ideas not built (in priority order)
1. Code signing: SignPath Foundation signs open-source projects free from a GitHub Actions build (TinyNvidiaUpdateChecker
   and DriverStoreExplorer use it); needs an OSI license and a CI build first. Then set `Updater.ExpectedSigner`.
2. CI: build + tests on every push (windows runner has csc v4 and NSIS via choco), release from a tag with build
   provenance attestation. Pushing `.github/workflows` needs the gh token's `workflow` scope.
3. Opt-in unattended app updates from the weekly check (apps only, never drivers), like Winget-AutoUpdate.
4. Publish to winget (winget-pkgs manifest), ideally automated per release.
5. Windows OS updates too (same WUA search without `Type='Driver'`) behind a toggle.
6. Name unknown USB vendors with the public `usb.ids` list.
7. Driver uninstall (`pnputil /delete-driver oemNN.inf /uninstall`) from the All devices card, with confirmation.

## Out of scope (say so in the UI, don't fake it)
- Bluetooth consumer headphones/earbuds — firmware updates go through the phone app.
- BIOS/UEFI — point to the motherboard vendor's tool.
- Anything that would require a third-party driver database.

## How to work in this repo
1. `build.cmd` (build + tests). Then run `dist\MillerDriverUpdater.exe` (or `--no-elevate` for a scan-only window).
2. Design decisions come from real devices: read `%LOCALAPPDATA%\Miller Driver Updater\reports\mdu.log` and the latest
   report first.
3. Keep C# 5, small static engine classes returning plain objects; formatting only in MainWindow.cs / Report.cs.
4. Bump `AppInfo.Version`, the Assembly*Version attributes (Core.cs), app.manifest's version and `APPVER` in
   installer/mdu.nsi together.
