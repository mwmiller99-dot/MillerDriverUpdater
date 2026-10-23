// Reports (JSON + HTML), the support bundle, the weekly scheduled check and its notification.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Mdu
{
    public static class Report
    {
        public static string Json(object o)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.Serialize(o);
        }

        // Writes mdu-<timestamp>.json and .html into the reports folder; returns the HTML path.
        public static string Write(ScanResult scan, List<string> notes)
        {
            string dir = AppInfo.ReportDir;
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", Util.Inv);
            string jsonPath = Path.Combine(dir, "mdu-" + stamp + ".json");
            string htmlPath = Path.Combine(dir, "mdu-" + stamp + ".html");
            List<NvidiaDriver> vendor = new List<NvidiaDriver>();
            if (scan.Nvidia != null) vendor.Add(scan.Nvidia);

            Dictionary<string, object> plain = new Dictionary<string, object>();
            plain["Generated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", Util.Inv);
            plain["App"] = AppInfo.Name + " " + AppInfo.Version;
            plain["System"] = scan.System;
            plain["ProblemDevices"] = scan.Problems;
            plain["Gpus"] = scan.Gpus;
            plain["Peripherals"] = scan.Peripherals;
            plain["WindowsUpdateDrivers"] = scan.Drivers;
            plain["VendorDrivers"] = vendor;
            plain["WingetUpgrades"] = scan.Apps;
            plain["DeviceCount"] = scan.DeviceCount;
            plain["Notes"] = notes;
            File.WriteAllText(jsonPath, Json(plain), new UTF8Encoding(false));
            scan.ReportJson = jsonPath;

            SystemInfo s = scan.System ?? new SystemInfo();
            List<Peripheral> periph = new List<Peripheral>(scan.Peripherals);
            periph.Sort((a, b) => a.Known != b.Known ? (a.Known ? -1 : 1) : string.Compare(a.Vendor, b.Vendor, StringComparison.OrdinalIgnoreCase));

            StringBuilder h = new StringBuilder();
            h.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>Miller Driver Updater report</title>\n<style>\n");
            h.Append(" body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1f23;background:#f6f8fa}\n");
            h.Append(" h1{margin:0 0 4px 0} .sub{color:#57606a;margin-bottom:24px}\n");
            h.Append(" section{background:#fff;border:1px solid #d0d7de;border-radius:8px;padding:16px 20px;margin-bottom:18px}\n");
            h.Append(" h2{font-size:17px;margin:0 0 10px 0} .count{display:inline-block;background:#0969da;color:#fff;border-radius:12px;padding:1px 9px;font-size:12px;margin-left:6px;vertical-align:middle}\n");
            h.Append(" table{border-collapse:collapse;width:100%;font-size:13px} th,td{border-bottom:1px solid #eaeef2;padding:6px 8px;text-align:left;vertical-align:top}\n");
            h.Append(" th{background:#f6f8fa;font-weight:600} tr:hover td{background:#f6f8fa} .empty{color:#57606a;margin:0}\n .notes li{margin:4px 0}\n");
            h.Append("</style></head><body>\n");
            h.Append("<h1>" + LogoImg() + "Miller Driver Updater report</h1>\n");
            h.AppendFormat("<div class=\"sub\">{0} - {1} build {2} &middot; generated {3} &middot; {4} devices scanned &middot; pending reboot: {5}</div>\n",
                Util.Html(s.Computer), Util.Html(s.OS), Util.Html(s.Build), plain["Generated"], scan.DeviceCount, s.PendingReboot);
            Section(h, "Devices with problems", scan.Problems, new[] { "Name", "Class", "Status", "Problem", "DriverVersion", "DriverDate" }, "No device is reporting an error.");
            Section(h, "Driver updates from Windows Update", scan.Drivers, new[] { "Title", "Manufacturer", "Class", "VersionDate", "Optional", "SizeMB" }, "Windows Update has no driver updates for this hardware right now.");
            Section(h, "Graphics driver from nvidia.com", vendor, new[] { "Gpu", "Installed", "Version", "Name", "ReleaseDate", "Available", "Url", "Error" }, "No NVIDIA graphics adapter found.");
            Section(h, "Software updates (winget)", scan.Apps, new[] { "Name", "Id", "Installed", "Available", "Source", "RequiresExplicitTarget" }, "Everything winget knows about is current.");
            Section(h, "Graphics", scan.Gpus, new[] { "Name", "Vendor", "FriendlyVersion", "DriverDate", "VendorApp", "AppInstalled", "AppVersion" }, "No graphics adapter found.");
            Section(h, "Peripherals (USB / Bluetooth)", periph, new[] { "Name", "Vendor", "Vid", "Pid", "FirmwareRev", "DriverVersion", "Present", "VendorApp", "AppInstalled", "AppVersion", "Url" }, "No USB or Bluetooth devices found.");
            h.Append("<section><h2>Notes</h2><ul class=\"notes\">");
            foreach (string n in notes) h.Append("<li>" + Util.Html(n) + "</li>\n");
            h.Append("</ul></section>\n</body></html>\n");
            File.WriteAllText(htmlPath, h.ToString(), new UTF8Encoding(true));
            return htmlPath;
        }

        // The app icon (assets\logo.svg, embedded) inline, so the report is still one self-contained file.
        static string LogoImg()
        {
            try
            {
                using (Stream st = typeof(Report).Assembly.GetManifestResourceStream("Mdu.logo.svg"))
                using (MemoryStream m = new MemoryStream())
                {
                    if (st == null) return "";
                    st.CopyTo(m);
                    return "<img src=\"data:image/svg+xml;base64," + Convert.ToBase64String(m.ToArray()) +
                           "\" width=\"36\" height=\"36\" alt=\"\" style=\"vertical-align:middle;margin:0 10px 4px 0\">";
                }
            }
            catch { return ""; }
        }

        static void Section(StringBuilder h, string title, IList rows, string[] props, string empty)
        {
            h.AppendFormat("<section><h2>{0} <span class='count'>{1}</span></h2>", Util.Html(title), rows.Count);
            if (rows.Count == 0) { h.Append("<p class='empty'>" + Util.Html(empty) + "</p></section>\n"); return; }
            h.Append("<table><tr>");
            foreach (string p in props) h.Append("<th>" + p + "</th>");
            h.Append("</tr>\n");
            foreach (object row in rows)
            {
                h.Append("<tr>");
                foreach (string p in props)
                {
                    FieldInfo f = row.GetType().GetField(p);
                    object v = f == null ? null : f.GetValue(row);
                    string cell = Util.Html(Util.Str(v));
                    cell = Regex.Replace(cell, "(https?://[^\\s<\"]+)", "<a href=\"$1\" target=\"_blank\">$1</a>");
                    h.Append("<td>" + cell + "</td>");
                }
                h.Append("</tr>\n");
            }
            h.Append("</table></section>\n");
        }

        // Zips the logs, the newest reports and a short system summary onto the desktop, for sharing privately with
        // whoever helps with a problem. Never includes driver backups or anything outside the reports folder.
        public static string SupportBundle(ScanResult lastScan, string dest = null)
        {
            if (string.IsNullOrEmpty(dest)) dest = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dest)) dest = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Directory.CreateDirectory(dest);
            string name = "MDU-support-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm", Util.Inv);
            string zip = Path.Combine(dest, name + ".zip");
            if (File.Exists(zip)) File.Delete(zip);

            string dir = AppInfo.ReportDir;
            List<string> files = new List<string>();
            foreach (string n in new[] { "mdu.log", "mdu.old.log", "gui-error.log" }) { string p = Path.Combine(dir, n); if (File.Exists(p)) files.Add(p); }
            files.AddRange(Newest(dir, "mdu-*.json", 3));
            files.AddRange(Newest(dir, "mdu-*.html", 1));

            List<string> info = new List<string>();
            info.Add(AppInfo.Name + " " + AppInfo.Version);
            info.Add("Collected: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Util.Inv));
            try
            {
                SystemInfo s = Inventory.GetSystemInfo();
                info.Add("Computer: " + s.Computer + "   Model: " + s.Model);
                info.Add("OS: " + s.OS + " " + s.OSVersion + " build " + s.Build);
                info.Add(".NET: " + Environment.Version + "   64-bit process: " + Environment.Is64BitProcess + "   Admin: " + s.Admin + "   Pending reboot: " + s.PendingReboot);
            }
            catch (Exception ex) { info.Add("System info failed: " + ex.Message); }
            string wg = Winget.FindExe();
            info.Add("winget: " + (wg ?? "not found"));
            if (wg != null) info.Add("winget version: " + Winget.VersionText());
            try { foreach (PreflightCheck c in Preflight.Run()) info.Add("Pre-flight [" + c.Severity + "] " + c.Title + ": " + c.Detail); } catch { }
            info.Add("");
            info.Add("Graphics:");
            try { foreach (Gpu g in Inventory.Gpus()) info.Add("  " + g.Name + "   driver " + g.FriendlyVersion + "   " + g.VendorApp); } catch { }
            if (lastScan != null && lastScan.Timing.Count > 0)
            {
                info.Add("");
                info.Add("Last scan timing (s):");
                foreach (KeyValuePair<string, double> kv in lastScan.Timing) info.Add("  " + kv.Key + " " + kv.Value.ToString("0.0", Util.Inv));
            }

            using (FileStream fs = new FileStream(zip, FileMode.CreateNew))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string f in files)
                {
                    try
                    {
                        ZipArchiveEntry e = za.CreateEntry(Path.GetFileName(f));
                        using (Stream w = e.Open())
                        using (FileStream r = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            r.CopyTo(w);
                    }
                    catch { }
                }
                ZipArchiveEntry sys = za.CreateEntry("system.txt");
                using (StreamWriter w = new StreamWriter(sys.Open(), new UTF8Encoding(true)))
                    foreach (string line in info) w.WriteLine(line);
            }
            return zip;
        }

        static List<string> Newest(string dir, string pattern, int take)
        {
            List<FileInfo> all = new List<FileInfo>();
            if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir, pattern)) all.Add(new FileInfo(f));
            all.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            List<string> res = new List<string>();
            for (int i = 0; i < all.Count && i < take; i++) res.Add(all[i].FullName);
            return res;
        }

        // Old reports pile up (one pair per scan); keep the newest 20 of each.
        public static void Prune()
        {
            try
            {
                foreach (string pattern in new[] { "mdu-*.json", "mdu-*.html" })
                {
                    List<string> keep = Newest(AppInfo.ReportDir, pattern, 20);
                    foreach (string f in Directory.GetFiles(AppInfo.ReportDir, pattern)) if (!keep.Contains(f)) { try { File.Delete(f); } catch { } }
                }
            }
            catch { }
        }
    }

    // Weekly check: a scheduled task for this user (no admin needed when it runs) that starts "MillerDriverUpdater.exe --check".
    public static class Schedule
    {
        static string Schtasks { get { return Util.SystemTool("schtasks.exe"); } }

        public static bool Exists(string taskName = AppInfo.ScheduleTaskName)
        {
            try { return Util.Run(Schtasks, "/Query /TN " + Util.QuoteArg(taskName), 15000).ExitCode == 0; } catch { return false; }
        }

        // The weekly task as Windows stores it (XML), or null.
        static string Definition(string taskName)
        {
            try
            {
                ProcResult r = Util.Run(Schtasks, "/Query /XML /TN " + Util.QuoteArg(taskName), 15000);
                return r.ExitCode == 0 ? r.Output : null;
            }
            catch { return null; }
        }

        // On: whether the weekly check exists. A task left by an earlier version - under the old name, or running the
        // PowerShell script or another copy of the exe - is replaced by one that runs this exe, so it is never set up twice.
        public static bool CheckAndRepair(string exePath)
        {
            string xml = Definition(AppInfo.ScheduleTaskName);
            bool old = false;
            foreach (string n in AppInfo.OldTaskNames) if (Exists(n)) old = true;
            if (xml == null && !old) return false;
            bool isApp = string.Equals(Path.GetFileName(exePath), "MillerDriverUpdater.exe", StringComparison.OrdinalIgnoreCase);
            if (!isApp) return true;   // a test harness: report, change nothing
            bool pointsHere = xml != null && xml.IndexOf(System.Security.SecurityElement.Escape(exePath), StringComparison.OrdinalIgnoreCase) >= 0;
            if (!pointsHere || old)
            {
                try { Register(exePath); Log.Diag("Weekly check now runs " + exePath + (old ? " (replaced the task from an earlier version)" : "")); }
                catch (Exception ex) { Log.Diag("Re-pointing the weekly check failed: " + ex.Message); }
            }
            return true;
        }

        public static void Register(string exePath, string taskName = AppInfo.ScheduleTaskName)
        {
            DateTime next = DateTime.Today.AddHours(10);
            while (next.DayOfWeek != DayOfWeek.Monday || next < DateTime.Now) next = next.AddDays(1);
            string user = WindowsIdentity.GetCurrent().Name;
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
"  <RegistrationInfo><Description>Scans for driver and software updates from official sources and shows a notification when any are available.</Description></RegistrationInfo>\n" +
"  <Triggers><CalendarTrigger><StartBoundary>" + next.ToString("yyyy-MM-ddTHH:mm:ss", Util.Inv) + "</StartBoundary><Enabled>true</Enabled>" +
     "<ScheduleByWeek><DaysOfWeek><Monday /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek></CalendarTrigger></Triggers>\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + System.Security.SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\n" +
"  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
     "<StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable><ExecutionTimeLimit>PT30M</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
"  <Actions Context=\"Author\"><Exec><Command>" + System.Security.SecurityElement.Escape(exePath) + "</Command><Arguments>--check</Arguments></Exec></Actions>\n" +
"</Task>\n";
            string tmp = Path.Combine(Path.GetTempPath(), "mdu-task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                ProcResult r = Util.Run(Schtasks, "/Create /F /TN " + Util.QuoteArg(taskName) + " /XML " + Util.QuoteArg(tmp), 30000);
                if (r.ExitCode != 0) throw new InvalidOperationException(r.Output.Trim());
            }
            finally { try { File.Delete(tmp); } catch { } }
            if (taskName == AppInfo.ScheduleTaskName) DeleteOld();   // one weekly check, under the current name
        }

        public static void Unregister(string taskName = AppInfo.ScheduleTaskName)
        {
            Util.Run(Schtasks, "/Delete /F /TN " + Util.QuoteArg(taskName), 15000);
            if (taskName == AppInfo.ScheduleTaskName) DeleteOld();
        }

        static void DeleteOld()
        {
            foreach (string n in AppInfo.OldTaskNames) { try { if (Exists(n)) Util.Run(Schtasks, "/Delete /F /TN " + Util.QuoteArg(n), 15000); } catch { } }
        }
    }

    // The scheduled check: scan quietly, and when something is found show a notification that opens the app when clicked.
    public static class CheckMode
    {
        public static int Run()
        {
            ScanResult r = Scanner.Run(new ScanProgress());
            if (r.Error != null) { Log.Diag("Weekly check failed: " + r.Error); return 1; }
            int drivers = r.Drivers.Count + (r.Nvidia != null && r.Nvidia.Available ? 1 : 0);
            int apps = 0;
            foreach (WingetUpgrade a in r.Apps) if (a.Kind == "normal") apps++;
            List<string> parts = new List<string>();
            if (drivers > 0) parts.Add(drivers == 1 ? "1 driver update" : drivers + " driver updates");
            if (apps > 0) parts.Add(apps == 1 ? "1 app update" : apps + " app updates");
            if (r.Problems.Count > 0) parts.Add(r.Problems.Count == 1 ? "1 device problem" : r.Problems.Count + " device problems");
            UpdateInfo self = Updater.Check();
            if (self.Available) parts.Add(AppInfo.Name + " " + self.Version + " is available");
            Log.Diag("Weekly check: " + (parts.Count == 0 ? "nothing to report" : string.Join(", ", parts.ToArray())));
            if (parts.Count == 0) return 0;
            Notify(string.Join(", ", parts.ToArray()) + ". Click here to review and install.");
            return 0;
        }

        // A tray icon whose balloon Windows shows as a regular notification. Clicking it (or the icon) opens the app.
        // The icon stays for 15 minutes so the notification can still be clicked after it slides away.
        static void Notify(string message)
        {
            System.Windows.Forms.NotifyIcon icon = new System.Windows.Forms.NotifyIcon();
            try
            {
                using (Stream s = typeof(CheckMode).Assembly.GetManifestResourceStream("Mdu.mdu.ico"))
                    icon.Icon = s != null ? new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Information;
                icon.Text = AppInfo.Name;
                EventHandler open = (sender, e) =>
                {
                    try { Process.Start(AppInfo.ExePath); } catch (Exception ex) { Log.Diag("Opening the app from the notification failed: " + ex.Message); }
                    System.Windows.Forms.Application.ExitThread();
                };
                icon.BalloonTipClicked += open;
                icon.Click += open;
                icon.Visible = true;
                icon.ShowBalloonTip(30000, AppInfo.Name, message, System.Windows.Forms.ToolTipIcon.Info);
                System.Windows.Forms.Timer quit = new System.Windows.Forms.Timer();
                quit.Interval = 15 * 60 * 1000;
                quit.Tick += (sender, e) => System.Windows.Forms.Application.ExitThread();
                quit.Start();
                System.Windows.Forms.Application.Run();
            }
            finally { icon.Visible = false; icon.Dispose(); }
        }
    }
}
