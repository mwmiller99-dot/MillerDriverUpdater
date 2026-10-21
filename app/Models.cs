// Plain data the engine hands to the window and the report. Public fields so the JSON report can serialize them as-is.
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Mdu
{
    public class SystemInfo
    {
        public string Computer, Model, OS, OSVersion, Build, LastBoot;
        public bool Admin, PendingReboot;
    }

    public class Device
    {
        public string Name, Class, Manufacturer, Provider, DriverVersion, DriverDate, Inf, Status, Problem,
                      InstanceId, HardwareId, Bus, Vid, Pid, FirmwareRev, Vendor;
        public bool Signed, Present;
        public int ProblemCode;
    }

    public class VendorInfo
    {
        public string Name, App, WingetId, AppMatch, Url;
    }

    public class Peripheral
    {
        public string Name, Vendor, Vid, Pid, FirmwareRev, DriverVersion, DriverDate, Bus, VendorApp, AppVersion, AppMatch, WingetId, Url;
        public bool Present, Known, AppInstalled;
        public int Interfaces;
    }

    public class Gpu
    {
        public string Name, Vendor, DriverVersion, FriendlyVersion, DriverDate, VendorApp, AppVersion, AppMatch, WingetId, Url;
        public bool AppInstalled;
    }

    public class InstalledApp
    {
        public string Name, Version, Kind, Location;
    }

    public class WuDriver
    {
        public string Title, Manufacturer, Model, Provider, Class, VersionDate, HardwareId;
        public bool Optional, Hidden;
        public double SizeMB;
        [ScriptIgnore] public object Update;   // the WUA IUpdate COM object; lives in the MTA, never touched by the UI thread
    }

    public class WingetUpgrade
    {
        public string Name, Id, Installed, Available, Source, Kind, Note, AppMatch;
        public bool RequiresExplicitTarget;
    }

    public class WingetResult
    {
        public string Id, Output = "", TimeoutWhy = "";
        public int ExitCode = -1;
        public bool Success, TimedOut;
        public double DownloadBytes, Seconds;
    }

    public class NvidiaDriver
    {
        public string Gpu, Installed, Version, Url, Name = "GeForce Game Ready Driver", ReleaseDate, Psid, Pfid, Lookup, Error, DetailsUrl;
        public double SizeBytes;
        public bool Notebook, Available, Skipped;
    }

    public class NvidiaInstallResult
    {
        public string Version, Message = "", Mode;
        public bool Success, RebootRequired;
        public int ExitCode = -1;
        public double DownloadBytes, DownloadSeconds, InstallSeconds;
    }

    public class PreflightCheck
    {
        public string Id, Severity, Title, Detail, FixText, FixKind, FixArg;
    }

    public class DriverBackup
    {
        public string Device, InstanceId, Inf, Path, Message, When, Exported, HardwareId;
        public bool Success;
    }

    public class DriverRestoreResult
    {
        public string Inf, Output = "";
        public int ExitCode = -1;
        public bool Success, RebootRequired;
    }

    public class DriverPackage
    {
        public string PublishedName, OriginalName, Provider, Class, Version, Date, Signer;
    }

    public class RestorePointResult
    {
        public bool Created, Existing;
        public string Message;
    }

    // One line of this app's own history: what it installed, rolled back or hid.
    public class HistoryEntry
    {
        public string When, What, Version, Source, Result;
        public bool Ok;
    }

    public class ScanResult
    {
        public List<WuDriver> HiddenDrivers = new List<WuDriver>();          // hidden in Windows Update itself
        public List<WingetUpgrade> PinnedApps = new List<WingetUpgrade>();   // winget pins (Name, Id, Installed)
        public string SkippedNvidia;                                         // an NVIDIA version the person skipped
        public List<HistoryEntry> History = new List<HistoryEntry>();
        public string Error, WuError, WingetError, ReportHtml, ReportJson;
        public SystemInfo System;
        public int DeviceCount;
        public List<Device> Inventory = new List<Device>();
        public List<Device> Problems = new List<Device>();
        public List<Gpu> Gpus = new List<Gpu>();
        public List<Peripheral> Peripherals = new List<Peripheral>();
        public List<WuDriver> Drivers = new List<WuDriver>();
        public List<string> DeferredGpu = new List<string>();
        public List<WingetUpgrade> Apps = new List<WingetUpgrade>();
        public NvidiaDriver Nvidia;
        public List<PreflightCheck> Preflight = new List<PreflightCheck>();
        public List<DriverBackup> Backups = new List<DriverBackup>();
        public Dictionary<string, bool> WingetHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Timing = new Dictionary<string, double>();
        public DateTime Finished;
        public double Seconds;
    }
}
