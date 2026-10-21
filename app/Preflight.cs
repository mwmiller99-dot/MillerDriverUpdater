// The "for friends" layer: pre-flight checks with one-click fixes, and errors in words a non-technical person can act on.
using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Mdu
{
    public static class Preflight
    {
        static PreflightCheck C(string id, string severity, string title, string detail, string fixText = null, string fixKind = null, string fixArg = null)
        {
            PreflightCheck c = new PreflightCheck();
            c.Id = id; c.Severity = severity; c.Title = title; c.Detail = detail; c.FixText = fixText; c.FixKind = fixKind; c.FixArg = fixArg;
            return c;
        }

        // Things worth knowing before installing anything. Nothing here blocks. quick = only the instant checks (run again
        // right before Install); a scan runs the full set. FixKind: 'start' = open FixArg, 'service' = re-enable that service.
        public static List<PreflightCheck> Run(bool quick = false, double minFreeGB = 5)
        {
            List<PreflightCheck> checks = new List<PreflightCheck>();

            bool online = true;
            try { online = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            if (!online) checks.Add(C("network", "bad", "No network connection", "Windows Update, winget and nvidia.com all need the internet. Connect, then Check Again.", "Open network settings", "start", "ms-settings:network"));

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT BatteryStatus, EstimatedChargeRemaining FROM Win32_Battery"))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementBaseObject b in rows)
                    {
                        using (b)
                        {
                            int status = Convert.ToInt32(b["BatteryStatus"] ?? 0);   // 1 = discharging, 4 = low, 5 = critical; 2 = on AC; 6-9 = charging
                            if (status == 1 || status == 4 || status == 5)
                            {
                                string detail = "Driver installs take several minutes and must not be interrupted. Plug the charger in first.";
                                if (b["EstimatedChargeRemaining"] != null) detail = "Battery at " + Convert.ToInt32(b["EstimatedChargeRemaining"]) + "%. " + detail;
                                checks.Add(C("battery", "warn", "Running on battery", detail));
                            }
                        }
                        break;
                    }
                }
            }
            catch { }

            try
            {
                string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                DriveInfo di = new DriveInfo(drive);
                double freeGB = Math.Round(di.AvailableFreeSpace / (1024.0 * 1024 * 1024), 1);
                if (freeGB < minFreeGB)
                    checks.Add(C("disk", "warn", "Low disk space on " + drive, freeGB.ToString(Util.Inv) + " GB free. Updates need room to download and unpack (the NVIDIA driver alone is about 1 GB, 3 GB unpacked).", "Free up space", "start", "ms-settings:storagesense"));
            }
            catch { }

            if (IsMetered()) checks.Add(C("metered", "info", "Metered connection", "This connection is set as metered in Windows. Updates here can be large (the NVIDIA driver is about 1 GB)."));

            if (Util.PendingReboot()) checks.Add(C("reboot", "warn", "A restart is already pending", "Windows is waiting to finish an earlier update. Restart first: driver installs on top of a pending restart sometimes fail or get undone."));
            if (quick) return checks;

            using (RegistryKey hklm = Util.OpenHklm())
            {
                object start = Util.RegValue(hklm, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start");
                if (start != null && Convert.ToInt32(start) == 4)
                    checks.Add(C("wu-service", "bad", "Windows Update is turned off", "The Windows Update service is disabled on this PC (a \"debloat\" or tweaking tool usually does this). Driver updates come from Windows Update, so the Drivers list stays empty until it is back on.", "Turn it back on", "service", "wuauserv"));

                if (Winget.FindExe() == null)
                    checks.Add(C("winget", "warn", "winget is missing", "winget (the Windows package manager) is what updates the apps. It comes with \"App Installer\" from the Microsoft Store and is built into Windows 11 and recent Windows 10. Driver updates work without it.", "Get App Installer", "start", "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1"));

                bool off = false;
                object interval = Util.RegValue(hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "RPSessionInterval");
                object disable = Util.RegValue(hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "DisableSR");
                object policy = Util.RegValue(hklm, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                if (interval != null && Convert.ToInt32(interval) == 0) off = true;   // 1 = System Restore on, 0 = off
                if (disable != null && Convert.ToInt32(disable) == 1) off = true;
                if (policy != null && Convert.ToInt32(policy) == 1) off = true;
                if (off)
                    checks.Add(C("protection", "warn", "System Protection is off", "Windows cannot create a restore point while System Protection is off for the Windows drive. Each driver is still backed up individually before it is replaced, so this is a second safety net, not a requirement. To turn it on: select the Windows drive, Configure, Turn on system protection.", "Open System Protection", "start", "SystemPropertiesProtection.exe"));
            }
            return checks;
        }

        // WinRT's NetworkInformation, reached by reflection (a .NET Framework app can load WinRT types this way, as PowerShell does).
        static bool IsMetered()
        {
            try
            {
                Type ni = Type.GetType("Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType=WindowsRuntime");
                if (ni == null) return false;
                object profile = ni.GetMethod("GetInternetConnectionProfile").Invoke(null, null);
                if (profile == null) return false;
                object cost = profile.GetType().GetMethod("GetConnectionCost").Invoke(profile, null);
                string type = Convert.ToString(cost.GetType().GetProperty("NetworkCostType").GetValue(cost, null));
                return type == "Fixed" || type == "Variable";
            }
            catch { return false; }
        }

        // Performs the fix a check offers. Returns a one-line message for the status bar.
        public static string Fix(PreflightCheck check)
        {
            switch (check.FixKind)
            {
                case "start":
                    Util.OpenShell(check.FixArg);
                    return "Opened " + check.FixText + ". Check Again afterwards.";
                case "service":
                    if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Turning a service back on needs administrator rights.");
                    ProcResult r = Util.Run(Util.SystemTool("sc.exe"), "config " + check.FixArg + " start= demand", 30000);
                    if (r.ExitCode != 0) throw new InvalidOperationException("sc config failed: " + r.Output.Trim());
                    try { using (ServiceController sc = new ServiceController(check.FixArg)) { if (sc.Status != ServiceControllerStatus.Running) sc.Start(); } } catch { }
                    return "The Windows Update service is back on. Check Again to look for driver updates.";
                default:
                    return "Nothing to do for that one.";
            }
        }
    }

    public static class Errors
    {
        // Order matters: first match wins.
        static readonly string[][] Map = {
            new[] { @"restore point|System Protection|Checkpoint-Computer|0x80042302|0x81000101|shadow copy", "Windows could not create a restore point. Turn on System Protection for the Windows drive (Start, type \"Create a restore point\"), or continue without one: each driver is still backed up individually before it is replaced." },
            new[] { @"winget was not found|App Installer|winget\.exe", "winget (the Windows package manager) is not on this PC, so apps cannot be updated from here. Install \"App Installer\" from the Microsoft Store, then Check Again. Driver updates work without it." },
            new[] { @"0x8A150101|0x8A150103|0x8A150111|is in use|being used by another", "That app is open right now, so its installer could not replace it. Close the app and try again." },
            new[] { @"0x8A150102|\b1618\b|another install", "Another installation is already running on this PC. Wait for it to finish, then try again." },
            new[] { @"0x8A150109|0x8A15010A|\b3010\b", "Windows needs a restart before this can finish. Restart, then Check Again." },
            new[] { @"0x8A150011", "The downloaded installer did not match the publisher's checksum, so winget refused to run it. This usually clears up within a day; try again later." },
            new[] { @"0x8A15008E", "This app was installed a different way (for example from the Microsoft Store or the vendor's own installer), so winget will not update it. Update it from where it came from." },
            new[] { @"0x8A15010D|0x8A15010E|already installed", "That version is already installed. Check Again to refresh the list." },
            new[] { @"0x8A150019|0x8A150056", "This installer refuses to run with administrator rights (some per-user apps do). Update it by opening the app itself." },
            new[] { @"0x8A15010C", "The installer was cancelled." },
            new[] { @"0x8A15010F|blocked by policy|0x8A15001B|0x8A15001C|0x8A15001E", "A policy on this PC blocks that installation." },
            new[] { @"0x80070422", "The Windows Update service is turned off on this PC. Turn it back on from \"Before you start\" (or Services, Windows Update, Startup type: Manual), then Check Again." },
            new[] { @"0x80070070|not enough space|disk full|0x8A150105", "There is not enough free space on the Windows drive. Free up a few GB and try again." },
            new[] { @"0x80240022|0x80240017|not applicable|0x8A15002B", "Windows says this update no longer applies to this PC (it may already be installed). Check Again to refresh the list." },
            new[] { @"0x8024001E|0x8024000B", "Windows Update was interrupted before it finished. Check Again and retry." },
            new[] { @"signature|Authenticode", "The downloaded installer did not pass the publisher signature check, so it was not run. Try again later; if it keeps happening, use Report a Problem." },
            new[] { @"Access is denied|UnauthorizedAccess|0x80070005|administrator rights", "Windows refused access. Close Miller Driver Updater, open it again from the Start menu, and approve the \"make changes to your device\" prompt - it has to run as administrator." },
            new[] { @"proxy tunnel|status code|\((40[0-9]|50[0-9])\)|\b(403 Forbidden|404 Not Found|502 Bad Gateway|503 Service Unavailable)\b", "The download server refused the request or is not answering right now. Try again later." },
            new[] { @"could not be resolved|No such host|0x80072EE7|0x80072EFD|0x80072EFE|0x8024402C|0x80244022|timed out|connection was closed|Unable to connect|connection was forcibly|WebException|0x8A150008|0x8A150107|network", "The internet connection dropped or the server did not answer. Check the connection and try again." },
        };

        // A raw error turned into a sentence; the original follows after "Details:" unless noDetails. Unknown text comes back unchanged.
        public static string Friendly(string message, bool noDetails = false)
        {
            string m = message ?? "";
            if (m.Length == 0) return m;
            foreach (string[] e in Map)
            {
                if (Rx.IsMatch(m, e[0])) return noDetails ? e[1] : e[1] + "\n\nDetails: " + m;
            }
            return m;
        }

        // The innermost useful message of an exception (COM errors carry their HRESULT in the text so Friendly can match it).
        public static string Text(Exception ex)
        {
            while (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            string msg = ex.Message;
            if (ex is System.Runtime.InteropServices.COMException && !Rx.IsMatch(msg, "0x[0-9A-F]{8}"))
                msg += string.Format(" (0x{0:X8})", ((System.Runtime.InteropServices.COMException)ex).ErrorCode);
            return msg;
        }

        // Short caption for a winget result: 'Updated', or a plain-language reason. When winget printed the app's own
        // installer code ("Installer failed with exit code: 1618") that is the better clue.
        public static string WingetExitText(long code, string output = null)
        {
            if (code == 0) return "Updated";
            if (code == -3 || Rx.IsMatch(output ?? "", @"\[mdu\] stopped:"))
            {
                string why = Rx.Group(output ?? "", @"\[mdu\] stopped: (.+)", 1);
                why = string.IsNullOrEmpty(why) ? "it stopped responding" : why.Trim();
                return "Stopped: " + why + ". Update it from inside the app instead.";
            }
            string inst = Rx.Group(output ?? "", @"Installer failed with exit code:\s*(-?\d+)", 1);
            if (inst != null)
            {
                long installer = long.Parse(inst, Util.Inv);
                string why = null;
                switch (installer)
                {
                    case 2: why = "a file the installer needed was missing (a bad download; try again)"; break;
                    case 5: why = "Windows refused the installer access"; break;
                    case 1602: why = "the installer was cancelled"; break;
                    case 1603: why = "the app's own installer hit a fatal error"; break;
                    case 1618: why = "another installation is already running - wait for it to finish, then try again"; break;
                    case 1619: why = "the installer package could not be opened (a bad download; try again)"; break;
                    case 1625: why = "a policy on this PC blocks that installer"; break;
                    case 1638: why = "another version of this app is already installed"; break;
                    case 1641: why = "the installer started a restart"; break;
                    case 3010: why = "done, but a restart is needed to finish"; break;
                    case 1223: why = "the installer was cancelled"; break;
                }
                if (why != null) return "Failed: " + why + " (installer code " + installer + ")";
                return "Failed: the app's own installer returned code " + installer + ". Updating it from inside the app usually works.";
            }
            string hex = null;
            if (code >= int.MinValue && code <= int.MaxValue) hex = "0x" + unchecked((uint)(int)code).ToString("X8");
            string probe = hex != null ? hex + " " + code : code.ToString(Util.Inv);
            string f = Friendly(probe, true);
            if (f == probe) return "Failed (winget code " + (hex ?? code.ToString(Util.Inv)) + ")";
            return f;
        }
    }
}
