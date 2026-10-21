// The scan. Three branches run at the same time, so the whole thing takes about as long as Windows Update's search alone:
//   main:   pre-flight -> devices -> graphics/peripherals -> nvidia.com
//   wu:     Windows Update driver search (the slow part; no progress API)
//   winget: winget upgrade list -> (after main) verify winget ids of missing vendor apps
// then the report. Each phase has an expected duration learned from earlier scans on this PC (scan-timing.json), and the
// window turns those into an honest percentage.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Mdu
{
    public class ScanPhase
    {
        public string Name, Branch, Label;
        public double Expected;
        public DateTime? Start, End;
    }

    public class ScanProgress
    {
        readonly object _lock = new object();
        readonly List<ScanPhase> _phases = new List<ScanPhase>();
        public DateTime Started = DateTime.UtcNow;
        double _shown;

        static readonly string[][] Defaults = {
            // name, branch, label, default expected seconds
            new[] { "preflight", "main", "checking the basics", "2" },
            new[] { "inventory", "main", "reading devices and drivers", "15" },
            new[] { "graphics", "main", "graphics and peripherals", "3" },
            new[] { "nvidia", "main", "asking nvidia.com", "5" },
            new[] { "wu", "wu", "asking Windows Update (usually 1-3 minutes)", "90" },
            new[] { "winget", "winget", "asking winget", "20" },
            new[] { "hints", "winget", "checking vendor apps", "5" },
            new[] { "report", "final", "writing the report", "2" },
        };

        public static string TimingPath { get { return Path.Combine(AppInfo.ReportDir, "scan-timing.json"); } }

        public ScanProgress()
        {
            Dictionary<string, object> saved = null;
            try { if (File.Exists(TimingPath)) saved = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(TimingPath)) as Dictionary<string, object>; } catch { }
            foreach (string[] d in Defaults)
            {
                ScanPhase p = new ScanPhase();
                p.Name = d[0]; p.Branch = d[1]; p.Label = d[2];
                p.Expected = double.Parse(d[3], Util.Inv);
                object v;
                if (saved != null && saved.TryGetValue(p.Name, out v)) { try { double x = Convert.ToDouble(v, Util.Inv); if (x > 0) p.Expected = x; } catch { } }
                _phases.Add(p);
            }
        }

        ScanPhase Find(string name) { foreach (ScanPhase p in _phases) if (p.Name == name) return p; return null; }

        public void Begin(string name) { lock (_lock) { ScanPhase p = Find(name); if (p != null && p.Start == null) p.Start = DateTime.UtcNow; } }
        public void End(string name) { lock (_lock) { ScanPhase p = Find(name); if (p != null) { if (p.Start == null) p.Start = DateTime.UtcNow; p.End = DateTime.UtcNow; } } }

        // Remaining time of a phase: falls linearly to 10% of its expected time at the expected time, then decays slowly,
        // so a slow PC sees the bar keep creeping instead of sitting at 99%.
        static double Remaining(ScanPhase p, DateTime now)
        {
            if (p.End != null) return 0;
            if (p.Start == null) return p.Expected;
            double t = (now - p.Start.Value).TotalSeconds, e = Math.Max(0.5, p.Expected);
            if (t <= e) return e - 0.9 * t;
            return 0.1 * e * Math.Exp(-(t - e) / e);
        }

        // 0-99 while running; never goes backwards.
        public int Percent()
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                Dictionary<string, double> branch = new Dictionary<string, double>();
                foreach (ScanPhase p in _phases)
                {
                    double r = Remaining(p, now);
                    double cur;
                    branch.TryGetValue(p.Branch, out cur);
                    branch[p.Branch] = cur + r;
                }
                double final, longest = 0;
                branch.TryGetValue("final", out final);
                foreach (KeyValuePair<string, double> kv in branch) if (kv.Key != "final" && kv.Value > longest) longest = kv.Value;
                double elapsed = (now - Started).TotalSeconds;
                double pct = elapsed + longest + final <= 0 ? 0 : 100.0 * elapsed / (elapsed + longest + final);
                if (pct > 99) pct = 99;
                if (pct > _shown) _shown = pct;
                return (int)Math.Floor(_shown);
            }
        }

        // What is happening right now, e.g. "Checking: asking Windows Update (usually 1-3 minutes), asking winget".
        public string Status()
        {
            lock (_lock)
            {
                List<string> active = new List<string>();
                foreach (ScanPhase p in _phases) if (p.Start != null && p.End == null) active.Add(p.Label);
                if (active.Count == 0) return "Starting...";
                return "Checking: " + string.Join(", ", active.ToArray()) + "...";
            }
        }

        // Remember how long each phase took (smoothed with last time) so the next scan's percentage fits this PC.
        public Dictionary<string, double> SaveTiming()
        {
            Dictionary<string, double> actual = new Dictionary<string, double>();
            Dictionary<string, double> merged = new Dictionary<string, double>();
            lock (_lock)
            {
                foreach (ScanPhase p in _phases)
                {
                    if (p.Start != null && p.End != null)
                    {
                        double a = Math.Round((p.End.Value - p.Start.Value).TotalSeconds, 1);
                        actual[p.Name] = a;
                        merged[p.Name] = Math.Round(0.3 * p.Expected + 0.7 * a, 1);
                    }
                    else merged[p.Name] = p.Expected;
                }
            }
            try { File.WriteAllText(TimingPath, Report.Json(merged)); } catch { }
            return actual;
        }
    }

    public static class Scanner
    {
        static T Phase<T>(ScanProgress prog, string name, Func<T> work)
        {
            prog.Begin(name);
            try { return work(); } finally { prog.End(name); }
        }

        public static ScanResult Run(ScanProgress prog)
        {
            ScanResult r = new ScanResult();
            prog.Started = DateTime.UtcNow;
            InstalledApps.All(true);   // fresh list: an app may have been installed or updated since the last scan
            try
            {
                Task<List<WuDriver>> wuTask = Task.Run(() => Phase(prog, "wu", () =>
                {
                    try { return WindowsUpdate.SearchDrivers(); }
                    catch (Exception ex) { r.WuError = Errors.Text(ex); Log.Diag("Windows Update search failed: " + r.WuError); return new List<WuDriver>(); }
                }));

                Task mainTask = Task.Run(() =>
                {
                    r.Preflight = Phase(prog, "preflight", () => { try { return Preflight.Run(); } catch (Exception ex) { Log.Diag("Pre-flight failed: " + ex.Message); return new List<PreflightCheck>(); } });
                    Phase(prog, "inventory", () =>
                    {
                        r.System = Inventory.GetSystemInfo();
                        r.Inventory = Inventory.GetDevices();
                        r.DeviceCount = r.Inventory.Count;
                        r.Problems = Inventory.ProblemDevices(r.Inventory);
                        r.Backups = Drivers.ListBackups(AppInfo.AllBackupDirs);
                        r.History = History.Load();
                        return 0;
                    });
                    Phase(prog, "graphics", () =>
                    {
                        r.Gpus = Inventory.Gpus();
                        r.Peripherals = Inventory.Peripherals(r.Inventory);
                        return 0;
                    });
                    Gpu nvGpu = r.Gpus.Find(g => g.Vendor == "NVIDIA");
                    if (nvGpu != null) r.Nvidia = Phase(prog, "nvidia", () => Nvidia.Lookup(nvGpu.Name, nvGpu.FriendlyVersion));
                    else { prog.Begin("nvidia"); prog.End("nvidia"); }
                    // A version the person chose to skip is not offered; a newer one is.
                    if (r.Nvidia != null && r.Nvidia.Available && r.Nvidia.Version == Skips.Get("nvidia"))
                    {
                        r.Nvidia.Available = false;
                        r.Nvidia.Skipped = true;
                        r.SkippedNvidia = r.Nvidia.Version;
                    }
                });

                Task wingetTask = Task.Run(() =>
                {
                    r.Apps = Phase(prog, "winget", () =>
                    {
                        try
                        {
                            List<WingetUpgrade> ups = Winget.Upgrades();
                            try { r.PinnedApps = Winget.Pins(); } catch (Exception ex) { Log.Diag("winget pin list failed: " + ex.Message); }
                            return ups;
                        }
                        catch (Exception ex) { r.WingetError = Errors.Text(ex); Log.Diag("winget listing failed: " + r.WingetError); return new List<WingetUpgrade>(); }
                    });
                    try { mainTask.Wait(); } catch { return; }
                    // Verify winget ids for any vendor app that is missing, so the hint shown is one that actually works.
                    Phase(prog, "hints", () =>
                    {
                        if (r.WingetError != null) return 0;
                        List<string> ids = new List<string>();
                        foreach (Gpu g in r.Gpus) if (!string.IsNullOrEmpty(g.WingetId) && !g.AppInstalled && !ids.Contains(g.WingetId)) ids.Add(g.WingetId);
                        foreach (Peripheral p in r.Peripherals) if (p.Present && !string.IsNullOrEmpty(p.WingetId) && !p.AppInstalled && !ids.Contains(p.WingetId)) ids.Add(p.WingetId);
                        foreach (string id in ids) r.WingetHints[id] = Winget.IdExists(id);
                        return 0;
                    });
                });

                Task.WaitAll(mainTask, wuTask, wingetTask);
                List<WuDriver> wu = wuTask.Result;

                // nvidia.com answered, or a GPU vendor app is installed: that owns the GPU driver, so Windows Update's (usually
                // older) display driver is left out.
                List<string> owners = new List<string>();
                foreach (Gpu g in r.Gpus)
                {
                    if (!g.AppInstalled) continue;
                    if (g.Vendor == "NVIDIA") owners.Add("NVIDIA");
                    else if (g.Vendor == "AMD") owners.Add("AMD|Advanced Micro|Radeon");
                    else if (g.Vendor == "Intel") owners.Add("Intel");
                }
                if (r.Nvidia != null && !string.IsNullOrEmpty(r.Nvidia.Version) && !owners.Contains("NVIDIA")) owners.Add("NVIDIA");
                foreach (WuDriver d in wu)
                {
                    if (d.Hidden) { r.HiddenDrivers.Add(d); continue; }   // hidden by the person, in Windows Update itself
                    bool deferred = false;
                    if (Rx.IsMatch(d.Class, "Display")) foreach (string o in owners) if (Rx.IsMatch((d.Manufacturer ?? "") + " " + d.Title, o)) deferred = true;
                    if (deferred) r.DeferredGpu.Add(d.Title); else r.Drivers.Add(d);
                }

                Phase(prog, "report", () =>
                {
                    List<string> notes = new List<string>();
                    foreach (string t in r.DeferredGpu) notes.Add("Left GPU driver '" + t + "' to nvidia.com / the vendor app instead of Windows Update.");
                    if (r.WuError != null) notes.Add("Windows Update check failed: " + r.WuError);
                    if (r.WingetError != null) notes.Add("winget check failed: " + r.WingetError);
                    notes.Add("Peripheral firmware: the version shown is what the device reports now; the vendor app is the only place that knows whether a newer one exists.");
                    try { r.ReportHtml = Report.Write(r, notes); Report.Prune(); } catch (Exception ex) { Log.Diag("Report failed: " + ex.Message); }
                    return 0;
                });
            }
            catch (Exception ex)
            {
                r.Error = Errors.Text(ex);
                Log.Error("Scan failed: " + ex);
            }
            r.Timing = prog.SaveTiming();
            r.Finished = DateTime.Now;
            r.Seconds = (DateTime.UtcNow - prog.Started).TotalSeconds;
            Log.Diag(string.Format(Util.Inv, "Scan done in {0:0.0} s: {1} devices, {2} problem(s), {3} WU driver(s), {4} app(s), NVIDIA {5}",
                r.Seconds, r.DeviceCount, r.Problems.Count, r.Drivers.Count, r.Apps.Count,
                r.Nvidia == null ? "n/a" : (r.Nvidia.Error ?? r.Nvidia.Version)));
            return r;
        }
    }
}
