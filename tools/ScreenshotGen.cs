// Draws the README screenshots (docs\screenshot-light.png, docs\screenshot-dark.png): the real window from
// dist\MillerDriverUpdater.exe, filled with made-up sample data instead of a scan, so no real PC, device or app shows.
// Run tools\screenshot.cmd after build.cmd. The window opens off-screen for a moment and closes itself.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mdu;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

static class ScreenshotGen
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static WingetUpgrade App(string name, string id, string from, string to, string kind = "normal", string note = null)
    {
        WingetUpgrade a = new WingetUpgrade();
        a.Name = name; a.Id = id; a.Installed = from; a.Available = to; a.Source = "winget"; a.Kind = kind; a.Note = note;
        return a;
    }

    static WuDriver Driver(string title, string maker, string cls, string date, double mb, bool optional)
    {
        WuDriver d = new WuDriver();
        d.Title = title; d.Manufacturer = maker; d.Class = cls; d.VersionDate = date; d.SizeMB = mb; d.Optional = optional;
        return d;
    }

    // A believable PC that is nobody's: one NVIDIA card, a few Windows Update drivers, common apps, one mouse.
    static ScanResult Sample()
    {
        ScanResult r = new ScanResult();
        r.System = new SystemInfo();
        r.System.Computer = "HOME-PC";
        r.Finished = DateTime.Today.AddHours(10).AddMinutes(42);
        r.Seconds = 9.4;
        r.DeviceCount = 214;
        r.ReportHtml = "sample.html";
        for (int i = 0; i < r.DeviceCount; i++)
        {
            Device d = new Device();
            d.Name = "Device " + i; d.Class = "System"; d.Present = true;
            r.Inventory.Add(d);
        }
        NvidiaDriver nv = new NvidiaDriver();
        nv.Gpu = "NVIDIA GeForce RTX 4070"; nv.Installed = "612.33"; nv.Version = "621.01"; nv.SizeBytes = 1.12e9;
        nv.Available = true; nv.Lookup = "exact"; nv.DetailsUrl = "https://www.nvidia.com/";
        r.Nvidia = nv;
        r.Drivers.Add(Driver("Realtek Semiconductor Corp. - MEDIA - 6.0.9785.1", "Realtek", "MEDIA", "2026-08-14", 52.3, false));
        r.Drivers.Add(Driver("Intel - Net - 23.110.0.5", "Intel", "Net", "2026-09-02", 18.7, false));
        r.Drivers.Add(Driver("Intel Corporation - Bluetooth - 23.110.0.3", "Intel", "Bluetooth", "2026-09-02", 12.4, true));
        r.Apps.Add(App("Mozilla Firefox", "Mozilla.Firefox", "144.0.2", "145.0.1"));
        r.Apps.Add(App("7-Zip", "7zip.7zip", "26.00", "26.01"));
        r.Apps.Add(App("VLC media player", "VideoLAN.VLC", "3.0.21", "3.0.23"));
        r.Apps.Add(App("Notepad++", "Notepad++.Notepad++", "8.8.6", "8.8.8"));
        r.Apps.Add(App("Steam", "Valve.Steam", "", "2.10.91.91", "self", "updates itself when opened"));
        Gpu g = new Gpu();
        g.Name = "NVIDIA GeForce RTX 4070"; g.Vendor = "NVIDIA"; g.FriendlyVersion = "612.33";
        r.Gpus.Add(g);
        Peripheral m = new Peripheral();
        m.Name = "Logitech G502 X"; m.Vendor = "Logitech"; m.FirmwareRev = "0102"; m.VendorApp = "Logitech G HUB";
        m.AppMatch = "G HUB"; m.AppInstalled = true; m.Known = true; m.Present = true;
        r.Peripherals.Add(m);
        return r;
    }

    // Windows' default look: light or dark, standard blue accent (not whatever this PC is set to).
    static void ApplyTheme(object mw, Type t, Window win, bool dark)
    {
        object theme = t.GetField("_theme", Any).GetValue(mw);
        Action<string, object> set = (name, v) => theme.GetType().GetField(name).SetValue(theme, v);
        set("Dark", dark);
        set("Accent", "#0078D4");
        if (dark)
        {
            set("WindowBg", "#1C1C1E"); set("Card", "#2C2C2E"); set("Border", "#3A3A3C"); set("Text", "#F5F5F7"); set("Text2", "#98989D"); set("Hover", "#3A3A3C");
            set("Ok", "#30D158"); set("Warn", "#FFD60A"); set("Bad", "#FF453A"); set("Muted", "#98989D");
        }
        else
        {
            set("WindowBg", "#F5F5F7"); set("Card", "#FFFFFF"); set("Border", "#E5E5EA"); set("Text", "#1D1D1F"); set("Text2", "#6E6E73"); set("Hover", "#F2F2F7");
            set("Ok", "#248A3D"); set("Warn", "#C25E00"); set("Bad", "#D70015"); set("Muted", "#6E6E73");
        }
        Dictionary<string, Brush> brushes = (Dictionary<string, Brush>)t.GetField("_brush", Any).GetValue(mw);
        foreach (string key in new[] { "WindowBg", "Card", "Border", "Text", "Text2", "Accent", "Hover" })
            brushes[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString((string)theme.GetType().GetField(key).GetValue(theme)));
        brushes["AccentTint"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#220078D4"));
        foreach (KeyValuePair<string, Brush> kv in brushes) win.Resources[kv.Key] = kv.Value;
        foreach (Inline il in ((TextBlock)win.FindName("VersionText")).Inlines) il.Foreground = brushes["Text2"];
    }

    // The window's client area at 2x, with rounded corners and a hairline edge.
    static void Save(Window win, string path)
    {
        FrameworkElement root = (FrameworkElement)VisualTreeHelper.GetChild(win, 0);
        double w = root.ActualWidth, h = root.ActualHeight, rad = 10;
        DrawingVisual v = new DrawingVisual();
        using (DrawingContext dc = v.RenderOpen())
        {
            Rect all = new Rect(0, 0, w, h);
            dc.PushClip(new RectangleGeometry(all, rad, rad));
            dc.DrawRectangle(win.Background, null, all);
            dc.DrawRectangle(new VisualBrush(root), null, all);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), 1), new Rect(0.5, 0.5, w - 1, h - 1), rad, rad);
        }
        RenderTargetBitmap rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        rtb.Render(v);
        PngBitmapEncoder e = new PngBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(rtb));
        using (FileStream f = File.Create(path)) e.Save(f);
        Console.WriteLine("  " + path);
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool dark = args.Length > 0 && args[0] == "dark";
        string path = args.Length > 1 ? args[1] : (dark ? @"docs\screenshot-dark.png" : @"docs\screenshot-light.png");
        Application app = new Application();
        Type t = typeof(ScanResult).Assembly.GetType("Mdu.MainWindow");
        object mw = Activator.CreateInstance(t, true);
        Window win = (Window)t.GetField("Window").GetValue(mw);
        t.GetField("_scanning", Any).SetValue(mw, true);   // the window's own scan sees this and does not start
        ApplyTheme(mw, t, win, dark);
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        win.Left = -5000; win.Top = 0; win.Width = 980; win.Height = 900;
        win.ShowInTaskbar = false;
        win.Loaded += (s, e) => win.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            t.GetField("_scanning", Any).SetValue(mw, false);
            t.GetMethod("ShowScan", Any).Invoke(mw, new object[] { Sample() });
            ((Button)win.FindName("RescanButton")).Visibility = Visibility.Visible;
            ((TextBlock)win.FindName("Status")).Text = "Select what you want, then click Install. Drivers get a restore point first.";
            win.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { Save(win, path); win.Close(); }));
        }));
        return app.Run(win);
    }
}
