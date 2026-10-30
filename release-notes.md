## Miller Driver Updater 2.5.0

A new **Motherboard and drives** card covers what Windows Update and winget never do, on any PC:

- **BIOS:** its version and age, flagged once it is two years old, with an **Open Page** button to your PC or
  motherboard maker's download page (ASUS, MSI, Gigabyte, ASRock, Dell, HP, Lenovo, Acer, Microsoft, Razer, Framework,
  Samsung). Brand PCs and laptops are recognised by their own model, home-built PCs by their motherboard. The app never
  flashes a BIOS itself.
- **The maker's own updater** (Armoury Crate, MSI Center, GIGABYTE Control Center, Dell Command | Update, HP Support
  Assistant, Lenovo Vantage...) for board-level audio, network and chipset drivers.
- **Chipset driver:** AMD Chipset Software from amd.com on AMD PCs (with the version you have), Intel's Driver &
  Support Assistant on Intel PCs.
- **SSD and hard drive firmware:** each drive with its maker's tool (Samsung Magician, WD/SanDisk Dashboard, Crucial
  Storage Executive, Kingston SSD Manager, Corsair SSD Toolbox, SeaTools and more).

**Install:** download `MillerDriverUpdater-2.5.0-Setup.exe` below and run it. Copies on 2.2.0 or later offer this update
themselves. The installer is not code-signed yet, so Windows may show "Windows protected your PC" (*More info*, then
*Run anyway*). To confirm the file is the official build:
`gh attestation verify MillerDriverUpdater-2.5.0-Setup.exe --repo mwmiller99-dot/MillerDriverUpdater`.
