// What the person decided, and what this app did, kept between runs. Hidden drivers live in Windows Update itself and
// hidden apps are winget pins, so the only choice stored here is an NVIDIA version to skip.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Mdu
{
    public static class Skips
    {
        static readonly object Lock = new object();
        static string FilePath { get { return Path.Combine(AppInfo.ReportDir, "skipped.json"); } }

        static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return map;
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (d != null) foreach (KeyValuePair<string, object> kv in d) map[kv.Key] = Util.Str(kv.Value);
            }
            catch (Exception ex) { Log.Diag("Reading skipped.json failed: " + ex.Message); }
            return map;
        }

        public static string Get(string key)
        {
            lock (Lock)
            {
                string v;
                return Load().TryGetValue(key, out v) && v.Length > 0 ? v : null;
            }
        }

        // value null = forget the choice.
        public static void Set(string key, string value)
        {
            lock (Lock)
            {
                Dictionary<string, string> map = Load();
                if (value == null) map.Remove(key); else map[key] = value;
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(map), new UTF8Encoding(false));
            }
        }
    }

    // reports\history.json: what this app installed, rolled back or hid (the newest 500 entries).
    public static class History
    {
        static readonly object Lock = new object();
        const int Keep = 500;
        static string FilePath { get { return Path.Combine(AppInfo.ReportDir, "history.json"); } }

        public static List<HistoryEntry> Load()
        {
            List<HistoryEntry> list = new List<HistoryEntry>();
            lock (Lock)
            {
                try
                {
                    if (!File.Exists(FilePath)) return list;
                    object[] rows = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(FilePath)) as object[];
                    if (rows == null) return list;
                    foreach (object o in rows)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        HistoryEntry e = new HistoryEntry();
                        object v;
                        e.When = d.TryGetValue("When", out v) ? Util.Str(v) : "";
                        e.What = d.TryGetValue("What", out v) ? Util.Str(v) : "";
                        e.Version = d.TryGetValue("Version", out v) ? Util.Str(v) : "";
                        e.Source = d.TryGetValue("Source", out v) ? Util.Str(v) : "";
                        e.Result = d.TryGetValue("Result", out v) ? Util.Str(v) : "";
                        e.Ok = d.TryGetValue("Ok", out v) && true.Equals(v);
                        list.Add(e);
                    }
                }
                catch (Exception ex) { Log.Diag("Reading history.json failed: " + ex.Message); }
            }
            return list;
        }

        public static void Add(string what, string version, string source, bool ok, string result)
        {
            try
            {
                List<HistoryEntry> list = Load();
                HistoryEntry e = new HistoryEntry();
                e.When = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                e.What = what ?? ""; e.Version = version ?? ""; e.Source = source ?? ""; e.Ok = ok; e.Result = result ?? "";
                list.Add(e);
                if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
                lock (Lock) File.WriteAllText(FilePath, Report.Json(list), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Diag("Writing history.json failed: " + ex.Message); }
        }
    }
}
