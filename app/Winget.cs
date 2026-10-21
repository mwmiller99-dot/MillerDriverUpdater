// winget: finding it, listing upgrades (fixed-width table parser), and upgrading one package with live progress
// through a hidden pseudo-console, plus a watchdog so no single app can become "the waiting point".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Mdu
{
    public class WingetKind
    {
        public string Kind, Note, AppMatch;
    }

    public class ByteProgress
    {
        public double Done, Total;
    }

    public static class Winget
    {
        static readonly object CallLock = new object();   // one winget at a time: they share a source index
        static readonly Dictionary<string, bool> IdCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static string FindExe()
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(';'))
            {
                try
                {
                    if (dir.Trim().Length == 0) continue;
                    string p = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), "winget.exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            // Elevated sessions often lose the per-user App Execution Alias from PATH; look it up directly.
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                string alias = Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(alias)) return alias;
            }
            string pkg = InstalledApps.PackageFolder("Microsoft.DesktopAppInstaller");
            if (pkg != null)
            {
                string p = Path.Combine(pkg, "winget.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static string RequireExe()
        {
            string exe = FindExe();
            if (exe == null) throw new InvalidOperationException("winget was not found on this PC. Install \"App Installer\" from the Microsoft Store, then re-run.");
            return exe;
        }

        public static ProcResult Invoke(IEnumerable<string> args, int timeoutMs = 600000)
        {
            string exe = RequireExe();
            lock (CallLock) { return Util.Run(exe, Util.JoinArgs(args), timeoutMs, new UTF8Encoding(false)); }
        }

        public static string VersionText()
        {
            try { return Invoke(new[] { "--version" }, 30000).Output.Trim(); } catch { return null; }
        }

        // True when winget knows a package with exactly this id. Cached.
        public static bool IdExists(string id)
        {
            lock (IdCache) { if (IdCache.ContainsKey(id)) return IdCache[id]; }
            bool ok = false;
            try { ok = Invoke(new[] { "show", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity" }, 120000).ExitCode == 0; } catch { }
            lock (IdCache) IdCache[id] = ok;
            return ok;
        }

        // Apps that update themselves (launchers, browsers, chat clients, Office). winget "upgrading" them is pointless at best
        // and an installer waiting for a click at worst (game launchers like Battle.net do this). Shown with an Open button, never run.
        // { id regex, display-name regex, why }
        static readonly string[][] SelfUpdatingApps = {
            new[] { @"^Blizzard\.BattleNet", @"Battle\.net", "updates itself when opened; its winget installer waits for a person at the keyboard" },
            new[] { @"^Valve\.Steam$", "^Steam$", "updates itself when opened" },
            new[] { @"^EpicGames\.EpicGamesLauncher", "Epic Games Launcher", "updates itself when opened" },
            new[] { @"^ElectronicArts\.EADesktop", "^EA app$|^EA Desktop", "updates itself when opened" },
            new[] { @"^Ubisoft\.Connect", "Ubisoft Connect", "updates itself when opened" },
            new[] { @"^GOG\.Galaxy", "GOG GALAXY", "updates itself when opened" },
            new[] { @"^RiotGames\.", "Riot Client|League of Legends|VALORANT", "updates itself when opened" },
            new[] { @"^Discord\.Discord", "^Discord", "updates itself every time it starts" },
            new[] { @"^Google\.Chrome", "^Google Chrome", "updates itself in the background" },
            new[] { @"^Microsoft\.Edge", "^Microsoft Edge", "updates itself through Windows" },
            new[] { @"^Mozilla\.Firefox", "^Mozilla Firefox|^Firefox", "updates itself in the background" },
            new[] { @"^Brave\.Brave", "^Brave", "updates itself in the background" },
            new[] { @"^Opera\.", "^Opera", "updates itself in the background" },
            new[] { @"^Vivaldi\.", "^Vivaldi", "updates itself in the background" },
            new[] { @"^Spotify\.Spotify", "^Spotify", "updates itself when opened" },
            new[] { @"^Microsoft\.Teams", "^Microsoft Teams", "updates itself when opened" },
            new[] { @"^Microsoft\.OneDrive", "^Microsoft OneDrive|^OneDrive", "updates itself through Windows" },
            new[] { @"^Microsoft\.Office|^Microsoft\.365", "^Microsoft 365|^Microsoft Office", "updates itself (Click-to-Run); winget would download the whole suite again" },
            new[] { @"^Zoom\.Zoom", "^Zoom", "updates itself when opened" },
            new[] { @"^SlackTechnologies\.Slack", "^Slack", "updates itself when opened" },
            new[] { @"^Dropbox\.Dropbox", "^Dropbox", "updates itself in the background" },
            new[] { @"^Telegram\.TelegramDesktop", "^Telegram", "updates itself when opened" },
            new[] { @"^WhatsApp\.WhatsApp", "^WhatsApp", "updates itself through the Store" },
            new[] { @"^OpenWhisperSystems\.Signal", "^Signal", "updates itself when opened" },
            new[] { @"^Overwolf\.", "^Overwolf", "updates itself when opened" },
            new[] { @"^Nvidia\.(App|GeForceExperience)", "^NVIDIA App|GeForce Experience", "updates itself when opened" },
            new[] { @"^Adobe\.CreativeCloud", "^Adobe Creative Cloud", "updates itself when opened" },
            new[] { @"^Amazon\.Games", "^Amazon Games", "updates itself when opened" },
            new[] { @"^RockstarGames\.", "^Rockstar Games Launcher", "updates itself when opened" },
            new[] { @"^Overwolf\.CurseForge|^CurseForge", "^CurseForge", "updates itself when opened" },
            new[] { @"^Microsoft\.PowerToys", "^PowerToys", "updates itself (it checks on start)" },
        };

        // self    - the app updates itself; Open button, never run
        // unknown - winget cannot read the installed version; unticked, not in Check all, run last if ticked
        // normal  - a real version step
        public static WingetKind ResolveKind(string id, string name, string installed)
        {
            WingetKind k = new WingetKind();
            foreach (string[] e in SelfUpdatingApps)
            {
                if ((!string.IsNullOrEmpty(id) && Rx.IsMatch(id, e[0])) || (!string.IsNullOrEmpty(name) && Rx.IsMatch(name, e[1])))
                {
                    k.Kind = "self"; k.Note = e[2]; k.AppMatch = e[1];
                    return k;
                }
            }
            string inst = (installed ?? "").Trim();
            if (inst.Length == 0 || Rx.IsMatch(inst, "^(Unknown|<|>)"))
            {
                k.Kind = "unknown";
                k.Note = "winget cannot read the installed version, so it cannot tell whether this is outdated; ticking it reinstalls the newest version";
                return k;
            }
            k.Kind = "normal";
            return k;
        }

        static string Take(string s, int start, int end)
        {
            if (start < 0 || start >= s.Length) return "";
            if (end < 0 || end > s.Length) end = s.Length;
            if (end <= start) return "";
            return s.Substring(start, end - start).Trim();
        }

        // Parses the fixed-width table `winget upgrade` prints. Pure (no winget call), unit tested.
        // Handles the second "require explicit targeting" table and progress-spinner junk.
        public static List<WingetUpgrade> ParseTable(IEnumerable<string> lines)
        {
            List<WingetUpgrade> results = new List<WingetUpgrade>();
            int[] cols = null;   // Name, Id, Version, Available, Source
            bool inRows = false, explicitTarget = false;
            foreach (string raw in lines)
            {
                if (raw == null) continue;
                // A terminal shows only the text after the last carriage return on a line.
                string line = "";
                string[] segs = raw.Split('\r');
                for (int i = segs.Length - 1; i >= 0; i--) if (segs[i].Length > 0) { line = segs[i]; break; }
                line = Rx.Replace(line, @"^[\s\-\\|/]*(?=Name\s)", "");

                if (Rx.IsMatch(line, "require explicit targeting")) { explicitTarget = true; inRows = false; cols = null; continue; }
                if (Rx.IsMatch(line, @"^\s*Name\s+Id\s+Version"))
                {
                    int nameAt = line.IndexOf("Name", StringComparison.Ordinal);
                    cols = new[] {
                        nameAt,
                        line.IndexOf("Id", nameAt + 4, StringComparison.Ordinal),
                        line.IndexOf("Version", StringComparison.Ordinal),
                        line.IndexOf("Available", StringComparison.Ordinal),
                        line.IndexOf("Source", StringComparison.Ordinal)
                    };
                    inRows = false;
                    continue;
                }
                if (cols != null && !inRows)
                {
                    if (Rx.IsMatch(line, @"^\s*-{5,}\s*$")) inRows = true;
                    continue;
                }
                if (!inRows) continue;
                if (string.IsNullOrWhiteSpace(line) || Rx.IsMatch(line, @"^\d+ (upgrade|package)") || Rx.IsMatch(line, @"^\s*-{5,}")) { inRows = false; continue; }

                WingetUpgrade u = new WingetUpgrade();
                u.Name = Take(line, cols[0], cols[1]);
                u.Id = Take(line, cols[1], cols[2]);
                u.Installed = Take(line, cols[2], cols[3]);
                u.Available = Take(line, cols[3], cols[4]);
                u.Source = Take(line, cols[4], -1);
                u.RequiresExplicitTarget = explicitTarget;
                WingetKind k = ResolveKind(u.Id, u.Name, u.Installed);
                u.Kind = k.Kind; u.Note = k.Note; u.AppMatch = k.AppMatch;
                if (u.Id.Length > 0) results.Add(u);
            }
            return results;
        }

        // Hiding an app update = a winget pin: `winget upgrade` leaves pinned apps out (verified with winget 1.29), and the
        // choice lives in winget itself. Unpin brings it back.
        public static void Pin(string id)
        {
            ProcResult r = Invoke(new[] { "pin", "add", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity" }, 120000);
            if (r.ExitCode != 0 && !Rx.IsMatch(r.Output, "already exists")) throw new InvalidOperationException("winget could not pin " + id + ": " + Util.Last(Lines(r.Output)));
            Log.Diag("Pinned " + id + " in winget.");
        }

        public static void Unpin(string id)
        {
            ProcResult r = Invoke(new[] { "pin", "remove", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity" }, 120000);
            if (r.ExitCode != 0 && !Rx.IsMatch(r.Output, "no pin|not found|does not exist")) throw new InvalidOperationException("winget could not unpin " + id + ": " + Util.Last(Lines(r.Output)));
            Log.Diag("Unpinned " + id + " in winget.");
        }

        public static List<WingetUpgrade> Pins()
        {
            ProcResult r = Invoke(new[] { "pin", "list", "--accept-source-agreements", "--disable-interactivity" }, 60000);
            return ParsePinTable(r.Lines);
        }

        static List<string> Lines(string text)
        {
            List<string> keep = new List<string>();
            foreach (string l in (text ?? "").Replace("\r\n", "\n").Split('\n')) if (l.Trim().Length > 0) keep.Add(l.Trim());
            return keep;
        }

        // Pure: `winget pin list` table (Name, Id, Version, Source, Pin type) -> Name / Id / Installed. Unit tested.
        public static List<WingetUpgrade> ParsePinTable(IEnumerable<string> lines)
        {
            List<WingetUpgrade> pins = new List<WingetUpgrade>();
            int nameAt = -1, idAt = -1, verAt = -1, srcAt = -1;
            bool inRows = false;
            foreach (string raw in lines)
            {
                if (raw == null) continue;
                string line = raw.TrimEnd('\r');
                if (Rx.IsMatch(line, @"^\s*Name\s+Id\s+Version"))
                {
                    nameAt = line.IndexOf("Name", StringComparison.Ordinal);
                    idAt = line.IndexOf("Id", nameAt + 4, StringComparison.Ordinal);
                    verAt = line.IndexOf("Version", StringComparison.Ordinal);
                    srcAt = line.IndexOf("Source", StringComparison.Ordinal);
                    inRows = false;
                    continue;
                }
                if (nameAt < 0) continue;
                if (!inRows) { if (Rx.IsMatch(line, @"^\s*-{5,}\s*$")) inRows = true; continue; }
                if (string.IsNullOrWhiteSpace(line)) { inRows = false; continue; }
                WingetUpgrade p = new WingetUpgrade();
                p.Name = Take(line, nameAt, idAt);
                p.Id = Take(line, idAt, verAt);
                p.Installed = Take(line, verAt, srcAt);
                p.Kind = "pinned";
                if (p.Id.Length > 0) pins.Add(p);
            }
            return pins;
        }

        // Software with an upgrade available.
        public static List<WingetUpgrade> Upgrades()
        {
            ProcResult r = Invoke(new[] { "upgrade", "--include-unknown", "--accept-source-agreements", "--disable-interactivity" });
            if (Rx.IsMatch(r.Output, "No installed package found|No applicable upgrade")) return new List<WingetUpgrade>();
            List<WingetUpgrade> rows = ParseTable(r.Lines);
            // A narrow table cuts long ids with an ellipsis; ask again through a 400-column pseudo-console so they are whole.
            bool cut = false;
            foreach (WingetUpgrade u in rows) if (u.Id.EndsWith("…") || u.Name.EndsWith("…")) cut = true;
            if (cut)
            {
                try
                {
                    string wide = RunPty(new[] { "upgrade", "--include-unknown", "--accept-source-agreements" }, 400, 120000);
                    List<WingetUpgrade> again = ParseTable(StripVt(wide).Replace("\r\n", "\n").Split('\n'));
                    if (again.Count >= rows.Count) rows = again;
                }
                catch (Exception ex) { Log.Diag("Wide winget listing failed: " + ex.Message); }
            }
            return rows;
        }

        static string RunPty(IEnumerable<string> args, short cols, int timeoutMs)
        {
            string cmd = Util.QuoteArg(RequireExe()) + " " + Util.JoinArgs(args);
            StringBuilder all = new StringBuilder();
            lock (CallLock)
            {
                using (PtyProcess p = PtyProcess.Start(cmd, cols, 50))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    while (!p.HasExited && sw.ElapsedMilliseconds < timeoutMs) { all.Append(p.ReadNew()); Thread.Sleep(100); }
                    if (!p.HasExited) p.Kill();
                    Thread.Sleep(150);
                    all.Append(p.ReadNew());
                }
            }
            return all.ToString();
        }

        // Pure parser: "12.3 MB / 45.6 MB" out of a winget progress line, in bytes; null when absent.
        public static ByteProgress ParseProgressLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            Match m = Rx.Match(line, @"(\d+(?:[.,]\d+)?)\s*(B|KB|MB|GB)\s*/\s*(\d+(?:[.,]\d+)?)\s*(B|KB|MB|GB)");
            if (!m.Success) return null;
            ByteProgress p = new ByteProgress();
            p.Done = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * Mult(m.Groups[2].Value);
            p.Total = double.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * Mult(m.Groups[4].Value);
            return p;
        }

        static double Mult(string unit)
        {
            switch (unit.ToUpperInvariant())
            {
                case "KB": return 1024.0;
                case "MB": return 1024.0 * 1024;
                case "GB": return 1024.0 * 1024 * 1024;
                default: return 1.0;
            }
        }

        // Pure: strips ANSI/VT escape sequences and stray control characters from console output.
        public static string StripVt(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = Regex.Replace(text, @"\x1b\[[0-9;?]*[ -/]*[@-~]", "");       // CSI (colors, cursor moves, erase)
            t = Regex.Replace(t, @"\x1b\][^\x07\x1b]*(\x07|\x1b\\)", "");            // OSC (window title)
            t = Regex.Replace(t, @"\x1b[=>()][0-9A-Za-z]?", "");                       // misc 2-char sequences
            t = Regex.Replace(t, @"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "");             // control chars except \t \r \n
            return t;
        }

        // Size of the file winget is downloading right now (newest file touched in the last 15 s under its temp folders), or 0.
        public static double DownloadBytes()
        {
            double best = 0;
            try
            {
                List<string> dirs = new List<string>();
                string temp = Path.GetTempPath();
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string win = Environment.GetEnvironmentVariable("windir");
                dirs.Add(Path.Combine(temp, "WinGet"));
                if (!string.IsNullOrEmpty(local)) { dirs.Add(Path.Combine(local, "WinGet")); dirs.Add(Path.Combine(local, @"Temp\WinGet")); }
                if (!string.IsNullOrEmpty(win)) dirs.Add(Path.Combine(win, @"Temp\WinGet"));
                DateTime cut = DateTime.Now.AddSeconds(-15);
                HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string dir in dirs)
                {
                    if (!done.Add(dir) || !Directory.Exists(dir)) continue;
                    foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            if (fi.LastWriteTime > cut && fi.Length > best) best = fi.Length;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return best;
        }

        class UpgradeState
        {
            public string Phase = "Download", Carry = "", TimeoutWhy = "";
            public double LastBytes;
            public int Matches, Seen;
            public DateTime? LastMatchAt, WindowSince, InstallSince;
            public DateTime LastOutputAt = DateTime.UtcNow, LastCheckAt = DateTime.UtcNow;
            public bool TimedOut;
        }

        // Upgrades one package. With onProgress, winget runs attached to a hidden pseudo-console so it prints its live
        // "x MB / y MB" counter. If the pseudo-console is unavailable the output goes through a pipe and winget's download
        // folder is watched instead. Watchdog: activity = output or a growing download. No activity for idleTimeoutSec ->
        // stopped; an installer with a visible window (it ignored --silent and wants a click) gets windowTimeoutSec;
        // maxSec caps the whole thing. The process tree is killed and the result carries ExitCode -3.
        public static WingetResult Upgrade(string id, string name, bool silent, Action<ProgressInfo> onProgress,
                                           int idleTimeoutSec = 360, int windowTimeoutSec = 240, int maxSec = 1500)
        {
            List<string> wargs = new List<string> { "upgrade", "--id", id, "--exact", "--include-unknown", "--accept-package-agreements", "--accept-source-agreements" };
            if (silent) wargs.Add("--silent");
            if (string.IsNullOrEmpty(name)) name = id;
            string exe = RequireExe();

            if (onProgress == null)
            {
                List<string> plain = new List<string>(wargs);
                plain.Add("--disable-interactivity");
                ProcResult pr = Invoke(plain, maxSec * 1000);
                WingetResult quick = new WingetResult();
                quick.Id = id; quick.ExitCode = pr.ExitCode; quick.Success = pr.ExitCode == 0; quick.Output = pr.Output;
                return quick;
            }

            onProgress(ProgressInfo.Create("Download", source: "winget", current: name));
            Stopwatch sw = Stopwatch.StartNew();
            SpeedMeter meter = new SpeedMeter();
            UpgradeState st = new UpgradeState();
            StringBuilder all = new StringBuilder();

            Action<ByteProgress> emitBytes = pg =>
            {
                st.Matches++;
                st.LastMatchAt = DateTime.UtcNow;
                st.LastBytes = pg.Done;
                int pct = -1;
                if (pg.Total > 0) pct = (int)Math.Min(100, Math.Round(100.0 * pg.Done / pg.Total));
                onProgress(ProgressInfo.Create("Download", percent: pct, bytesDone: pg.Done, bytesTotal: pg.Total, bytesPerSec: meter.Measure(pg.Done), current: name, source: "winget"));
            };
            // One chunk of console text: split into segments, turn progress counters into callbacks.
            Action<string> handle = chunk =>
            {
                st.LastOutputAt = DateTime.UtcNow;
                string clean = StripVt(chunk);
                all.Append(clean);
                string[] segments = Regex.Split(st.Carry + clean, "[\r\n]");
                st.Carry = segments[segments.Length - 1];
                if (st.Carry.Length > 2000) st.Carry = st.Carry.Substring(st.Carry.Length - 2000);
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    string seg = segments[i];
                    if (string.IsNullOrWhiteSpace(seg)) continue;
                    if (st.Seen < 25) { st.Seen++; Log.Diag("winget> " + seg.Trim()); }
                    ByteProgress pg = ParseProgressLine(seg);
                    if (pg != null) emitBytes(pg);
                    else if (st.Phase == "Download" && Rx.IsMatch(seg, "Installing|Starting package install|installer hash|Successfully verified"))
                    {
                        st.Phase = "Install";
                        st.InstallSince = DateTime.UtcNow;
                        onProgress(ProgressInfo.Create("Install", current: name, source: "winget"));
                    }
                }
                // The live partial segment too: progress lines are rewritten in place without a newline.
                ByteProgress live = ParseProgressLine(st.Carry);
                if (live != null && live.Done != st.LastBytes) emitBytes(live);
            };
            // No counter printed: watch the download folder grow.
            Action watch = () =>
            {
                if (st.Phase != "Download") return;
                if (st.LastMatchAt != null && (DateTime.UtcNow - st.LastMatchAt.Value).TotalSeconds < 3) return;
                double b = DownloadBytes();
                if (b > 0 && b != st.LastBytes)
                {
                    st.LastBytes = b;
                    st.LastOutputAt = DateTime.UtcNow;   // a growing download counts as activity
                    onProgress(ProgressInfo.Create("Download", bytesDone: b, bytesPerSec: meter.Measure(b), current: name, source: "winget"));
                }
            };
            // Every 5 s: is this still alive? True = stop it.
            Func<int, bool> watchdog = rootPid =>
            {
                DateTime now = DateTime.UtcNow;
                if ((now - st.LastCheckAt).TotalSeconds < 5) return false;
                st.LastCheckAt = now;
                double idle = (now - st.LastOutputAt).TotalSeconds;
                if (rootPid > 0 && idle > 15)
                {
                    List<int> kids = ProcessTree.Descendants(rootPid);
                    if (kids.Count > 0 && ProcessTree.AnyHasWindow(kids))
                    {
                        if (st.WindowSince == null) { st.WindowSince = now; Log.Diag("winget's installer for " + id + " has a visible window"); }
                    }
                    else st.WindowSince = null;
                }
                if (st.Phase == "Install" && st.InstallSince != null)
                {
                    // Keep the row alive while the installer works silently: elapsed time plus the limit it is held to.
                    string limit = st.WindowSince != null
                        ? "its window has been waiting " + Util.FormatDuration((now - st.WindowSince.Value).TotalSeconds) + "; stops after " + Util.FormatDuration(windowTimeoutSec) + " of that"
                        : "stops automatically after " + Util.FormatDuration(idleTimeoutSec) + " without progress";
                    onProgress(ProgressInfo.Create("Install", current: name, source: "winget",
                        itemText: "Installing... " + Util.FormatDuration((now - st.InstallSince.Value).TotalSeconds) + " (" + limit + ")"));
                }
                int lim = idleTimeoutSec;
                if (st.WindowSince != null) lim = Math.Min(idleTimeoutSec, windowTimeoutSec);
                if (idle > lim)
                {
                    st.TimedOut = true;
                    st.TimeoutWhy = st.WindowSince != null ? "its installer opened a window and waited for a click" : "no progress for " + Util.FormatDuration(idle);
                    return true;
                }
                if (sw.Elapsed.TotalSeconds > maxSec) { st.TimedOut = true; st.TimeoutWhy = "still running after " + Util.FormatDuration(maxSec); return true; }
                return false;
            };

            int code = -1;
            string mode = "pipe";
            lock (CallLock)
            {
                PtyProcess pty = null;
                try
                {
                    string cmd = Util.QuoteArg(exe) + " " + Util.JoinArgs(wargs);
                    pty = PtyProcess.Start(cmd, 120, 40);
                    mode = "pty";
                    Log.Diag("winget via pseudo-console: " + cmd);
                }
                catch (Exception ex) { Log.Diag("Pseudo-console start failed: " + ex.Message); pty = null; }
                if (pty != null)
                {
                    try
                    {
                        while (!pty.HasExited)
                        {
                            string chunk = pty.ReadNew();
                            if (chunk.Length > 0) handle(chunk);
                            watch();
                            if (watchdog(pty.ProcessId))
                            {
                                Log.Diag("Stopping winget for " + id + ": " + st.TimeoutWhy);
                                ProcessTree.Kill(pty.ProcessId);
                                try { pty.Kill(); } catch { }
                                break;
                            }
                            Thread.Sleep(200);
                        }
                        code = pty.ExitCode;
                        Thread.Sleep(150);
                        string rest = pty.ReadNew();
                        if (rest.Length > 0) handle(rest);
                    }
                    finally { try { pty.Dispose(); } catch { } }
                }
                if (mode == "pipe")
                {
                    List<string> pargs = new List<string>(wargs);
                    pargs.Add("--disable-interactivity");
                    ProcessStartInfo psi = new ProcessStartInfo(exe, Util.JoinArgs(pargs));
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.CreateNoWindow = true;
                    psi.StandardOutputEncoding = new UTF8Encoding(false);
                    Log.Diag("winget via pipe: " + psi.Arguments);
                    using (Process proc = Process.Start(psi))
                    {
                        PipeReader reader = new PipeReader(proc.StandardOutput);
                        while (!(proc.HasExited && reader.Eof))
                        {
                            string chunk = reader.ReadNew();
                            if (chunk.Length > 0) handle(chunk);
                            watch();
                            if (watchdog(proc.Id))
                            {
                                Log.Diag("Stopping winget for " + id + ": " + st.TimeoutWhy);
                                ProcessTree.Kill(proc.Id);
                                break;
                            }
                            Thread.Sleep(200);
                        }
                        try { proc.WaitForExit(10000); } catch { }
                        reader.Join(2000);
                        string rest = reader.ReadNew();
                        if (rest.Length > 0) handle(rest);
                        try { if (proc.HasExited) code = proc.ExitCode; } catch { }
                    }
                }
            }
            if (st.TimedOut)
            {
                code = -3;
                all.Append("\n[mdu] stopped: " + st.TimeoutWhy + "\n");
            }
            Log.Diag(string.Format(CultureInfo.InvariantCulture, "winget exit {0} ({1}); progress counters seen: {2}; last bytes: {3}; timed out: {4}", code, mode, st.Matches, st.LastBytes, st.TimedOut));
            WingetResult res = new WingetResult();
            res.Id = id; res.ExitCode = code; res.Success = code == 0; res.Output = all.ToString();
            res.DownloadBytes = st.LastBytes; res.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            res.TimedOut = st.TimedOut; res.TimeoutWhy = st.TimeoutWhy;
            return res;
        }
    }
}
