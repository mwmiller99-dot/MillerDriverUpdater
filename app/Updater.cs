// Self-update from GitHub Releases. The app asks api.github.com for the repository's latest release; when its version is
// newer, the window shows a one-line banner. Update downloads the release's Setup.exe from github.com, checks its SHA-256
// against the digest GitHub publishes for that file (and, once the app is code-signed, the publisher signature), then
// runs Setup quietly with /UPDATE and closes; Setup replaces the exe and opens the app again. Never silent, never during
// an install.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Mdu
{
    public class UpdateInfo
    {
        public string Version, Tag, Name, Notes, PageUrl, AssetName, AssetUrl, Sha256, Error;
        public double Size;
        public bool Available;
    }

    public static class Updater
    {
        // "owner/name" of the public GitHub repository whose Releases carry MillerDriverUpdater-<version>-Setup.exe.
        // Empty = no update source yet: nothing is checked.
        public const string Repo = "mwmiller99-dot/MillerDriverUpdater";

        // Subject the Setup's Authenticode signature must carry once releases are code-signed ("" = not signed yet:
        // the SHA-256 from GitHub is the check, and a signature that is present must still be valid).
        public const string ExpectedSigner = "";

        public static bool Configured { get { return Repo.Length > 0; } }
        public static string RepoUrl { get { return "https://github.com/" + Repo; } }

        const string AssetPattern = @"^MillerDriverUpdater-\d+(\.\d+){1,3}-Setup\.exe$";

        // Pure: "v2.2.0" / "2.2" / "Miller Driver Updater 2.2.1" -> Version, or null.
        public static Version ParseVersion(string text)
        {
            Match m = Rx.Match(text, @"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
            int c = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0, d = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
            return new Version(a, b, c, d);
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        // Pure: reads GitHub's "latest release" JSON for repo and decides whether it is an update for currentVersion.
        // Offered only when the release is final, newer, has a Setup asset served from this repo's release downloads,
        // and GitHub lists a sha256 digest for it.
        public static UpdateInfo ParseRelease(string json, string currentVersion, string repo)
        {
            UpdateInfo u = new UpdateInfo();
            Dictionary<string, object> d = null;
            try { d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; } catch { }
            if (d == null) { u.Error = "GitHub's answer could not be read."; return u; }
            u.Tag = Util.Str(Get(d, "tag_name"));
            u.Name = Util.Str(Get(d, "name"));
            u.Notes = Util.Str(Get(d, "body"));
            u.PageUrl = Util.Str(Get(d, "html_url"));
            if (true.Equals(Get(d, "draft")) || true.Equals(Get(d, "prerelease"))) return u;
            Version latest = ParseVersion(u.Tag), mine = ParseVersion(currentVersion);
            if (latest == null) { u.Error = "The latest release has no version number in its tag (" + u.Tag + ")."; return u; }
            u.Version = latest.Revision > 0 ? latest.ToString(4) : latest.ToString(3);
            if (mine != null && latest <= mine) return u;   // up to date

            object[] assets = Get(d, "assets") as object[];
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    string name = Util.Str(Get(a, "name"));
                    if (!Rx.IsMatch(name, AssetPattern)) continue;
                    u.AssetName = name;
                    u.AssetUrl = Util.Str(Get(a, "browser_download_url"));
                    try { u.Size = Convert.ToDouble(Get(a, "size") ?? 0); } catch { }
                    string digest = Util.Str(Get(a, "digest"));
                    Match dm = Rx.Match(digest, "^sha256:([0-9a-f]{64})$");
                    if (dm.Success) u.Sha256 = dm.Groups[1].Value.ToLowerInvariant();
                    break;
                }
            }
            if (u.AssetName == null) { u.Error = "Release " + u.Tag + " has no " + "MillerDriverUpdater-<version>-Setup.exe" + " attached."; return u; }
            string prefix = "https://github.com/" + repo + "/releases/download/";
            if (!u.AssetUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { u.Error = "Release " + u.Tag + " points its Setup somewhere other than " + prefix; return u; }
            if (u.Sha256 == null) { u.Error = "GitHub lists no SHA-256 checksum for " + u.AssetName + ", so it cannot be verified and is not offered."; return u; }
            u.Available = true;
            return u;
        }

        // Asks GitHub. Never throws: problems come back in Error and are logged.
        public static UpdateInfo Check()
        {
            UpdateInfo none = new UpdateInfo();
            if (!Configured) return none;
            try
            {
                Util.EnsureTls();
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                req.UserAgent = "MillerDriverUpdater/" + AppInfo.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 20000;
                string json;
                try
                {
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
                }
                catch (WebException wex)
                {
                    HttpWebResponse hr = wex.Response as HttpWebResponse;
                    if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) { Log.Diag("Update check: " + Repo + " has no releases yet."); return none; }
                    throw;
                }
                UpdateInfo u = ParseRelease(json, AppInfo.Version, Repo);
                Log.Diag("Update check: latest " + (u.Tag ?? "?") + ", this is " + AppInfo.Version + (u.Available ? " -> update offered" : "") + (u.Error != null ? " (" + u.Error + ")" : ""));
                return u;
            }
            catch (Exception ex)
            {
                none.Error = ex.Message;
                Log.Diag("Update check failed: " + ex.Message);
                return none;
            }
        }

        public static string Sha256Of(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // Downloads the release's Setup and verifies it. Returns its path; throws with a plain reason if anything is off
        // (the file is deleted then). progress(percent, text) is called from this thread.
        public static string Download(UpdateInfo u, Action<int, string> progress)
        {
            if (!u.Available) throw new InvalidOperationException("No update to download.");
            Util.EnsureTls();
            string dir = Path.Combine(AppInfo.DownloadDir, "update");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, u.AssetName);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(u.AssetUrl);
            req.UserAgent = "MillerDriverUpdater/" + AppInfo.Version;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            double done = 0, total = u.Size;
            using (WebResponse resp = req.GetResponse())
            using (Stream input = resp.GetResponseStream())
            using (FileStream output = File.Create(path))
            {
                if (resp.ContentLength > 0) total = resp.ContentLength;
                byte[] buf = new byte[65536];
                int n;
                DateTime last = DateTime.MinValue;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    done += n;
                    if (progress != null && (DateTime.UtcNow - last).TotalMilliseconds > 250)
                    {
                        last = DateTime.UtcNow;
                        int pct = total > 0 ? (int)Math.Min(100, 100.0 * done / total) : -1;
                        progress(pct, "Downloading the update: " + Util.FormatBytes(done) + (total > 0 ? " of " + Util.FormatBytes(total) : ""));
                    }
                }
            }
            if (progress != null) progress(100, "Checking the download...");
            string actual = Sha256Of(path);
            if (actual != u.Sha256)
            {
                try { File.Delete(path); } catch { }
                throw new InvalidOperationException("The downloaded update does not match the checksum GitHub lists for it, so it was deleted and not run. Try again later.");
            }
            string subject; int code;
            bool signed = Authenticode.Verify(path, out subject, out code);
            if (ExpectedSigner.Length > 0 && !(signed && Rx.IsMatch(subject ?? "", Regex.Escape(ExpectedSigner))))
            {
                try { File.Delete(path); } catch { }
                throw new InvalidOperationException("The downloaded update is not signed by " + ExpectedSigner + ", so it was deleted and not run.");
            }
            if (!signed && code != unchecked((int)0x800B0100))   // TRUST_E_NOSIGNATURE is fine until releases are signed
            {
                try { File.Delete(path); } catch { }
                throw new InvalidOperationException(string.Format("The downloaded update carries a broken signature (0x{0:X8}), so it was deleted and not run.", code));
            }
            Log.Diag("Update " + u.Version + " downloaded and verified: sha256 " + actual + (signed ? ", signed by " + subject : ", unsigned"));
            return path;
        }

        // Runs the verified Setup quietly in update mode; the caller closes the app right after. Setup waits for this exe
        // to be free, replaces it (keeping the old one until the new one is known to run) and opens the app again.
        public static void LaunchSetup(string setupPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo(setupPath, "/S /UPDATE");
            psi.UseShellExecute = false;
            Process.Start(psi);
            Log.Diag("Started " + Path.GetFileName(setupPath) + " /S /UPDATE; closing for the update.");
        }
    }
}
