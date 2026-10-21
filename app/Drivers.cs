// Safety rails around driver installs: system restore point, per-driver backup/rollback with pnputil (built into Windows).
using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Mdu
{
    public static class Drivers
    {
        static string Pnputil { get { return Util.SystemTool("pnputil.exe"); } }

        // Creates a system restore point, or reports the one Windows already made in the last 24 hours (Windows makes at most
        // one a day for programs). Same WMI call Checkpoint-Computer makes. Needs administrator rights.
        public static RestorePointResult CreateRestorePoint(string description = AppInfo.Name)
        {
            if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Creating a restore point requires administrator rights.");
            ManagementScope scope = new ManagementScope(@"\\.\root\default");
            RestorePointResult r = new RestorePointResult();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Description, CreationTime FROM SystemRestore")))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementBaseObject o in rows)
                    {
                        using (o)
                        {
                            DateTime when;
                            try { when = ManagementDateTimeConverter.ToDateTime(Util.Str(o["CreationTime"])); } catch { continue; }
                            if (when > DateTime.Now.AddHours(-24))
                            {
                                r.Existing = true;
                                r.Message = "A restore point from the last 24 hours already exists (\"" + Util.Str(o["Description"]) + "\"); Windows will not create another one.";
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Diag("Listing restore points failed: " + ex.Message); }
            if (r.Existing) return r;

            using (ManagementClass cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
            using (ManagementBaseObject inParams = cls.GetMethodParameters("CreateRestorePoint"))
            {
                inParams["Description"] = description;
                inParams["RestorePointType"] = 12;   // MODIFY_SETTINGS
                inParams["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
                using (ManagementBaseObject outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null))
                {
                    int rv = Convert.ToInt32(outParams["ReturnValue"]);
                    if (rv != 0)
                    {
                        string why = rv == 1058 ? "System Protection is off for the Windows drive" : "error " + rv;
                        throw new InvalidOperationException("Windows could not create a restore point (" + why + ").");
                    }
                }
            }
            r.Created = true;
            r.Message = "Restore point created.";
            return r;
        }

        // Exports the currently installed driver package of each device into <dir>\<timestamp>\<oemNN>\ so it can be put back
        // with Restore. Inbox (Microsoft) drivers cannot be exported and never need to be: Windows keeps them.
        public static List<DriverBackup> Backup(List<Device> devices, string directory)
        {
            if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Backing up drivers requires administrator rights.");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", Util.Inv);
            string root = Path.Combine(directory, stamp);
            Directory.CreateDirectory(root);
            Dictionary<string, string> done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<DriverBackup> list = new List<DriverBackup>();
            foreach (Device d in devices)
            {
                DriverBackup rec = new DriverBackup();
                rec.Device = d.Name; rec.InstanceId = d.InstanceId; rec.Inf = d.Inf; rec.When = stamp; rec.HardwareId = d.HardwareId;
                list.Add(rec);
                if (string.IsNullOrEmpty(d.Inf) || !Rx.IsMatch(d.Inf, @"^oem\d+\.inf$")) { rec.Message = "Inbox driver, nothing to back up."; rec.Success = true; continue; }
                string prev;
                if (done.TryGetValue(d.Inf, out prev)) { rec.Path = prev; rec.Success = true; rec.Message = "Already exported."; continue; }
                string dest = Path.Combine(root, Rx.Replace(d.Inf, @"\.inf$", ""));
                Directory.CreateDirectory(dest);
                ProcResult r = Util.Run(Pnputil, "/export-driver " + d.Inf + " " + Util.QuoteArg(dest), 300000);
                if (r.ExitCode == 0 && Directory.GetFiles(dest, "*.inf", SearchOption.AllDirectories).Length > 0)
                {
                    rec.Path = dest; rec.Success = true; rec.Message = "Exported.";
                    done[d.Inf] = dest;
                    // A note of what this backup was for, next to the files.
                    Dictionary<string, string> meta = new Dictionary<string, string>();
                    meta["Device"] = rec.Device; meta["InstanceId"] = rec.InstanceId; meta["Inf"] = d.Inf; meta["HardwareId"] = d.HardwareId;
                    meta["Exported"] = DateTime.Now.ToString("s", Util.Inv);
                    File.WriteAllText(Path.Combine(dest, "mdu-backup.json"), new JavaScriptSerializer().Serialize(meta), new UTF8Encoding(false));
                }
                else
                {
                    string[] lines = r.Lines;
                    string tail = string.Join(" ", lines, Math.Max(0, lines.Length - 3), Math.Min(3, lines.Length)).Trim();
                    rec.Message = ("pnputil exit " + r.ExitCode + ": " + tail).Trim();
                }
            }
            return list;
        }

        // Puts a backed-up driver package back and reinstalls the device from it. Needs administrator rights.
        public static DriverRestoreResult Restore(string backupPath, string instanceId)
        {
            if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Restoring a driver requires administrator rights.");
            string[] infs = Directory.Exists(backupPath) ? Directory.GetFiles(backupPath, "*.inf", SearchOption.AllDirectories) : new string[0];
            if (infs.Length == 0) throw new FileNotFoundException("No .inf found under " + backupPath);
            DriverRestoreResult res = new DriverRestoreResult();
            res.Inf = Path.GetFileName(infs[0]);
            ProcResult r = Util.Run(Pnputil, "/add-driver " + Util.QuoteArg(infs[0]) + " /install", 600000);
            res.ExitCode = r.ExitCode;
            res.Output = r.Output;
            // 259 = no device matched (still added to the store), 3010 = done, restart needed
            res.Success = r.ExitCode == 0 || r.ExitCode == 259 || r.ExitCode == 3010;
            res.RebootRequired = r.ExitCode == 3010 || Rx.IsMatch(r.Output, "reboot|restart");
            if (res.Success && !string.IsNullOrEmpty(instanceId))
            {
                // Make sure the device actually uses the restored package (a newer-ranked one can win otherwise).
                ProcResult r2 = Util.Run(Pnputil, "/restart-device " + Util.QuoteArg(instanceId), 120000);
                res.Output += "\n" + r2.Output;
            }
            return res;
        }

        // Existing backups across several folders (newest first, each folder once).
        public static List<DriverBackup> ListBackups(string[] directories)
        {
            List<DriverBackup> all = new List<DriverBackup>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in directories)
                foreach (DriverBackup b in ListBackups(dir))
                    if (seen.Add(b.Path)) all.Add(b);
            all.Sort((a, b) => string.CompareOrdinal(b.Exported, a.Exported));
            return all;
        }

        // Existing backups in one folder (newest first).
        public static List<DriverBackup> ListBackups(string directory)
        {
            List<DriverBackup> list = new List<DriverBackup>();
            if (!Directory.Exists(directory)) return list;
            JavaScriptSerializer js = new JavaScriptSerializer();
            foreach (string meta in Directory.GetFiles(directory, "mdu-backup.json", SearchOption.AllDirectories))
            {
                try
                {
                    Dictionary<string, object> j = js.DeserializeObject(File.ReadAllText(meta)) as Dictionary<string, object>;
                    if (j == null) continue;
                    DriverBackup b = new DriverBackup();
                    object v;
                    b.Device = j.TryGetValue("Device", out v) ? Util.Str(v) : "";
                    b.InstanceId = j.TryGetValue("InstanceId", out v) ? Util.Str(v) : "";
                    b.Inf = j.TryGetValue("Inf", out v) ? Util.Str(v) : "";
                    b.HardwareId = j.TryGetValue("HardwareId", out v) ? Util.Str(v) : "";
                    b.Exported = j.TryGetValue("Exported", out v) ? Util.Str(v) : "";
                    b.Path = Path.GetDirectoryName(meta);
                    b.Success = true;
                    list.Add(b);
                }
                catch { }
            }
            list.Sort((a, b) => string.CompareOrdinal(b.Exported, a.Exported));
            return list;
        }

        // Pure parser for `pnputil /enum-drivers` output (key: value blocks). Unit tested.
        public static List<DriverPackage> ParsePnputilDrivers(IEnumerable<string> lines)
        {
            List<DriverPackage> results = new List<DriverPackage>();
            DriverPackage cur = null;
            foreach (string raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                Match m = Rx.Match(line, @"^Published Name\s*:\s*(.+)$");
                if (m.Success) { cur = new DriverPackage(); cur.PublishedName = m.Groups[1].Value.Trim(); results.Add(cur); continue; }
                if (cur == null) continue;
                if ((m = Rx.Match(line, @"^Original Name\s*:\s*(.+)$")).Success) { cur.OriginalName = m.Groups[1].Value.Trim(); continue; }
                if ((m = Rx.Match(line, @"^Provider Name\s*:\s*(.+)$")).Success) { cur.Provider = m.Groups[1].Value.Trim(); continue; }
                if ((m = Rx.Match(line, @"^Class Name\s*:\s*(.+)$")).Success) { cur.Class = m.Groups[1].Value.Trim(); continue; }
                if ((m = Rx.Match(line, @"^Driver Version\s*:\s*(.+)$")).Success)
                {
                    string v = m.Groups[1].Value.Trim();
                    Match dv = Rx.Match(v, @"^(\S+)\s+(\S+)$");
                    if (dv.Success) { cur.Date = dv.Groups[1].Value; cur.Version = dv.Groups[2].Value; } else cur.Version = v;
                    continue;
                }
                if ((m = Rx.Match(line, @"^Signer Name\s*:\s*(.+)$")).Success) { cur.Signer = m.Groups[1].Value.Trim(); continue; }
            }
            return results;
        }
    }
}
