// Installing what was ticked: restore point -> backups of the drivers being replaced -> Windows Update drivers -> NVIDIA
// -> apps, one at a time. Per-item state (keyed by driver title / winget id / "nvidia") and an overall figure are kept
// here for the window's timer to read.
using System;
using System.Collections.Generic;

namespace Mdu
{
    public class ItemState
    {
        public string State = "queued";   // queued / active / done / failed
        public int Percent = -1;
        public string Text = "";
    }

    public class InstallRequest
    {
        public List<WuDriver> Drivers = new List<WuDriver>();
        public List<string> WingetIds = new List<string>();
        public Dictionary<string, string> WingetNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> WingetVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // the version being installed, for History
        public NvidiaDriver Nvidia;
        public bool SkipRestorePoint;
    }

    public class DriverOutcome { public string Title, Text; public int Code; }
    public class AppOutcome { public string Id; public bool Success; public int Code; }
    public class InstallStat { public string What; public double Bytes, DownloadSeconds, InstallSeconds; }

    public class InstallResult
    {
        public string RestorePoint, Error;
        public bool RestoreFailed, Reboot;
        public List<DriverOutcome> Drivers = new List<DriverOutcome>();
        public List<AppOutcome> Apps = new List<AppOutcome>();
        public List<DriverBackup> Backups = new List<DriverBackup>();
        public NvidiaInstallResult Nvidia;
        public List<InstallStat> Stats = new List<InstallStat>();
    }

    public class InstallJob
    {
        readonly object _lock = new object();
        readonly Dictionary<string, ItemState> _items = new Dictionary<string, ItemState>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, double> _fraction = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        int _overall = -1;
        string _status = "Preparing...";
        string _key;                 // item a non-WU progress tick belongs to
        List<string> _wuOrder = new List<string>();

        public int Overall { get { lock (_lock) return _overall; } }
        public string Status { get { lock (_lock) return _status; } set { lock (_lock) _status = value; } }

        public Dictionary<string, ItemState> Snapshot()
        {
            lock (_lock)
            {
                Dictionary<string, ItemState> copy = new Dictionary<string, ItemState>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, ItemState> kv in _items)
                {
                    ItemState s = new ItemState();
                    s.State = kv.Value.State; s.Percent = kv.Value.Percent; s.Text = kv.Value.Text;
                    copy[kv.Key] = s;
                }
                return copy;
            }
        }

        void Set(string key, string state, int pct, string text)
        {
            lock (_lock)
            {
                ItemState s;
                if (!_items.TryGetValue(key, out s)) { s = new ItemState(); _items[key] = s; }
                s.State = state; s.Percent = pct; s.Text = text ?? "";
            }
        }

        void Fraction(string key, double f, bool onlyForward = false)
        {
            lock (_lock)
            {
                double cur;
                _fraction.TryGetValue(key, out cur);
                if (onlyForward && f <= cur) return;   // never let the overall figure go backwards
                _fraction[key] = f;
                double sum = 0;
                foreach (double v in _fraction.Values) sum += v;
                if (_fraction.Count > 0) _overall = (int)Math.Round(100.0 * sum / _fraction.Count);
            }
        }

        // Engine progress ticks -> per-item state + overall. Driver items: download = first half, install = second half.
        void OnProgress(ProgressInfo p)
        {
            Status = p.Text;
            int ipct = p.ItemPercent;
            if (p.Source == "Windows Update")
            {
                List<string> order = _wuOrder;
                if (ipct < 0 && order.Count > 1 && p.Percent < 0)
                {
                    // synchronous fallback: no per-item numbers, everything in the batch is "in progress"
                    foreach (string t in order) Set(t, "active", -1, p.ItemText);
                    return;
                }
                double f = ipct >= 0 ? ipct / 100.0 : 0;
                for (int j = 0; j < order.Count; j++)
                {
                    string t = order[j];
                    if (p.Phase == "Download")
                    {
                        if (j < p.Index) { Fraction(t, 0.5, true); Set(t, "active", 100, "Downloaded, waiting to install"); }
                        else if (j == p.Index) { Fraction(t, 0.5 * f, true); Set(t, "active", ipct, p.ItemText); }
                    }
                    else
                    {
                        if (j < p.Index) { Fraction(t, 1.0, true); Set(t, "active", 100, "Installed"); }
                        else if (j == p.Index) { Fraction(t, 0.5 + 0.5 * f, true); Set(t, "active", ipct, p.ItemText); }
                        else { Fraction(t, 0.5, true); Set(t, "active", 100, "Downloaded, waiting to install"); }
                    }
                }
            }
            else if (_key != null)
            {
                double f = 0.9;
                if (p.Phase == "Download") f = 0.8 * (ipct >= 0 ? ipct / 100.0 : 0);
                Fraction(_key, f, true);
                Set(_key, "active", ipct, p.ItemText);
            }
        }

