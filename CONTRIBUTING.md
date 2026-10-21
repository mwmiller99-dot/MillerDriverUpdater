# Contributing

Thanks for helping. A few rules keep this app trustworthy and simple.

## The rules
1. **Official sources only.** Updates come from Windows Update, winget, a vendor's own public driver service
   (nvidia.com), or the vendor's own app. No third-party driver databases, no scraping vendor sites, no repackaged
   installers.
2. **One window, no settings pages.** The design is Apple Software Update, not a dashboard: one headline, one primary
   button, quiet cards. New features fit into a card or a small link on a row.
3. **Safety rails stay.** Nothing installs without a click and a confirmation; restore point and per-driver backup
   before drivers; never restart the PC automatically.
4. **No personal data in the repo.** Never commit reports, logs, screenshots or anything that names a real PC, its
   hardware or its installed apps. Use made-up sample data in tests.

## Building
Run `build.cmd` on any Windows 10/11 PC. It compiles `dist\MillerDriverUpdater.exe` with the C# compiler that ships
inside Windows (.NET Framework 4.8) and runs the tests - no Visual Studio, no SDK. That compiler speaks **C# 5**, so no
string interpolation, `?.`, `nameof`, expression-bodied members or tuples. The installer needs NSIS 3
(`installer\mdu.nsi`). See `CLAUDE.md` for how the parts fit together.

## Pull requests
- Keep `build.cmd` passing; add a test in `tests\Tests.cs` for every parsing fix (with sample data, not real output
  from your PC).
- Describe what you tested on a real PC and what you could not.
- Bug reports: use the issue form. Do **not** attach the "Report a Problem" zip publicly; it contains device names and
  driver versions.
