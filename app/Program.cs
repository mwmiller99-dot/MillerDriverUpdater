// Entry point.
//   MillerDriverUpdater.exe                 the window (asks for administrator rights once, at launch)
//   MillerDriverUpdater.exe --check         quiet scan; a notification when something is found (the weekly task runs this)
//   MillerDriverUpdater.exe --schedule      register the weekly check      --unschedule  remove it
//   MillerDriverUpdater.exe --support-bundle  zip the logs onto the desktop
//   MillerDriverUpdater.exe --no-elevate    the window without the UAC prompt (scanning works; installing does not)
//   MillerDriverUpdater.exe --ping          start and exit 0 (Setup runs this so an antivirus decides during install)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Mdu
{
    static class Program
    {
        const string WindowMutex = @"Local\MillerDriverUpdater.Window";
        const string CheckMutex = @"Local\MillerDriverUpdater.Check";

        [STAThread]
        static int Main(string[] args)
        {
            HashSet<string> flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in args) flags.Add(a.Trim());
            if (flags.Contains("--ping")) return 0;
            AppInfo.MigrateOldData();   // before anything creates the new data folder
            Util.EnsureTls();
            System.Text.RegularExpressions.Regex.CacheSize = 64;
            Log.Rotate();
            try
            {
                if (flags.Contains("--check")) return RunCheck();
                if (flags.Contains("--schedule")) { Schedule.Register(AppInfo.ExePath); return 0; }
                if (flags.Contains("--unschedule")) { Schedule.Unregister(); return 0; }
                if (flags.Contains("--support-bundle"))
                {
                    string zip = Report.SupportBundle(null);
                    Process.Start("explorer.exe", "/select,\"" + zip + "\"");
                    return 0;
                }
                if (flags.Contains("--version")) { MessageBox.Show(AppInfo.Name + " " + AppInfo.Version, AppInfo.Name); return 0; }
            }
            catch (Exception ex) { Log.Error(string.Join(" ", args) + " failed: " + ex); return 1; }

            // Opened again while the window is up: bring that window forward instead (and no second UAC prompt).
            if (WindowOpen()) { NativeUi.ActivateExisting(AppInfo.Name); return 0; }

            // One UAC prompt at launch keeps scanning and installing in one process.
            if (!Util.IsAdmin() && !flags.Contains("--no-elevate"))
            {
                while (true)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(AppInfo.ExePath, "--elevated");
                        psi.UseShellExecute = true;
                        psi.Verb = "runas";
                        Process.Start(psi);
                        return 0;
                    }
                    catch (Win32Exception ex)
                    {
                        if (ex.NativeErrorCode != 1223)   // 1223 = the person clicked No
                        {
                            Log.Error("Elevation failed: " + ex);
                            MessageBox.Show("Miller Driver Updater could not ask Windows for administrator rights:\n\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                            return 1;
                        }
                        MessageBoxResult again = MessageBox.Show("Miller Driver Updater installs drivers, so Windows asks for your OK first.\n\nClick OK, then Yes on the Windows prompt. (Checking for updates never changes anything; nothing installs until you click Install.)",
                            AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Information);
                        if (again != MessageBoxResult.OK) return 0;
                    }
                }
            }

            bool created;
            using (Mutex m = new Mutex(true, WindowMutex, out created))
            {
                if (!created) { NativeUi.ActivateExisting(AppInfo.Name); return 0; }
                return RunWindow();
            }
        }

        static bool WindowOpen()
        {
            try
            {
                Mutex m;
                if (Mutex.TryOpenExisting(WindowMutex, MutexRights.Synchronize, out m)) { m.Dispose(); return true; }
                return false;
            }
            catch (UnauthorizedAccessException) { return true; }   // it exists, owned by the elevated window
            catch { return false; }
        }

        static int RunCheck()
        {
            bool created;
            using (Mutex m = new Mutex(true, CheckMutex, out created))
            {
                if (!created || WindowOpen()) return 0;   // already checking, or the window is open and the person is looking
                return CheckMode.Run();
            }
        }

        static int RunWindow()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error("Unhandled: " + e.Exception);
                MessageBox.Show("Something went wrong: " + Errors.Friendly(Errors.Text(e.Exception), true) + "\n\nThe details were saved to:\n" + Log.ErrorPath,
                    AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Fatal: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => { Log.Diag("Background task error: " + e.Exception); e.SetObserved(); };
            MainWindow w;
            try { w = new MainWindow(); }
            catch (Exception ex)
            {
                Log.Error("Window failed to start: " + ex);
                MessageBox.Show("Miller Driver Updater could not start.\n\n" + ex.Message + "\n\nThe details were saved to:\n" + Log.ErrorPath + "\n\nSend that file to whoever gave you the app.",
                    AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            return app.Run(w.Window);
        }
    }
}