        static string Version(InstallRequest req, string id)
        {
            string v;
            return req.WingetVersions.TryGetValue(id, out v) ? v : "";
        }

        public InstallResult Run(InstallRequest req)
        {
            InstallResult res = new InstallResult();
            Log.Diag(string.Format("Install: {0} driver(s), NVIDIA: {1}, {2} app(s), skip restore point: {3}", req.Drivers.Count, req.Nvidia != null, req.WingetIds.Count, req.SkipRestorePoint));
            foreach (WuDriver d in req.Drivers) { Set(d.Title, "queued", -1, ""); Fraction(d.Title, 0); }
            if (req.Nvidia != null) { Set("nvidia", "queued", -1, ""); Fraction("nvidia", 0); }
            foreach (string id in req.WingetIds) { Set(id, "queued", -1, ""); Fraction(id, 0); }
            Action<ProgressInfo> progress = OnProgress;
            try
            {
                bool driversWanted = req.Drivers.Count > 0 || req.Nvidia != null;
                if (driversWanted && !req.SkipRestorePoint)
                {
                    Status = "Creating a system restore point (this can take a minute)...";
                    try { res.RestorePoint = Drivers.CreateRestorePoint().Message; }
                    catch (Exception ex) { res.RestoreFailed = true; res.RestorePoint = Errors.Text(ex); Log.Diag("Restore point failed: " + res.RestorePoint); }
                }
                bool driversGo = req.SkipRestorePoint || !res.RestoreFailed;

                if (driversGo && req.Drivers.Count > 0) InstallWindowsUpdate(req, res, progress);

                if (driversGo && req.Nvidia != null)
                {
                    // GeForce driver straight from nvidia.com: download with progress, verify NVIDIA's signature, install driver-only.
                    _key = "nvidia";
                    string label = "NVIDIA " + req.Nvidia.Name + " " + req.Nvidia.Version;
                    Status = "Downloading " + label + " from nvidia.com...";
                    Set("nvidia", "active", -1, "Connecting to nvidia.com...");
                    try
                    {
                        NvidiaInstallResult nr = Nvidia.Install(req.Nvidia, AppInfo.DownloadDir, progress);
                        res.Nvidia = nr;
                        InstallStat st = new InstallStat(); st.What = "NVIDIA driver"; st.Bytes = nr.DownloadBytes; st.DownloadSeconds = nr.DownloadSeconds; st.InstallSeconds = nr.InstallSeconds;
                        res.Stats.Add(st);
                        if (nr.RebootRequired) res.Reboot = true;
                        string summary = nr.Message;
                        if (nr.Success && nr.DownloadBytes > 0 && nr.DownloadSeconds > 0)
                            summary = string.Format(Util.Inv, "Installed {0}: {1} in {2} s ({3}), setup {4} s", nr.Version, Util.FormatBytes(nr.DownloadBytes), nr.DownloadSeconds, Util.FormatSpeed(nr.DownloadBytes / nr.DownloadSeconds), nr.InstallSeconds);
                        Set("nvidia", nr.Success ? "done" : "failed", 100, nr.Success ? summary : Errors.Friendly(nr.Message, true));
                        History.Add("NVIDIA " + req.Nvidia.Name, req.Nvidia.Version, "nvidia.com", nr.Success, nr.Success ? "Installed" : Errors.Friendly(nr.Message, true));
                    }
                    catch (Exception ex)
                    {
                        NvidiaInstallResult nr = new NvidiaInstallResult();
                        nr.Version = req.Nvidia.Version; nr.Message = Errors.Text(ex);
                        res.Nvidia = nr;
                        Set("nvidia", "failed", 100, Errors.Friendly(nr.Message, true));
                        History.Add("NVIDIA " + req.Nvidia.Name, req.Nvidia.Version, "nvidia.com", false, Errors.Friendly(nr.Message, true));
                        Log.Diag("NVIDIA install failed: " + ex);
                    }
                    Fraction("nvidia", 1.0);
                    _key = null;
                }

                foreach (string id in req.WingetIds)
                {
                    string name;
                    if (!req.WingetNames.TryGetValue(id, out name)) name = id;
                    Status = "Updating " + name + "...";
                    _key = id;
                    Set(id, "active", -1, "Starting...");
                    AppOutcome outcome = new AppOutcome();
                    outcome.Id = id;
                    try
                    {
                        WingetResult r = Winget.Upgrade(id, name, true, progress);
                        outcome.Success = r.Success; outcome.Code = r.ExitCode;
                        InstallStat st = new InstallStat(); st.What = name; st.Bytes = r.DownloadBytes; st.DownloadSeconds = r.Seconds;
                        res.Stats.Add(st);
                        string summary = "Updated";
                        if (r.DownloadBytes > 0 && r.Seconds > 0)
                            summary = string.Format(Util.Inv, "Updated: {0} in {1} s ({2})", Util.FormatBytes(r.DownloadBytes), r.Seconds, Util.FormatSpeed(r.DownloadBytes / r.Seconds));
                        Set(id, r.Success ? "done" : "failed", 100, r.Success ? summary : Errors.WingetExitText(r.ExitCode, r.Output));
                        History.Add(name, Version(req, id), "winget", r.Success, r.Success ? "Updated" : Errors.WingetExitText(r.ExitCode, r.Output));
                    }
                    catch (Exception ex)
                    {
                        outcome.Success = false; outcome.Code = -1;
                        Set(id, "failed", 100, Errors.Friendly(Errors.Text(ex), true));
                        History.Add(name, Version(req, id), "winget", false, Errors.Friendly(Errors.Text(ex), true));
                    }
                    res.Apps.Add(outcome);
                    Fraction(id, 1.0);
                    _key = null;
                }
                if (!res.Reboot) res.Reboot = Util.PendingReboot();
            }
            catch (Exception ex)
            {
                res.Error = Errors.Text(ex);
                Log.Error("Install failed: " + ex);
            }
            return res;
        }

