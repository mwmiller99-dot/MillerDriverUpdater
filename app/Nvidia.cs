// NVIDIA GeForce driver straight from NVIDIA - no NVIDIA App, no sign-in. Uses the product catalogue and driver-manifest
// service nvidia.com's own download page calls, downloads from *.nvidia.com, verifies the Authenticode signature
// (NVIDIA Corporation) before anything runs, and installs only the driver components.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace Mdu
{
    public static class Nvidia
    {
        const string UserAgent = "Mozilla/5.0 MillerDriverUpdater";

        static string HttpGet(string url)
        {
            Util.EnsureTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string NodeText(XmlNode n, string name)
        {
            XmlAttribute a = n.Attributes == null ? null : n.Attributes[name];
            if (a != null) return a.Value;
            XmlNode c = n.SelectSingleNode(name);
            return c == null ? null : c.InnerText;
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static NvidiaDriver Lookup(string gpuName, string installedVersion, bool studio = false)
        {
            NvidiaDriver info = new NvidiaDriver();
            info.Gpu = gpuName;
            info.Installed = installedVersion;
            try
            {
                string clean = Rx.Replace(gpuName ?? "", @"^\s*NVIDIA\s+", "").Trim();
                info.Notebook = Rx.IsMatch(clean, "Laptop|Notebook|Max-Q|Mobile");
                // 1) product catalogue: every GPU with its product id (Value) and series id (ParentID)
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(HttpGet("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3"));
                XmlNode hit = null;
                XmlNodeList all = xml.SelectNodes("//LookupValue");
                foreach (XmlNode n in all) if (string.Equals(NodeText(n, "Name"), clean, StringComparison.OrdinalIgnoreCase)) { hit = n; break; }
                if (hit == null)
                {
                    // tolerate small naming differences (e.g. "Laptop GPU" vs "Notebook")
                    string alt = Rx.Replace(Rx.Replace(clean, @"\s*Laptop GPU$", ""), @"\s*\(Notebook\)$", "");
                    foreach (XmlNode n in all)
                    {
                        string nm = NodeText(n, "Name") ?? "";
                        if (string.Equals(Rx.Replace(nm, @"\s*\(Notebooks?\)$", ""), alt, StringComparison.OrdinalIgnoreCase) && Rx.IsMatch(nm, "Notebook") == info.Notebook) { hit = n; break; }
                    }
                }
                if (hit != null) { info.Pfid = NodeText(hit, "Value"); info.Psid = NodeText(hit, "ParentID"); info.Lookup = "exact"; }
                else
                {
                    // unknown name: ask for the current unified GeForce driver via a known product; the installer refuses unsupported hardware
                    info.Pfid = "929"; info.Psid = "120"; info.Lookup = "generic";
                    Log.Diag("NVIDIA lookup: '" + clean + "' not in the product list; using the generic GeForce driver lookup.");
                }
                // 2) driver manifest for that product (DriverManualLookup: the call nvidia.com's download page makes)
                string url = string.Format(Util.Inv,
                    "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid={0}&pfid={1}&osID=57&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD={2}&qnf=0&sort1=0&numberOfResults=1",
                    info.Psid, info.Pfid, studio ? 1 : 0);
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> json = js.DeserializeObject(HttpGet(url)) as Dictionary<string, object>;
                object[] ids = Get(json, "IDS") as object[];
                if (Util.Str(Get(json, "Success")) != "1" || ids == null || ids.Length == 0) throw new InvalidOperationException("NVIDIA returned no driver for this product.");
                Dictionary<string, object> d = Get(ids[0] as Dictionary<string, object>, "downloadInfo") as Dictionary<string, object>;
                if (d == null) throw new InvalidOperationException("NVIDIA's answer had no download information.");
                info.Version = Util.Str(Get(d, "Version"));
                string name = Util.Str(Get(d, "Name"));
                if (name.Length > 0) info.Name = Uri.UnescapeDataString(name);
                info.ReleaseDate = Util.Str(Get(d, "ReleaseDateTime"));
                string dl = Util.Str(Get(d, "DownloadURL"));
                if (dl.Length > 0 && !Rx.IsMatch(dl, "^https?://")) dl = "https:" + dl;
                if (dl.Length == 0)
                    dl = string.Format("https://us.download.nvidia.com/Windows/{0}/{0}-{1}-win10-win11-64bit-international-dch-whql.exe", info.Version, info.Notebook ? "notebook" : "desktop");
                info.Url = dl;
                info.SizeBytes = Util.ParseSizeText(Util.Str(Get(d, "DownloadURLFileSize")));
                if (!Rx.IsMatch(info.Url, @"^https://[a-z0-9.-]*nvidia\.com/")) throw new InvalidOperationException("Unexpected download host in NVIDIA's answer: " + info.Url);
                if (info.Version.Length > 0)
                {
                    if (!string.IsNullOrEmpty(installedVersion))
                    {
                        Version a, b;
                        if (Version.TryParse(info.Version, out a) && Version.TryParse(installedVersion, out b)) info.Available = a > b;
                        else info.Available = info.Version != installedVersion;
                    }
                    else info.Available = true;
                }
                Log.Diag(string.Format("NVIDIA lookup: gpu='{0}' psid={1} pfid={2} ({3}) latest={4} installed={5} url={6}", clean, info.Psid, info.Pfid, info.Lookup, info.Version, installedVersion, info.Url));
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
                Log.Diag("NVIDIA lookup failed: " + ex.Message);
            }
            return info;
        }

        // Downloads a file with progress callbacks. Returns the byte count.
        public static double Download(string url, string path, string label, Action<ProgressInfo> onProgress)
        {
            Util.EnsureTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            double done = 0, total;
            using (WebResponse resp = req.GetResponse())
            using (Stream input = resp.GetResponseStream())
            using (FileStream output = File.Create(path))
            {
                total = resp.ContentLength;
                SpeedMeter meter = new SpeedMeter();
                byte[] buf = new byte[262144];
                DateTime last = DateTime.UtcNow.AddSeconds(-1);
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    done += n;
                    if (onProgress != null && (DateTime.UtcNow - last).TotalMilliseconds >= 300)
                    {
                        last = DateTime.UtcNow;
                        int pct = total > 0 ? (int)Math.Min(100, Math.Round(100.0 * done / total)) : -1;
                        onProgress(ProgressInfo.Create("Download", percent: pct, bytesDone: done, bytesTotal: total, bytesPerSec: meter.Measure(done), current: label, source: "NVIDIA"));
                    }
                }
            }
            if (onProgress != null) onProgress(ProgressInfo.Create("Download", percent: 100, bytesDone: done, bytesTotal: total > 0 ? total : done, current: label, source: "NVIDIA"));
            return done;
        }

        // True only when the file carries a valid Authenticode signature from NVIDIA Corporation.
        public static bool SignatureOk(string path)
        {
            try
            {
                string subject; int code;
                bool valid = Authenticode.Verify(path, out subject, out code);
                bool ok = valid && Rx.IsMatch(subject ?? "", "NVIDIA Corporation");
                Log.Diag(string.Format("NVIDIA installer signature: code 0x{0:X8} / {1}", code, subject));
                return ok;
            }
            catch (Exception ex) { Log.Diag("Signature check failed: " + ex.Message); return false; }
        }

        public static string Find7Zip()
        {
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"7-Zip\7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"7-Zip\7z.exe")
            };
            foreach (string p in candidates) if (File.Exists(p)) return p;
            return null;
        }

        // Downloads, verifies and silently installs the driver from Lookup(). Display driver + HD audio + PhysX only.
        public static NvidiaInstallResult Install(NvidiaDriver info, string workDir, Action<ProgressInfo> onProgress, bool keepDownload = false)
        {
            if (!Util.IsAdmin()) throw new UnauthorizedAccessException("Installing the NVIDIA driver requires administrator rights.");
            string label = "NVIDIA " + info.Name + " " + info.Version;
            NvidiaInstallResult res = new NvidiaInstallResult();
            res.Version = info.Version;
            Directory.CreateDirectory(workDir);
            string pkg = Path.Combine(workDir, "nvidia-" + info.Version + ".exe");

            // 1) download (reuse a previous, valid download if it is there)
            Stopwatch sw = Stopwatch.StartNew();
            if (File.Exists(pkg) && SignatureOk(pkg)) res.DownloadBytes = new FileInfo(pkg).Length;
            else res.DownloadBytes = Download(info.Url, pkg, label, onProgress);
            res.DownloadSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

            // 2) verify before anything executes
            if (onProgress != null) onProgress(ProgressInfo.Create("Install", current: label, source: "NVIDIA", itemText: "Checking NVIDIA's signature..."));
            if (!SignatureOk(pkg))
            {
                try { File.Delete(pkg); } catch { }
                res.Message = "The downloaded installer is not validly signed by NVIDIA Corporation; it was deleted and nothing was installed.";
                Log.Diag(res.Message);
                return res;
            }

            // 3) extract only the driver parts (no NVIDIA App). 7-Zip comes from winget if it is missing.
            sw.Restart();
            if (onProgress != null) onProgress(ProgressInfo.Create("Install", current: label, source: "NVIDIA", itemText: "Unpacking the driver..."));
            string sevenZip = Find7Zip();
            if (sevenZip == null)
            {
                try
                {
                    Log.Diag("7-Zip missing; installing it with winget.");
                    Winget.Invoke(new[] { "install", "--id", "7zip.7zip", "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" });
                    sevenZip = Find7Zip();
                }
                catch { }
            }
            string extract = Path.Combine(workDir, "nvidia-" + info.Version);
            string setup = null;
            if (sevenZip != null)
            {
                try { if (Directory.Exists(extract)) Directory.Delete(extract, true); } catch { }
                Directory.CreateDirectory(extract);
                ProcResult z = Util.Run(sevenZip, "x -bso0 -bsp0 -bse1 -aoa " + Util.QuoteArg(pkg) + " Display.Driver HDAudio NVI2 PhysX EULA.txt ListDevices.txt setup.cfg setup.exe " + Util.QuoteArg("-o" + extract), 15 * 60 * 1000);
                Log.Diag("7-Zip extract exit " + z.ExitCode);
                string cfg = Path.Combine(extract, "setup.cfg");
                if (z.ExitCode == 0 && File.Exists(cfg) && File.Exists(Path.Combine(extract, "setup.exe")))
                {
                    // These entries reference files we did not extract (consent/EULA/privacy pages); setup refuses to start with them present.
                    List<string> keep = new List<string>();
                    foreach (string line in File.ReadAllLines(cfg))
                        if (!Rx.IsMatch(line, "name=\"\\$\\{\\{(EulaHtmlFile|FunctionalConsentFile|PrivacyPolicyFile)\\}\\}\"")) keep.Add(line);
                    File.WriteAllLines(cfg, keep.ToArray(), new UTF8Encoding(true));
                    setup = Path.Combine(extract, "setup.exe");
                    res.Mode = "extracted (driver only)";
                }
            }
            if (setup == null)
            {
                // No 7-Zip: NVIDIA's own package, silently (it may also install the NVIDIA App; it can be removed afterwards).
                setup = pkg;
                res.Mode = "full package";
                Log.Diag("Running the full NVIDIA package silently (7-Zip unavailable).");
            }

            // 4) silent install, with an elapsed-time caption so a long setup never looks frozen
            const string setupArgs = "-passive -noreboot -noeula -nofinish -s";
            Log.Diag("NVIDIA setup: \"" + setup + "\" " + setupArgs);
            ProcessStartInfo psi = new ProcessStartInfo(setup, setupArgs);
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi))
            {
                while (!p.WaitForExit(5000))
                {
                    if (onProgress != null) onProgress(ProgressInfo.Create("Install", current: label, source: "NVIDIA",
                        itemText: "Installing... " + Util.FormatDuration(sw.Elapsed.TotalSeconds) + " (the screen may flicker while the display driver swaps)"));
                }
                res.ExitCode = p.ExitCode;
            }
            res.InstallSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            // 0 = ok, 1 = ok but reboot required (NVIDIA setup convention)
            res.Success = res.ExitCode == 0 || res.ExitCode == 1;
            res.RebootRequired = res.ExitCode == 1 || Util.PendingReboot();
            res.Message = res.Success ? "Installed " + info.Version + " (" + res.Mode + ")." : "NVIDIA setup exited with code " + res.ExitCode + ".";
            Log.Diag(res.Message);
            if (!keepDownload)
            {
                try { Directory.Delete(extract, true); } catch { }
                // The package is ~1 GB: keep it only when setup failed, so a retry does not download it again.
                if (res.Success) foreach (string old in Directory.GetFiles(workDir, "nvidia-*.exe")) { try { File.Delete(old); } catch { } }
            }
            return res;
        }
    }
}
