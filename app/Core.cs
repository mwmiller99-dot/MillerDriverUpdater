// Miller Driver Updater - shared basics: app identity, data folder, logging, formatting, process helpers.
// C# 5 on purpose: build.cmd compiles with the csc.exe that ships inside Windows (.NET Framework 4.8),
// so the app builds on any Windows 10/11 PC with no SDK and runs with nothing to install.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

[assembly: AssemblyTitle("Miller Driver Updater")]
[assembly: AssemblyDescription("Miller Driver Updater")]
[assembly: AssemblyProduct("Miller Driver Updater")]
[assembly: AssemblyCompany("Miller Driver Updater")]
[assembly: AssemblyCopyright("Official sources only: Windows Update, nvidia.com, winget.")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]
[assembly: AssemblyInformationalVersion("2.3.0")]
// csc does not stamp this by itself; without it the runtime applies .NET 4.0 compatibility quirks (old TLS defaults etc.).
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

namespace Mdu
{
    public static class AppInfo
    {
        public const string Name = "Miller Driver Updater";
        public const string Version = "2.3.0";   // bump together with AssemblyVersion above and APPVER in installer/mdu.nsi
        public const string ScheduleTaskName = "Miller Driver Updater - weekly check";

        static string _dataDir;

        public static string ExePath
        {
            get
            {
                Assembly a = Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return a.Location;
                return Process.GetCurrentProcess().MainModule.FileName;
            }
        }

        public static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }

