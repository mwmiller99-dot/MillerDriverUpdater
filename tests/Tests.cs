// Tests for the pure logic (parsers, formatting, matching, error wording). No winget, no admin, no network.
// build.cmd compiles this against dist\MillerDriverUpdater.exe and runs it.
using System;
using System.Collections.Generic;
using Mdu;

static class Tests
{
    static int failures;

    static void Eq(object expected, object actual, string what)
    {
        string e = Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture);
        string a = Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture);
        if (e != a) { failures++; Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("FAIL  " + what + "  expected [" + e + "] got [" + a + "]"); }
        else { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("ok    " + what); }
        Console.ResetColor();
    }

    static void True(bool cond, string what) { Eq(true, cond, what); }

    static Device Dev(string name, string hw, bool present = true, string instance = null)
    {
        Device d = new Device(); d.Name = name; d.HardwareId = hw; d.Present = present; d.InstanceId = instance; return d;
    }

    static int Main()
    {
        // --- winget upgrade table, with spinner junk, a trailing count line and the second "explicit targeting" table
        string[] sample = {
            "   -",
            "   \\\r   |\r   /\r   -\rName                                   Id                                Version        Available      Source",
            "---------------------------------------------------------------------------------------------------------------",
            "7-Zip 24.08 (x64)                      7zip.7zip                         24.08          24.09          winget",
            "Discord                                Discord.Discord                   1.0.9165       1.0.9170       winget",
            "Microsoft Edge                         Microsoft.Edge                    129.0.2792.52  130.0.2849.46  winget",
            "3 upgrades available.",
            "",
            "The following packages have an upgrade available, but require explicit targeting for upgrade:",
            "Name                     Id                   Version      Available    Source",
            "------------------------------------------------------------------------------",
            "Logitech G HUB           Logitech.GHUB        2024.6.6     2024.9.6     winget",
            "1 package(s) have version numbers that cannot be determined. Use --include-unknown to see all results."
        };
        List<WingetUpgrade> rows = Winget.ParseTable(sample);
        Eq(4, rows.Count, "parses 4 rows across both tables");
        Eq("7zip.7zip", rows[0].Id, "first row id");
        Eq("24.08", rows[0].Installed, "first row installed version");
        Eq("24.09", rows[0].Available, "first row available version");
        Eq("winget", rows[0].Source, "first row source");
        Eq("Microsoft Edge", rows[2].Name, "third row name");
        Eq("130.0.2849.46", rows[2].Available, "third row available");
        Eq(false, rows[2].RequiresExplicitTarget, "first table rows are not explicit-target");
        Eq("Logitech.GHUB", rows[3].Id, "second table row id");
        Eq(true, rows[3].RequiresExplicitTarget, "second table row flagged explicit-target");

        Eq(0, Winget.ParseTable(new[] { "No installed package found matching input criteria." }).Count, "no-results output yields no rows");
        Eq(0, Winget.ParseTable(new string[0]).Count, "empty output yields no rows");

        List<WingetUpgrade> r2 = Winget.ParseTable(new[] {
            "Name              Id                 Version   Available",
            "----------------------------------------------------------",
            "Some App          Vendor.SomeApp     1.0       1.1" });
        Eq(1, r2.Count, "no-source table parses one row");
        Eq("1.1", r2[0].Available, "no-source table available version");
        Eq("", r2[0].Source, "no-source table source is empty");

        // laid out the way winget 1.29 prints to a pipe: columns sized to the widest value, one space apart (sample apps)
        Func<string, string, string, string, string> wrow = (n, i, v, a) => string.Format("{0,-23}{1,-29}{2,-25}{3,-25}winget", n, i, v, a);
        List<WingetUpgrade> piped = Winget.ParseTable(new[] {
            string.Format("{0,-23}{1,-29}{2,-25}{3,-25}Source", "Name", "Id", "Version", "Available"),
            new string('-', 108),
            wrow("Steam", "Valve.Steam", "2.10.91.91", "2.10.91.94"),
            wrow("7-Zip 24.08 (x64)", "7zip.7zip", "24.08", "24.09"),
            wrow("Example Tool", "Example.Tool", "4.2.1.1977.gabc123def456", "4.3.0.2001.g0123456789ab"),
            "3 upgrades available." });
        Eq(3, piped.Count, "piped table: 3 rows");
        Eq("4.3.0.2001.g0123456789ab", piped[2].Available, "piped table: long version not cut");
        Eq("self,normal,normal", piped[0].Kind + "," + piped[1].Kind + "," + piped[2].Kind, "piped table: kinds");

        // --- USB / Bluetooth ids (through the inventory's own builder)
        string hw = "HID\\VID_046D&PID_C539&REV_0100&MI_01&Col01";
        Eq("046D", Rx.Group(hw, "VID_([0-9A-F]{4})", 1).ToUpperInvariant(), "USB VID parsed");
        Eq("C539", Rx.Group(hw, "PID_([0-9A-F]{4})", 1).ToUpperInvariant(), "USB PID parsed");
        Eq("0100", Rx.Group(hw, "REV_([0-9A-F]{4})", 1).ToUpperInvariant(), "USB REV parsed");
        Eq("046D", Rx.Group("BTHENUM\\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001046d_PID&b02c", "VID&[0-9A-F]{4}([0-9A-F]{4})", 1).ToUpperInvariant(), "Bluetooth VID parsed");

        Eq("560.94", Inventory.NvidiaFriendlyVersion("32.0.15.6094"), "NVIDIA friendly version");

        // --- pnputil /enum-drivers parsing
        List<DriverPackage> pk = Drivers.ParsePnputilDrivers(new[] {
            "Microsoft PnP Utility", "",
            "Published Name:     oem12.inf", "Original Name:      nvhda.inf", "Provider Name:      NVIDIA Corporation",
            "Class Name:         Sound, video and game controllers", "Class GUID:         {4d36e96c-e325-11ce-bfc1-08002be10318}",
            "Driver Version:     05/14/2024 1.4.1.1", "Signer Name:        Microsoft Windows Hardware Compatibility Publisher", "",
            "Published Name:     oem3.inf", "Original Name:      e2f68.inf", "Provider Name:      Intel", "Class Name:         Network adapters",
            "Driver Version:     01/09/2024 14.0.10.0", "Signer Name:        Microsoft Windows Hardware Compatibility Publisher" });
        Eq(2, pk.Count, "pnputil: two packages parsed");
        Eq("oem12.inf", pk[0].PublishedName, "pnputil: published name");
        Eq("nvhda.inf", pk[0].OriginalName, "pnputil: original name");
        Eq("1.4.1.1", pk[0].Version, "pnputil: version split from date");
        Eq("05/14/2024", pk[0].Date, "pnputil: date split from version");
        Eq("Network adapters", pk[1].Class, "pnputil: second package class");
        Eq(0, Drivers.ParsePnputilDrivers(new string[0]).Count, "pnputil: empty input");

        // --- catalog url from hardware ids
        Eq("https://www.catalog.update.microsoft.com/Search.aspx?q=HID%5CVID_046D%26PID_C539", Inventory.CatalogUrl("HID\\VID_046D&PID_C539&REV_0100&MI_01&Col01"), "catalog url strips REV/MI/Col, keeps bus, escapes");
        Eq("https://www.catalog.update.microsoft.com/Search.aspx?q=PCI%5CVEN_10DE%26DEV_2684", Inventory.CatalogUrl("PCI\\VEN_10DE&DEV_2684&SUBSYS_1"), "catalog url for PCI id drops SUBSYS");
        Eq("https://www.catalog.update.microsoft.com/Search.aspx?q=HDAUDIO%5CFUNC_01%26VEN_10DE%26DEV_009A", Inventory.CatalogUrl("HDAUDIO\\FUNC_01&VEN_10DE&DEV_009A&SUBSYS_12345678&REV_1001"), "catalog url for HD audio id");

        // --- devices with no driver: readable name + which update on the list fixes them
        Eq("NVIDIA High Definition Audio (the HDMI / DisplayPort audio on the graphics card)", Inventory.HardwareDescription("HDAUDIO\\FUNC_01&VEN_10DE&DEV_009A&SUBSYS_12345678&REV_1001", "MEDIA"), "hardware description: NVIDIA HD audio");
        Eq("Realtek High Definition Audio device", Inventory.HardwareDescription("HDAUDIO\\FUNC_01&VEN_10EC&DEV_0897", "MEDIA"), "hardware description: Realtek HD audio");
        Eq("Logitech USB device", Inventory.HardwareDescription("USB\\VID_046D&PID_C539", ""), "hardware description: USB vendor from the peripheral map");
        Eq("Unknown system (ACPI) device", Inventory.HardwareDescription("ACPI\\MSFT0101", ""), "hardware description: unknown ACPI");
        Device pdev = Dev("x", "HDAUDIO\\FUNC_01&VEN_10DE&DEV_009A&SUBSYS_12345678&REV_1001");
        NvidiaDriver nv = new NvidiaDriver(); nv.Available = true; nv.Version = "999.99";
        True((Inventory.ProblemHint(pdev, new List<WuDriver>(), nv) ?? "").Contains("part of the NVIDIA driver update below"), "problem hint: NVIDIA HD audio covered by nvidia.com update");
        True((Inventory.ProblemHint(pdev, new List<WuDriver>(), null) ?? "").Contains("comes with the NVIDIA GeForce driver package"), "problem hint: NVIDIA HD audio, no update on the list");
        WuDriver wud = new WuDriver(); wud.Title = "Intel - Net - 22.250.0.5"; wud.HardwareId = "USB\\VID_8087&PID_0033";
        Eq("The 'Intel - Net - 22.250.0.5' driver update on the list below is for this device.", Inventory.ProblemHint(Dev("y", "USB\\VID_8087&PID_0033&REV_0001"), new List<WuDriver> { wud }, null), "problem hint: WU driver matches by hardware-id prefix");
        True(Inventory.ProblemHint(Dev("z", "ACPI\\MSFT0101"), new List<WuDriver> { wud }, null) == null, "problem hint: nothing known");

        // --- matching a WU driver to inventory devices by hardware id
        List<Device> inv = new List<Device> {
            Dev("Wi-Fi", "PCI\\VEN_8086&DEV_51F0&SUBSYS_00948086&REV_01", true, "PCI\\A"),
            Dev("Old", "PCI\\VEN_8086&DEV_51F0&SUBSYS_00948086&REV_01", false, "PCI\\B"),
            Dev("Other", "PCI\\VEN_10DE&DEV_2684", true, "PCI\\C") };
        List<Device> m = Inventory.DevicesForDriver("PCI\\VEN_8086&DEV_51F0&SUBSYS_00948086", inv);
        Eq(1, m.Count, "driver->device match: present device with id prefix");
        Eq("Wi-Fi", m[0].Name, "driver->device match: right device");
        Eq(0, Inventory.DevicesForDriver("", inv).Count, "driver->device match: empty id matches nothing");

        // --- bytes / speed / duration formatting
        Eq("11.8 MB", Util.FormatBytes(12345678), "format MB");
        Eq("1.15 GB", Util.FormatBytes(1234567890), "format GB");
        Eq("512 KB", Util.FormatBytes(524288), "format KB");
        Eq("3.1 MB/s", Util.FormatSpeed(3250000), "format speed");
        Eq("", Util.FormatSpeed(0), "zero speed is blank");
        Eq("45 s", Util.FormatDuration(45.4), "duration seconds");
        Eq("6 min 00 s", Util.FormatDuration(360), "duration minutes");
        Eq("1 min 05 s", Util.FormatDuration(65), "duration minutes and seconds");

        // --- speed meter: two samples 1 s apart at 1 MB/s
        SpeedMeter sm = new SpeedMeter();
        sm.Measure(0);
        sm.Time = sm.Time.Value.AddSeconds(-1);
        double sp = sm.Measure(1048576);
        True(sp > 1000000 && sp < 1100000, "speed meter computes ~1 MB/s");
        sm.Time = sm.Time.Value.AddSeconds(-1);
        True(sm.Measure(1048576) < sp, "speed meter decays when bytes stop");

        // --- winget progress line parsing
        ByteProgress pg = Winget.ParseProgressLine("  ######################          12.3 MB / 45.6 MB");
        True(pg != null, "winget progress line recognised");
        Eq(Math.Round(12.3 * 1048576), Math.Round(pg.Done), "winget progress done bytes");
        Eq(Math.Round(45.6 * 1048576), Math.Round(pg.Total), "winget progress total bytes");
        Eq(Math.Round(1.2 * 1073741824), Math.Round(Winget.ParseProgressLine("  -  800 KB / 1.20 GB").Total), "winget progress GB total");
        True(Winget.ParseProgressLine("Successfully installed") == null, "non-progress line ignored");
        True(Winget.ParseProgressLine("") == null, "empty line ignored");
        Eq("Downloading 12.3 MB", Winget.StripVt("\u001b[2K\u001b[1GDownloading\u001b[0m 12.3 MB\u001b]0;winget\u0007"), "VT sequences stripped");

        // --- progress text
        ProgressInfo pi = ProgressInfo.Create("Download", percent: 36, bytesDone: 4400000, bytesTotal: 12345678, bytesPerSec: 3250000, index: 0, count: 2, current: "Intel - Net - 22.250.0.5", source: "Windows Update");
        Eq("Downloading Intel - Net - 22.250.0.5 (1 of 2): 4.2 MB of 11.8 MB, 3.1 MB/s", pi.Text, "progress text with totals and speed");
        Eq("Downloading (Windows Update)", pi.Activity, "progress activity");
        Eq("Installing Realtek - MEDIA (2 of 2): 45%", ProgressInfo.Create("Install", percent: 45, index: 1, count: 2, current: "Realtek - MEDIA").Text, "install progress text");
        Eq("Downloading Discord...", ProgressInfo.Create("Download", current: "Discord").Text, "progress text without numbers");

        // --- NVIDIA size text
        Eq(Math.Round(990.85 * 1048576), Math.Round(Util.ParseSizeText("990.85 MB")), "size text MB");
        Eq(Math.Round(1.2 * 1073741824), Math.Round(Util.ParseSizeText("1.2 GB")), "size text GB");
        Eq(0, Util.ParseSizeText(""), "size text empty");
        Eq(12345, Util.ParseSizeText("12345"), "size text plain number");

        // --- which winget rows are real updates (launchers such as Battle.net: version "Unknown", interactive installer)
        Eq("self", Winget.ResolveKind("Blizzard.BattleNet", "Battle.net", "Unknown").Kind, "kind: Battle.net by id is self-updating");
        Eq("self", Winget.ResolveKind("Some.Other", "Battle.net", "1.0").Kind, "kind: Battle.net by name is self-updating");
        Eq("self", Winget.ResolveKind("Valve.Steam", "Steam", "2.10.91.91").Kind, "kind: Steam");
        Eq("self", Winget.ResolveKind("Google.Chrome", "Google Chrome", "129.0").Kind, "kind: Chrome");
        Eq("unknown", Winget.ResolveKind("Acme.Tool", "Acme Tool", "Unknown").Kind, "kind: unknown installed version");
        Eq("unknown", Winget.ResolveKind("Acme.Tool", "Acme Tool", "< 2.0").Kind, "kind: lower-bound version is unknown");
        Eq("normal", Winget.ResolveKind("7zip.7zip", "7-Zip 24.08 (x64)", "24.08").Kind, "kind: normal");
        Eq("normal", Winget.ResolveKind("Logitech.GHUB", "Logitech G HUB", "2024.6.6").Kind, "kind: vendor app with a real version stays normal");
        Eq("Battle\\.net", Winget.ResolveKind("Blizzard.BattleNet", "Battle.net", "Unknown").AppMatch, "kind: self rows carry the Start-menu pattern");

        // --- the watchdog's own result code and captions
        Eq("Stopped: its installer opened a window and waited for a click. Update it from inside the app instead.", Errors.WingetExitText(-3, "Found Battle.net\n[mdu] stopped: its installer opened a window and waited for a click\n"), "watchdog caption: window");
        Eq("Stopped: no progress for 6 min 00 s. Update it from inside the app instead.", Errors.WingetExitText(-3, "[mdu] stopped: no progress for 6 min 00 s"), "watchdog caption: idle");

        // --- plain-language errors (first match wins; unknown text passes through unchanged)
        True(Errors.Friendly("Exception from HRESULT: 0x80070422", true).StartsWith("The Windows Update service is turned off"), "friendly: WU service off");
        True(Errors.Friendly("winget was not found on this PC.", true).StartsWith("winget (the Windows package manager) is not on this PC"), "friendly: winget missing");
        True(Errors.Friendly("Creating a restore point requires administrator rights.", true).StartsWith("Windows could not create a restore point"), "friendly: restore point before access-denied");
        True(Errors.Friendly("The remote name could not be resolved: 'gfwsl.geforce.com'", true).StartsWith("The internet connection dropped"), "friendly: dns");
        True(Errors.Friendly("The remote server returned an error: (404) Not Found.", true).StartsWith("The download server refused"), "friendly: http 404");
        Eq("Driver file not found", Errors.Friendly("Driver file not found", true), "friendly: unknown text unchanged");
        True(Errors.Friendly("Exception from HRESULT: 0x80070422").EndsWith("\n\nDetails: Exception from HRESULT: 0x80070422"), "friendly: details appended");
        Eq("", Errors.Friendly(""), "friendly: empty stays empty");

        // --- winget exit captions (HRESULTs arrive as negative int32 exit codes; the installer's own code wins when printed)
        Eq("Updated", Errors.WingetExitText(0), "winget exit 0");
        True(Errors.WingetExitText(-1978335189).StartsWith("Windows says this update no longer applies"), "winget 0x8A15002B not applicable");
        True(Errors.WingetExitText(-1978334975).StartsWith("That app is open right now"), "winget 0x8A150101 in use");
        Eq("Failed (winget code 0x8A150014)", Errors.WingetExitText(-1978335212), "winget unmapped code shown as hex");
        Eq("Failed: another installation is already running - wait for it to finish, then try again (installer code 1618)", Errors.WingetExitText(-1978335230, "Found X\nInstaller failed with exit code: 1618"), "winget installer code 1618");
        True(Errors.WingetExitText(-1978335230, "Installer failed with exit code: 77").StartsWith("Failed: the app's own installer returned code 77"), "winget unknown installer code");

        // --- self-update: GitHub "latest release" JSON -> offered or not
        Eq("2.2.0.0", Updater.ParseVersion("v2.2.0").ToString(), "update: version from tag");
        Eq("2.2.0.0", Updater.ParseVersion("2.2").ToString(), "update: two-part version");
        True(Updater.ParseVersion("latest") == null, "update: no version in tag");
        const string sha = "a0793f0832ba540a55848deeee271c3686f2149cb4ead7fe72be2417c9f62605";
        Func<string, string, string, bool, string, string> rel = (tag, asset, url, draft, digest) =>
            "{\"tag_name\":\"" + tag + "\",\"name\":\"Miller Driver Updater " + tag + "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":false," +
            "\"html_url\":\"https://github.com/me/MDU/releases/tag/" + tag + "\",\"body\":\"notes\",\"assets\":[" +
            "{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/me/MDU/releases/download/" + tag + "/SHA256SUMS.txt\",\"size\":200,\"digest\":\"sha256:" + sha + "\"}," +
            "{\"name\":\"" + asset + "\",\"browser_download_url\":\"" + url + "\",\"size\":212705" + (digest != null ? ",\"digest\":\"" + digest + "\"" : "") + "}]}";
        string good = "https://github.com/me/MDU/releases/download/v2.3.0/MillerDriverUpdater-2.3.0-Setup.exe";
        UpdateInfo up = Updater.ParseRelease(rel("v2.3.0", "MillerDriverUpdater-2.3.0-Setup.exe", good, false, "sha256:" + sha), "2.2.0", "me/MDU");
        True(up.Available, "update: newer release with digest is offered");
        Eq("2.3.0", up.Version, "update: version text");
        Eq(sha, up.Sha256, "update: sha256 taken from GitHub's digest");
        Eq("MillerDriverUpdater-2.3.0-Setup.exe", up.AssetName, "update: the Setup asset is picked, not the checksum file");
        True(!Updater.ParseRelease(rel("v2.2.0", "MillerDriverUpdater-2.2.0-Setup.exe", good, false, "sha256:" + sha), "2.2.0", "me/MDU").Available, "update: same version is not offered");
        True(!Updater.ParseRelease(rel("v2.1.9", "MillerDriverUpdater-2.1.9-Setup.exe", good, false, "sha256:" + sha), "2.2.0", "me/MDU").Available, "update: older release is not offered");
        True(!Updater.ParseRelease(rel("v2.3.0", "MillerDriverUpdater-2.3.0-Setup.exe", good, true, "sha256:" + sha), "2.2.0", "me/MDU").Available, "update: draft is not offered");
        UpdateInfo noDigest = Updater.ParseRelease(rel("v2.3.0", "MillerDriverUpdater-2.3.0-Setup.exe", good, false, null), "2.2.0", "me/MDU");
        True(!noDigest.Available && noDigest.Error != null, "update: no checksum, not offered");
        True(!Updater.ParseRelease(rel("v2.3.0", "MillerDriverUpdater-2.3.0-Setup.exe", "https://example.com/MillerDriverUpdater-2.3.0-Setup.exe", false, "sha256:" + sha), "2.2.0", "me/MDU").Available, "update: download outside the repo's releases is refused");
        True(!Updater.ParseRelease(rel("v2.3.0", "SomethingElse.exe", good, false, "sha256:" + sha), "2.2.0", "me/MDU").Available, "update: no Setup asset, not offered");
        True(!Updater.ParseRelease("not json", "2.2.0", "me/MDU").Available, "update: unreadable answer, not offered");

        // --- scan percentage never runs ahead of the clock on a fresh phase list
        ScanProgress sp0 = new ScanProgress();
        True(sp0.Percent() <= 1, "scan percent starts near 0");

        Console.WriteLine();
        if (failures == 0) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("All tests passed."); Console.ResetColor(); return 0; }
        Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(failures + " test(s) failed."); Console.ResetColor();
        return 1;
    }
}