        void InstallWindowsUpdate(InstallRequest req, InstallResult res, Action<ProgressInfo> progress)
        {
            List<WuDriver> found = req.Drivers;
            _wuOrder = new List<string>();
            foreach (WuDriver d in found) { _wuOrder.Add(d.Title); Set(d.Title, "active", -1, "Waiting..."); }

            // Back up the drivers being replaced (pnputil /export-driver) so each one can be rolled back.
            Status = "Backing up the drivers being replaced...";
            try
            {
                List<Device> inv = Inventory.GetDevices();
                List<Device> affected = new List<Device>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (WuDriver d in found)
                    foreach (Device dev in Inventory.DevicesForDriver(d.HardwareId, inv))
                        if (seen.Add(dev.InstanceId)) affected.Add(dev);
                if (affected.Count > 0) res.Backups.AddRange(Drivers.Backup(affected, AppInfo.BackupDir));
            }
            catch (Exception ex)
            {
                DriverBackup b = new DriverBackup(); b.Device = "(all)"; b.Success = false; b.Message = Errors.Text(ex);
                res.Backups.Add(b);
            }

            Status = "Downloading and installing drivers from Windows Update...";
            WuInstallResult r;
            try { r = WindowsUpdate.Install(found, progress); }
            catch (Exception ex)
            {
                // The scan's update objects can go stale (Windows Update itself installed something meanwhile): search again once.
                Log.Diag("Install with the scan's update objects failed (" + Errors.Text(ex) + "); searching again.");
                Status = "Looking up the drivers again...";
                List<string> titles = new List<string>(_wuOrder);
                List<WuDriver> again = WindowsUpdate.SearchDrivers().FindAll(d => titles.Contains(d.Title));
                foreach (string t in titles)
                {
                    if (again.Exists(d => d.Title == t)) continue;
                    Fraction(t, 1.0); Set(t, "done", 100, "No longer offered by Windows Update");
                    DriverOutcome gone = new DriverOutcome(); gone.Title = t; gone.Code = 0; gone.Text = "No longer offered";
                    res.Drivers.Add(gone);
                }
                if (again.Count == 0) return;
                _wuOrder = again.ConvertAll(d => d.Title);
                r = WindowsUpdate.Install(again, progress);
            }
            foreach (KeyValuePair<string, int> d in r.Details)
            {
                string txt = WindowsUpdate.ResultText(d.Value);
                DriverOutcome o = new DriverOutcome(); o.Title = d.Key; o.Code = d.Value; o.Text = txt;
                res.Drivers.Add(o);
                Fraction(d.Key, 1.0);
                Set(d.Key, d.Value == 2 ? "done" : "failed", 100, txt);
                History.Add(d.Key, "", "Windows Update", d.Value == 2, txt);
            }
            if (r.RebootRequired) res.Reboot = true;
            InstallStat st = new InstallStat(); st.What = "Drivers"; st.Bytes = r.DownloadBytes; st.DownloadSeconds = r.DownloadSeconds; st.InstallSeconds = r.InstallSeconds;
            res.Stats.Add(st);
        }
    }
}
