// Windows Update driver catalog through the Windows Update Agent COM API. Everything here runs on thread-pool (MTA)
// threads; the IUpdate objects found by the scan are reused by the install, so it does not search a second time.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Mdu
{
    public class WuInstallResult
    {
        public int DownloadResult, InstallResult;
        public bool RebootRequired;
        public List<KeyValuePair<string, int>> Details = new List<KeyValuePair<string, int>>();   // title -> ResultCode
        public double DownloadBytes, DownloadSeconds, InstallSeconds;
    }

    public static class WindowsUpdate
    {
        static object _session;
        static readonly object Lock = new object();

        static dynamic Session()
        {
            lock (Lock)
            {
                if (_session == null)
                {
                    dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true));
                    try { s.ClientApplicationID = AppInfo.Name; } catch { }
                    _session = s;
                }
                return _session;
            }
        }

        // Driver updates Windows Update offers for this exact hardware (signed, from Microsoft's catalog). Optional ones are
        // included and flagged. Usually 1-3 minutes; there is no progress API for a search.
        public static List<WuDriver> SearchDrivers()
        {
            dynamic searcher = Session().CreateUpdateSearcher();
            // Hidden ones too (flagged), so the window can list them under Hidden updates with Unhide.
            dynamic result = searcher.Search("IsInstalled=0 and Type='Driver'");
            List<WuDriver> list = new List<WuDriver>();
            dynamic updates = result.Updates;
            int count = updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic u = updates.Item(i);
                WuDriver d = new WuDriver();
                d.Title = Util.Str(u.Title);
                try { d.Manufacturer = Util.Str(u.DriverManufacturer); } catch { }
                try { d.Model = Util.Str(u.DriverModel); } catch { }
                try { d.Provider = Util.Str(u.DriverProvider); } catch { }
                try { d.Class = Util.Str(u.DriverClass); } catch { }
                try { d.HardwareId = Util.Str(u.DriverHardwareID); } catch { }
                try { object date = u.DriverVerDate; if (date is DateTime) d.VersionDate = ((DateTime)date).ToString("yyyy-MM-dd", Util.Inv); } catch { }
                try { d.Optional = (bool)u.BrowseOnly; } catch { }
                try { d.Hidden = (bool)u.IsHidden; } catch { }
                try { d.SizeMB = Math.Round(Num((object)u.MaxDownloadSize) / (1024.0 * 1024), 1); } catch { }
                d.Update = u;
                list.Add(d);
            }
            return list;
        }

        // Hides a driver update in Windows Update itself (or shows it again), so Windows does not install it on its own
        // either - the way to keep a driver away after Roll Back. Needs administrator rights. Call from a thread-pool (MTA)
        // thread: the update object belongs to the scan's MTA session.
        public static void SetHidden(WuDriver d, bool hidden)
        {
            if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Hiding a Windows Update driver requires administrator rights.");
            dynamic u = d.Update;
            u.IsHidden = hidden;
            d.Hidden = hidden;
            Log.Diag((hidden ? "Hid" : "Unhid") + " Windows Update driver: " + d.Title);
        }

        static double Num(object o)
        {
            try { return o == null ? 0 : Convert.ToDouble(o, Util.Inv); } catch { return 0; }
        }

        public static string ResultText(int code)
        {
            switch (code)
            {
                case 2: return "Succeeded";
                case 3: return "Succeeded with errors";
                case 4: return "Failed";
                case 5: return "Aborted";
                default: return "Code " + code;
            }
        }

        // Downloads and installs the given drivers. Needs administrator rights. onProgress gets download and install ticks.
        public static WuInstallResult Install(List<WuDriver> drivers, Action<ProgressInfo> onProgress)
        {
            if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Installing drivers requires administrator rights.");
            dynamic session = Session();
            dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true));
            List<string> titles = new List<string>();
            double bytes = 0;
            foreach (WuDriver d in drivers)
            {
                dynamic u = d.Update;
                try { if (!(bool)u.EulaAccepted) u.AcceptEula(); } catch { }
                coll.Add(u);
                titles.Add(Util.Str(u.Title));
                try { bytes += Num((object)u.MaxDownloadSize); } catch { }
            }
            WuInstallResult res = new WuInstallResult();
            if ((int)coll.Count == 0) return res;
            res.DownloadBytes = bytes;

            Stopwatch sw = Stopwatch.StartNew();
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = coll;
            dynamic dres = Download(downloader, titles, onProgress);
            res.DownloadSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

            sw.Restart();
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = coll;
            dynamic ires = InstallBatch(installer, titles, onProgress);
            res.InstallSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

            res.DownloadResult = (int)dres.ResultCode;
            res.InstallResult = (int)ires.ResultCode;
            res.RebootRequired = (bool)ires.RebootRequired;
            int n = coll.Count;
            for (int i = 0; i < n; i++)
            {
                // ResultCode: 2 = Succeeded, 3 = Succeeded with errors, 4 = Failed, 5 = Aborted
                res.Details.Add(new KeyValuePair<string, int>(Util.Str(coll.Item(i).Title), (int)ires.GetUpdateResult(i).ResultCode));
            }
            return res;
        }

        // Live progress when the async API accepts our callback object; otherwise the synchronous call with no numbers.
        static dynamic Download(dynamic downloader, List<string> titles, Action<ProgressInfo> onProgress)
        {
            dynamic job = null;
            try { WuaSink sink = new WuaSink(); job = downloader.BeginDownload(sink, sink, "mdu"); Log.Diag("BeginDownload: async job started."); }
            catch (Exception ex) { Log.Diag("BeginDownload failed: " + ex.Message); job = null; }
            if (job == null)
            {
                Log.Diag("Downloading synchronously (no live progress).");
                if (onProgress != null) onProgress(ProgressInfo.Create("Download", source: "Windows Update", count: titles.Count, current: string.Join(", ", titles.ToArray())));
                return downloader.Download();
            }
            SpeedMeter meter = new SpeedMeter();
            int ticks = 0;
            while (!(bool)job.IsCompleted)
            {
                if (onProgress != null)
                {
                    try
                    {
                        dynamic p = job.GetProgress();
                        int idx = (int)p.CurrentUpdateIndex;
                        int pct = (int)p.PercentComplete, itemPct = (int)p.CurrentUpdatePercentComplete;
                        double done = Num((object)p.TotalBytesDownloaded), total = Num((object)p.TotalBytesToDownload);
                        double itemDone = Num((object)p.CurrentUpdateBytesDownloaded), itemTotal = Num((object)p.CurrentUpdateBytesToDownload);
                        string cur = idx >= 0 && idx < titles.Count ? titles[idx] : "";
                        ProgressInfo info = ProgressInfo.Create("Download", percent: pct, bytesDone: done, bytesTotal: total,
                            bytesPerSec: meter.Measure(done), index: idx, count: titles.Count, current: cur, source: "Windows Update",
                            itemPercent: itemPct, itemBytesDone: itemDone, itemBytesTotal: itemTotal);
                        if (ticks == 0) Log.Diag("First download progress sample: " + info.Text);
                        onProgress(info);
                    }
                    catch (Exception ex) { if (ticks == 0) Log.Diag("GetProgress failed: " + ex.Message); }
                }
                ticks++;
                Thread.Sleep(400);
            }
            Log.Diag("Download job finished after " + ticks + " polls.");
            return downloader.EndDownload(job);
        }

        static dynamic InstallBatch(dynamic installer, List<string> titles, Action<ProgressInfo> onProgress)
        {
            dynamic job = null;
            try { WuaSink sink = new WuaSink(); job = installer.BeginInstall(sink, sink, "mdu"); Log.Diag("BeginInstall: async job started."); }
            catch (Exception ex) { Log.Diag("BeginInstall failed: " + ex.Message); job = null; }
            if (job == null)
            {
                Log.Diag("Installing synchronously (no live progress).");
                if (onProgress != null) onProgress(ProgressInfo.Create("Install", source: "Windows Update", count: titles.Count, current: string.Join(", ", titles.ToArray())));
                return installer.Install();
            }
            while (!(bool)job.IsCompleted)
            {
                if (onProgress != null)
                {
                    try
                    {
                        dynamic p = job.GetProgress();
                        int idx = (int)p.CurrentUpdateIndex;
                        int pct = (int)p.PercentComplete, itemPct = (int)p.CurrentUpdatePercentComplete;
                        string cur = idx >= 0 && idx < titles.Count ? titles[idx] : "";
                        onProgress(ProgressInfo.Create("Install", percent: pct, index: idx, count: titles.Count, current: cur, source: "Windows Update", itemPercent: itemPct));
                    }
                    catch { }
                }
                Thread.Sleep(400);
            }
            return installer.EndInstall(job);
        }
    }
}