        // %LOCALAPPDATA%\Miller Driver Updater: reports, logs, driver backups, downloads. Same place whether the
        // app was installed or is run as a single portable exe from Downloads.
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                string[] candidates = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name),
                    Path.Combine(Path.GetTempPath(), Name)
                };
                foreach (string c in candidates)
                {
                    try { Directory.CreateDirectory(Path.Combine(c, "reports")); _dataDir = c; return c; } catch { }
                }
                _dataDir = Path.GetTempPath();
                return _dataDir;
            }
        }

        public static string ReportDir { get { return Path.Combine(DataDir, "reports"); } }
        public static string BackupDir { get { return Path.Combine(ReportDir, "driver-backups"); } }
        public static string DownloadDir { get { return Path.Combine(DataDir, "downloads"); } }

        // ---- Until 2.0 the app was "Miller Driver Update" (1.x: PowerShell, reports under the install folder;
        // 2.0: this exe, data in %LOCALAPPDATA%\Miller Driver Update). 2.1 renamed it to Miller Driver Updater.
        public const string OldName = "Miller Driver Update";
        public static readonly string[] OldTaskNames = { "Miller Driver Update - weekly check", "AIO Updater - weekly check" };

        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        // Every place driver backups may live: this version's, 2.0's (if its folder could not be moved) and 1.x's.
        public static string[] AllBackupDirs
        {
            get
            {
                return new[] {
                    BackupDir,
                    Path.Combine(Local, OldName, @"reports\driver-backups"),
                    Path.Combine(Local, @"Programs\" + OldName + @"\reports\driver-backups")
                };
            }
        }

        // Moves 2.0's data folder to the new name the first time 2.1 runs, so reports, scan timings and driver backups
        // carry over. Must run before anything touches DataDir (that creates the new folder).
        public static void MigrateOldData()
        {
            try
            {
                string oldDir = Path.Combine(Local, OldName), newDir = Path.Combine(Local, Name);
                if (!Directory.Exists(oldDir)) return;
                // An empty new folder (only created, nothing written yet) does not count as data to keep.
                if (Directory.Exists(newDir) && Directory.GetFiles(newDir, "*", SearchOption.AllDirectories).Length == 0)
                    Directory.Delete(newDir, true);
                if (!Directory.Exists(newDir))
                {
                    Directory.Move(oldDir, newDir);
                    Log.Diag("Moved the data folder from " + oldDir + " to " + newDir + ".");
                }
            }
            catch (Exception ex) { Log.Diag("Moving the old data folder failed (" + ex.Message + "); its backups are still listed from there."); }
        }
    }

    public static class Log
    {
        static readonly object Lock = new object();
        public static string DiagPath { get { return Path.Combine(AppInfo.ReportDir, "mdu.log"); } }
        public static string ErrorPath { get { return Path.Combine(AppInfo.ReportDir, "gui-error.log"); } }

        public static void Diag(string text) { Write(DiagPath, "[diag] " + text); }
        public static void Error(string text) { Write(ErrorPath, text); Write(DiagPath, "[error] " + text); }

        // Keeps the log from growing forever: one previous generation is kept as mdu.old.log.
        public static void Rotate()
        {
            try
            {
                FileInfo f = new FileInfo(DiagPath);
                if (f.Exists && f.Length > 2 * 1024 * 1024)
                {
                    string old = Path.Combine(AppInfo.ReportDir, "mdu.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(DiagPath, old);
                }
            }
            catch { }
        }

        static void Write(string path, string text)
        {
            try
            {
                lock (Lock)
                {
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }

    // PowerShell's -match is case-insensitive; every pattern ported from the old engine relies on that.
    public static class Rx
    {
        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        public static bool IsMatch(string input, string pattern) { return input != null && Regex.IsMatch(input, pattern, Opt); }
        public static Match Match(string input, string pattern) { return Regex.Match(input ?? "", pattern, Opt); }
        public static string Replace(string input, string pattern, string replacement) { return Regex.Replace(input ?? "", pattern, replacement, Opt); }
        public static string Group(string input, string pattern, int group)
        {
            Match m = Match(input, pattern);
            return m.Success ? m.Groups[group].Value : null;
        }
    }

    public class ProcResult
    {
        public int ExitCode = -1;
        public string Output = "";
        public bool TimedOut;
        public string[] Lines { get { return (Output ?? "").Replace("\r\n", "\n").Split('\n'); } }
    }

    public static class Util
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string Dot = "·";
        public const string Arrow = "→";

        // TLS 1.2 for nvidia.com, whatever the host process defaults to (SystemDefault already includes it).
        public static void EnsureTls()
        {
            try
            {
                if (System.Net.ServicePointManager.SecurityProtocol != System.Net.SecurityProtocolType.SystemDefault)
                    System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch { }
        }

        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public static RegistryKey OpenHklm() { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }

        public static bool KeyExists(RegistryKey root, string path)
        {
            try { using (RegistryKey k = root.OpenSubKey(path)) { return k != null; } } catch { return false; }
        }

        public static object RegValue(RegistryKey root, string path, string name)
        {
            try { using (RegistryKey k = root.OpenSubKey(path)) { return k == null ? null : k.GetValue(name); } } catch { return null; }
        }

        public static bool PendingReboot()
        {
            using (RegistryKey hklm = OpenHklm())
            {
                return KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
                    || KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            }
        }

        public static string FormatBytes(double bytes)
        {
            const double KB = 1024.0, MB = KB * 1024, GB = MB * 1024;
            if (bytes >= GB) return (bytes / GB).ToString("n2", Inv) + " GB";
            if (bytes >= MB) return (bytes / MB).ToString("n1", Inv) + " MB";
            if (bytes >= KB) return (bytes / KB).ToString("n0", Inv) + " KB";
            return bytes.ToString("n0", Inv) + " B";
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "";
            return FormatBytes(bytesPerSec) + "/s";
        }

        public static string FormatDuration(double seconds)
        {
            int s = (int)Math.Round(seconds);
            if (s < 60) return s.ToString(Inv) + " s";
            return string.Format(Inv, "{0} min {1:00} s", s / 60, s % 60);
        }

        // "990.85 MB" / "1.2 GB" / "512 KB" -> bytes; a plain number is taken as bytes; 0 when unparseable.
        public static double ParseSizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Match m = Rx.Match(text, @"([\d.,]+)\s*(B|KB|MB|GB)\b");
            if (m.Success)
            {
                double mult = 1;
                switch (m.Groups[2].Value.ToUpperInvariant())
                {
                    case "KB": mult = 1024.0; break;
                    case "MB": mult = 1024.0 * 1024; break;
                    case "GB": mult = 1024.0 * 1024 * 1024; break;
                }
                double v;
                if (double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, Inv, out v)) return v * mult;
                return 0;
            }
            double plain;
            return double.TryParse(text.Trim(), NumberStyles.Float, Inv, out plain) ? plain : 0;
        }

        public static string SystemTool(string exe) { return Path.Combine(Environment.SystemDirectory, exe); }

        public static string QuoteArg(string a)
        {
            if (string.IsNullOrEmpty(a)) return "\"\"";
            if (a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        public static string JoinArgs(IEnumerable<string> args)
        {
            List<string> parts = new List<string>();
            foreach (string a in args) parts.Add(QuoteArg(a));
            return string.Join(" ", parts.ToArray());
        }

        // Runs a console tool hidden and captures stdout + stderr. ExitCode -2 = killed at the timeout.
        public static ProcResult Run(string exe, string args, int timeoutMs, Encoding encoding = null)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args ?? "");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            if (encoding != null) { psi.StandardOutputEncoding = encoding; psi.StandardErrorEncoding = encoding; }
            StringBuilder sb = new StringBuilder();
            object gate = new object();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (gate) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                ProcResult r = new ProcResult();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    r.TimedOut = true;
                    r.ExitCode = -2;
                }
                else
                {
                    p.WaitForExit();   // lets the async readers drain
                    r.ExitCode = p.ExitCode;
                }
                lock (gate) r.Output = sb.ToString();
                return r;
            }
        }

        // Settings pages, Store links, URLs and plain exes, the way Explorer would open them.
        public static void OpenShell(string target, string args = null)
        {
            ProcessStartInfo psi = new ProcessStartInfo(target);
            if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        public static string Html(string s) { return System.Net.WebUtility.HtmlEncode(s ?? ""); }

        public static string CimDate(object value)
        {
            string s = value as string;
            if (string.IsNullOrEmpty(s)) return null;
            try { return System.Management.ManagementDateTimeConverter.ToDateTime(s).ToString("yyyy-MM-dd", Inv); } catch { return null; }
        }

        public static string Str(object o) { return o == null ? "" : Convert.ToString(o, Inv); }

        public static string Last(IList<string> list) { return list.Count == 0 ? "" : list[list.Count - 1]; }
    }

    // Smoothed bytes-per-second figure from (time, bytes) samples.
    public class SpeedMeter
    {
        public DateTime? Time;
        public double Bytes;
        public double Speed;

        public double Measure(double bytes)
        {
            DateTime now = DateTime.UtcNow;
            if (Time == null) { Time = now; Bytes = bytes; return Speed; }
            double dt = (now - Time.Value).TotalSeconds;
            if (dt < 0.25) return Speed;
            double inst = (bytes - Bytes) / dt;
            if (inst < 0) inst = 0;
            Speed = Speed > 0 ? 0.6 * Speed + 0.4 * inst : inst;
            Time = now;
            Bytes = bytes;
            return Speed;
        }
    }

    // One progress tick. Percent / Bytes* describe the whole batch; Item* the item named in Current (Index of Count).
    // -1 / 0 mean unknown. Text and ItemText are ready to show.
    public class ProgressInfo
    {
        public string Phase, Source, Current, ItemText, Activity, Text;
        public int Percent, Index, Count, ItemPercent;
        public double BytesDone, BytesTotal, BytesPerSec, ItemBytesDone, ItemBytesTotal;

        public static ProgressInfo Create(string phase, int percent = -1, double bytesDone = 0, double bytesTotal = 0, double bytesPerSec = 0,
                                          int index = 0, int count = 0, string current = "", string source = "",
                                          int itemPercent = -1, double itemBytesDone = 0, double itemBytesTotal = 0, string itemText = "")
        {
            string verb = phase == "Install" ? "Installing" : "Downloading";
            if (itemPercent < 0 && count <= 1) itemPercent = percent;
            if (itemBytesTotal <= 0 && count <= 1) { itemBytesDone = bytesDone; itemBytesTotal = bytesTotal; }
            current = current ?? "";
            string what = current;
            if (count > 1 && current.Length > 0) what = string.Format(Util.Inv, "{0} ({1} of {2})", current, index + 1, count);

            List<string> parts = new List<string>();
            if (bytesTotal > 0) parts.Add(Util.FormatBytes(bytesDone) + " of " + Util.FormatBytes(bytesTotal));
            else if (bytesDone > 0) parts.Add(Util.FormatBytes(bytesDone));
            if (bytesPerSec > 0) parts.Add(Util.FormatSpeed(bytesPerSec));
            if (phase == "Install" && percent >= 0) parts.Add(percent + "%");
            string text = verb;
            if (what.Length > 0) text += " " + what;
            text += parts.Count > 0 ? ": " + string.Join(", ", parts.ToArray()) : "...";

            List<string> iparts = new List<string>();
            if (itemBytesTotal > 0) iparts.Add(Util.FormatBytes(itemBytesDone) + " of " + Util.FormatBytes(itemBytesTotal));
            else if (itemBytesDone > 0) iparts.Add(Util.FormatBytes(itemBytesDone));
            if (bytesPerSec > 0 && phase != "Install") iparts.Add(Util.FormatSpeed(bytesPerSec));
            if (itemPercent >= 0) iparts.Add(itemPercent + "%");
            string itext = verb + (iparts.Count > 0 ? " " + string.Join(", ", iparts.ToArray()) : "...");
            if (!string.IsNullOrEmpty(itemText)) itext = itemText;   // caller-supplied caption (e.g. the watchdog's elapsed-time line)

            ProgressInfo p = new ProgressInfo();
            p.Phase = phase; p.Source = source ?? ""; p.Percent = percent; p.BytesDone = bytesDone; p.BytesTotal = bytesTotal;
            p.BytesPerSec = bytesPerSec; p.Index = index; p.Count = count; p.Current = current; p.ItemPercent = itemPercent;
            p.ItemBytesDone = itemBytesDone; p.ItemBytesTotal = itemBytesTotal; p.ItemText = itext; p.Text = text;
            p.Activity = string.IsNullOrEmpty(source) ? verb : verb + " (" + source + ")";
            return p;
        }
    }
}
