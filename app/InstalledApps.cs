// Installed programs (Win32 uninstall keys + Store/MSIX packages), cached per run, and launching an app by Start-menu name.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace Mdu
{
    public static class InstalledApps
    {
        static List<InstalledApp> _cache;
        static readonly object Lock = new object();

        public static List<InstalledApp> All(bool refresh = false)
        {
            lock (Lock)
            {
                if (_cache != null && !refresh) return _cache;
                List<InstalledApp> list = new List<InstalledApp>();
                ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, list);
                ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, list);   // WOW6432Node
                ReadUninstall(RegistryHive.CurrentUser, RegistryView.Default, list);
                foreach (KeyValuePair<string, string[]> p in Packages())
                {
                    InstalledApp a = new InstalledApp();
                    a.Name = p.Value[0]; a.Version = p.Value[1]; a.Kind = "Store"; a.Location = p.Value[2];
                    list.Add(a);
                }
                _cache = list;
                return list;
            }
        }

        static void ReadUninstall(RegistryHive hive, RegistryView view, List<InstalledApp> list)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey un = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (un == null) return;
                    foreach (string n in un.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = un.OpenSubKey(n))
                            {
                                if (k == null) continue;
                                string name = k.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(name)) continue;
                                InstalledApp a = new InstalledApp();
                                a.Name = name;
                                a.Version = k.GetValue("DisplayVersion") == null ? null : Convert.ToString(k.GetValue("DisplayVersion"));
                                a.Kind = "Win32";
                                a.Location = k.GetValue("InstallLocation") as string;
                                list.Add(a);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // This user's MSIX/Store packages, read from the AppModel repository (what Get-AppxPackage lists):
        // key = package full name; value = { name, version, install folder }.
        public static Dictionary<string, string[]> Packages()
        {
            Dictionary<string, string[]> found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (k == null) return found;
                    foreach (string full in k.GetSubKeyNames())
                    {
                        string[] parts = full.Split('_');
                        if (parts.Length < 2) continue;
                        string root = null;
                        try { using (RegistryKey p = k.OpenSubKey(full)) { if (p != null) root = p.GetValue("PackageRootFolder") as string; } } catch { }
                        found[full] = new[] { parts[0], parts[1], root };
                    }
                }
            }
            catch { }
            return found;
        }

        // Install folder of the newest version of a package (e.g. Microsoft.DesktopAppInstaller, where winget.exe lives).
        public static string PackageFolder(string packageName)
        {
            string best = null;
            Version bestVer = null;
            foreach (string[] p in Packages().Values)
            {
                if (!string.Equals(p[0], packageName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(p[2])) continue;
                Version v;
                if (!Version.TryParse(p[1], out v)) v = new Version(0, 0);
                if (bestVer == null || v > bestVer) { bestVer = v; best = p[2]; }
            }
            return best;
        }

        public static List<InstalledApp> Find(string pattern)
        {
            List<InstalledApp> hits = new List<InstalledApp>();
            if (string.IsNullOrWhiteSpace(pattern) || pattern == "^$") return hits;
            foreach (InstalledApp a in All()) if (Rx.IsMatch(a.Name, pattern)) hits.Add(a);
            return hits;
        }

        public static InstalledApp FindFirst(string pattern)
        {
            List<InstalledApp> hits = Find(pattern);
            return hits.Count > 0 ? hits[0] : null;
        }

        // Launches an installed app by its Start-menu name (Win32 and Store apps alike); falls back to a URL.
        // Uses the Shell's AppsFolder, which is what Get-StartApps reads. Call from an STA thread (the UI thread).
        public static string Start(string pattern, string fallbackUrl)
        {
            string appName;
            string appId = FindStartApp(pattern, out appName);
            try
            {
                if (appId != null)
                {
                    Process.Start("explorer.exe", "shell:AppsFolder\\" + appId);
                    return "Launched " + appName;
                }
                if (!string.IsNullOrEmpty(fallbackUrl))
                {
                    Util.OpenShell(fallbackUrl);
                    return "Opened " + fallbackUrl;
                }
            }
            catch (Exception ex) { return "Could not launch (" + ex.Message + ")"; }
            return "Not found";
        }

        // The Start-menu entry whose name matches: its AppUserModelID / path (what explorer shell:AppsFolder\ opens), or null.
        public static string FindStartApp(string pattern, out string appName)
        {
            string appId = null;
            appName = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(t);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                if (folder != null)
                {
                    dynamic items = folder.Items();
                    int count = items.Count;
                    for (int i = 0; i < count; i++)
                    {
                        dynamic item = items.Item(i);
                        string name = item.Name;
                        if (Rx.IsMatch(name, pattern)) { appId = item.Path; appName = name; break; }
                    }
                }
            }
            catch (Exception ex) { Log.Diag("Start menu lookup failed: " + ex.Message); }
            return appId;
        }
    }
}
