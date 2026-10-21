// Device inventory: every device, its driver, USB/Bluetooth ids, GPUs, peripheral vendor apps, problem devices.
using System;
using System.Collections.Generic;
using System.Management;

namespace Mdu
{
    public static class Inventory
    {
        static VendorInfo V(string name, string app, string wingetId, string appMatch, string url)
        {
            VendorInfo v = new VendorInfo();
            v.Name = name; v.App = app; v.WingetId = wingetId; v.AppMatch = appMatch; v.Url = url;
            return v;
        }

        // USB vendor id -> peripheral ecosystem. WingetId values are candidates only (verified with winget show before
        // they are recommended). AppMatch is a regex tested against installed program names; '^$' = no desktop app.
        public static readonly Dictionary<string, VendorInfo> VendorMap = new Dictionary<string, VendorInfo>(StringComparer.OrdinalIgnoreCase)
        {
            { "046D", V("Logitech", "Logitech G HUB / Logi Options+", "Logitech.GHUB", "Logitech G HUB|Logi Options", "https://www.logitechg.com/innovation/g-hub.html") },
            { "1532", V("Razer", "Razer Synapse", "RazerInc.RazerInstaller", "Razer Synapse", "https://www.razer.com/synapse") },
            { "1B1C", V("Corsair", "Corsair iCUE", "Corsair.iCUE", "iCUE", "https://www.corsair.com/icue") },
            { "0FD9", V("Elgato", "Elgato software (Stream Deck / Camera Hub)", null, "Stream Deck|Camera Hub|Elgato", "https://www.elgato.com/downloads") },
            { "1038", V("SteelSeries", "SteelSeries GG", "SteelSeries.GG", "SteelSeries GG", "https://steelseries.com/gg") },
            { "0951", V("HyperX", "HyperX NGENUITY", null, "NGENUITY", "https://hyperx.com/pages/ngenuity") },
            { "03F0", V("HP (incl. HyperX)", "HyperX NGENUITY for HyperX gear; HP Support Assistant for HP hardware", null, "NGENUITY|HP Support Assistant", "https://hyperx.com/pages/ngenuity") },
            { "3434", V("Keychron", "Keychron Launcher (web, VIA/QMK)", null, "^$", "https://launcher.keychron.com") },
            { "31E3", V("Wooting", "Wootility", null, "Wootility", "https://wooting.io/wootility") },
            { "3297", V("ZSA", "Keymapp", null, "Keymapp", "https://www.zsa.io/flash") },
            { "045E", V("Microsoft", "Windows Update (Xbox Accessories app for controllers)", null, "^$", "https://apps.microsoft.com/search?query=Xbox+Accessories") },
            { "0B05", V("ASUS", "Armoury Crate", null, "Armoury Crate", "https://rog.asus.com/armoury-crate/") },
            { "1462", V("MSI", "MSI Center", null, "MSI Center", "https://www.msi.com/") },
            { "1E7D", V("ROCCAT", "ROCCAT Swarm", null, "Swarm", "https://www.turtlebeach.com/") },
            { "10F5", V("Turtle Beach", "Turtle Beach Swarm II", null, "Swarm", "https://www.turtlebeach.com/") },
            { "2516", V("Cooler Master", "MasterPlus+", null, "MasterPlus", "https://www.coolermaster.com/") },
            { "1E71", V("NZXT", "NZXT CAM", "NZXT.CAM", "NZXT CAM", "https://nzxt.com/software/cam") },
            { "1395", V("Sennheiser / EPOS", "EPOS Connect", null, "EPOS Connect", "https://www.eposaudio.com/") },
            { "0B0E", V("Jabra", "Jabra Direct", "Jabra.Direct", "Jabra Direct", "https://www.jabra.com/") },
            { "047F", V("Poly / Plantronics", "Poly Lens", null, "Poly Lens", "https://www.poly.com/") },
            { "054C", V("Sony", "Phone app (headphones) / DualSense PC updater", null, "^$", "https://www.sony.com/") },
            { "28DE", V("Valve", "Steam (controller firmware lives in Steam)", "Valve.Steam", "^Steam$", "https://store.steampowered.com/about/") },
            { "2DC8", V("8BitDo", "8BitDo firmware updater", null, "8BitDo", "https://support.8bitdo.com/") },
            { "044F", V("Thrustmaster", "Thrustmaster drivers / firmware", null, "Thrustmaster", "https://support.thrustmaster.com/") },
            { "0EB7", V("Fanatec", "Fanatec Control Panel", null, "Fanatec", "https://fanatec.com/") },
        };

        // GPU vendor -> the app that owns the newest driver for it.
        public static readonly Dictionary<string, VendorInfo> GpuMap = new Dictionary<string, VendorInfo>(StringComparer.OrdinalIgnoreCase)
        {
            { "NVIDIA", V("NVIDIA", "NVIDIA App", "Nvidia.App", "^NVIDIA App|GeForce Experience", "https://www.nvidia.com/en-us/software/nvidia-app/") },
            { "AMD", V("AMD", "AMD Software: Adrenalin Edition", null, "AMD Software|Adrenalin", "https://www.amd.com/en/support") },
            { "Intel", V("Intel", "Intel Driver & Support Assistant", "Intel.IntelDriverAndSupportAssistant", "Driver & Support Assistant|Driver and Support Assistant", "https://www.intel.com/content/www/us/en/support/detect.html") },
        };

        // PCI / HD Audio vendor ids (VEN_xxxx). Only used to describe a device that has no driver and therefore no name.
        public static readonly Dictionary<string, string> PciVendorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "10DE", "NVIDIA" }, { "1002", "AMD" }, { "1022", "AMD" }, { "8086", "Intel" }, { "8087", "Intel" }, { "10EC", "Realtek" }, { "14E4", "Broadcom" },
            { "168C", "Qualcomm Atheros" }, { "17CB", "Qualcomm" }, { "1969", "Qualcomm Atheros" }, { "1B21", "ASMedia" }, { "1106", "VIA" }, { "104C", "Texas Instruments" },
            { "1033", "Renesas" }, { "1912", "Renesas" }, { "1B73", "Fresco Logic" }, { "144D", "Samsung" }, { "1987", "Phison" }, { "126F", "Silicon Motion" },
            { "1C5C", "SK hynix" }, { "1E0F", "Kioxia" }, { "1344", "Micron" }, { "15B3", "NVIDIA Mellanox" }, { "1D6A", "Marvell Aquantia" }, { "11AB", "Marvell" },
            { "1180", "Ricoh" }, { "1217", "O2 Micro" }, { "10B5", "Broadcom" }, { "1B4B", "Marvell" }, { "1AE0", "Google" }, { "15AD", "VMware" }, { "1414", "Microsoft" },
        };

        // Device Manager's problem codes, in words.
        static string ProblemText(int code)
        {
            switch (code)
            {
                case 1: return "Code 1: not configured correctly";
                case 3: return "Code 3: the driver may be damaged, or Windows is low on memory";
                case 10: return "Code 10: this device cannot start";
                case 12: return "Code 12: not enough free resources";
                case 14: return "Code 14: needs a restart to work properly";
                case 18: return "Code 18: its drivers need to be reinstalled";
                case 19: return "Code 19: its configuration in the registry is damaged";
                case 21: return "Code 21: Windows is removing this device";
                case 22: return "Code 22: disabled";
                case 24: return "Code 24: not present, not working properly, or missing drivers";
                case 28: return "Code 28: no driver installed";
                case 29: return "Code 29: turned off in the firmware (BIOS/UEFI)";
                case 31: return "Code 31: not working properly; Windows cannot load its driver";
                case 32: return "Code 32: its driver service is disabled";
                case 37: return "Code 37: Windows cannot initialize its driver";
                case 39: return "Code 39: Windows cannot load its driver (damaged or missing)";
                case 41: return "Code 41: the driver loaded but cannot find the hardware";
                case 43: return "Code 43: Windows stopped it because it reported problems";
                case 45: return "Code 45: not connected";
                case 48: return "Code 48: its driver is blocked because of known problems";
                case 52: return "Code 52: Windows cannot verify its driver's digital signature";
                default: return code > 0 ? "Problem code " + code : null;
            }
        }

        class PnpState { public string Name, Class, Status, HardwareId; public bool Present = true; public int Code; }

        static Dictionary<string, PnpState> ReadPnpEntities()
        {
            Dictionary<string, PnpState> map = new Dictionary<string, PnpState>(StringComparer.OrdinalIgnoreCase);
            string[] queries = {
                "SELECT DeviceID, Name, PNPClass, Status, Present, ConfigManagerErrorCode, HardwareID FROM Win32_PnPEntity",
                "SELECT DeviceID, Name, PNPClass, Status, ConfigManagerErrorCode, HardwareID FROM Win32_PnPEntity"   // no Present before Windows 8
            };
            foreach (string q in queries)
            {
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", q))
                    using (ManagementObjectCollection rows = s.Get())
                    {
                        foreach (ManagementBaseObject o in rows)
                        {
                            using (o)
                            {
                                string id = Util.Str(o["DeviceID"]);
                                if (id.Length == 0) continue;
                                PnpState p = new PnpState();
                                p.Name = Util.Str(o["Name"]);
                                p.Class = Util.Str(o["PNPClass"]);
                                p.Status = Util.Str(o["Status"]);
                                try { if (q.Contains("Present") && o["Present"] != null) p.Present = (bool)o["Present"]; } catch { }
                                try { p.Code = Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0); } catch { }
                                string[] hw = o["HardwareID"] as string[];
                                if (hw != null && hw.Length > 0) p.HardwareId = hw[0];
                                map[id] = p;
                            }
                        }
                    }
                    return map;
                }
                catch (Exception ex) { Log.Diag("Win32_PnPEntity query failed (" + ex.Message + ")"); map.Clear(); }
            }
            return map;
        }

        static bool IsBareWord(string s) { return string.IsNullOrEmpty(s) || System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Z0-9]{3,}$"); }

        // Every device Windows knows about, with its installed driver and parsed USB ids.
        public static List<Device> GetDevices()
        {
            Dictionary<string, PnpState> pnp = ReadPnpEntities();
            List<Device> list = new List<Device>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2",
                "SELECT DeviceName, Manufacturer, DriverProviderName, DriverVersion, DriverDate, DeviceClass, IsSigned, InfName, HardWareID, DeviceID FROM Win32_PnPSignedDriver"))
            using (ManagementObjectCollection rows = s.Get())
            {
                foreach (ManagementBaseObject d in rows)
                {
                    using (d)
                    {
                        string devName = Util.Str(d["DeviceName"]);
                        if (devName.Length == 0) continue;
                        string instance = Util.Str(d["DeviceID"]);
                        PnpState p = null;
                        if (instance.Length > 0) pnp.TryGetValue(instance, out p);
                        Device dev = Build(devName, Util.Str(d["DeviceClass"]), Util.Str(d["HardWareID"]), instance, p);
                        dev.Manufacturer = Util.Str(d["Manufacturer"]);
                        dev.Provider = Util.Str(d["DriverProviderName"]);
                        dev.DriverVersion = Util.Str(d["DriverVersion"]);
                        dev.DriverDate = Util.CimDate(d["DriverDate"]);
                        try { dev.Signed = d["IsSigned"] != null && (bool)d["IsSigned"]; } catch { }
                        dev.Inf = Util.Str(d["InfName"]);
                        list.Add(dev);
                        seen.Add(instance);
                    }
                }
            }
            // A device with a problem that has no signed-driver record at all would otherwise never be listed.
            foreach (KeyValuePair<string, PnpState> kv in pnp)
            {
                if (seen.Contains(kv.Key) || kv.Value.Code == 0 || !kv.Value.Present) continue;
                list.Add(Build(kv.Value.Name, kv.Value.Class, kv.Value.HardwareId ?? "", kv.Key, kv.Value));
            }
            return list;
        }

        static Device Build(string name, string cls, string hw, string instance, PnpState p)
        {
            Device dev = new Device();
            dev.InstanceId = instance;
            dev.HardwareId = hw;
            dev.Bus = hw.Split('\\')[0];
            dev.Present = true;
            string vid = Rx.Group(hw, "VID_([0-9A-F]{4})", 1) ?? Rx.Group(hw, "VID&[0-9A-F]{4}([0-9A-F]{4})", 1);   // second form: Bluetooth VID&0001046d
            string pid = Rx.Group(hw, "PID_([0-9A-F]{4})", 1) ?? Rx.Group(hw, "PID&([0-9A-F]{4})", 1);
            string rev = Rx.Group(hw, "REV_([0-9A-F]{4})", 1);
            dev.Vid = vid == null ? null : vid.ToUpperInvariant();
            dev.Pid = pid == null ? null : pid.ToUpperInvariant();
            dev.FirmwareRev = rev == null ? null : rev.ToUpperInvariant();
            VendorInfo vi;
            if (dev.Vid != null && VendorMap.TryGetValue(dev.Vid, out vi)) dev.Vendor = vi.Name;

            string friendly = null, pnpClass = null;
            if (p != null)
            {
                dev.Status = p.Status;
                dev.Present = p.Present;
                dev.ProblemCode = p.Code;
                dev.Problem = ProblemText(p.Code);
                friendly = p.Name;
                pnpClass = p.Class;
            }
            // A device with no driver (code 28) comes back with its class word as the "name" ("MEDIA") and no class.
            if (string.IsNullOrEmpty(cls) && !string.IsNullOrEmpty(pnpClass)) cls = pnpClass;
            if (IsBareWord(name) && !IsBareWord(friendly)) name = friendly;
            if (IsBareWord(name)) name = HardwareDescription(hw, cls);
            dev.Name = name;
            dev.Class = cls;
            return dev;
        }

        // Present devices Windows reports with an error (the yellow-bang list). Devices someone disabled on purpose are left out.
        public static List<Device> ProblemDevices(List<Device> inventory)
        {
            List<Device> hits = new List<Device>();
            foreach (Device d in inventory)
            {
                if (!d.Present || d.ProblemCode == 22) continue;
                if (string.Equals(d.Status, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(d.Status, "Degraded", StringComparison.OrdinalIgnoreCase)) hits.Add(d);
            }
            return hits;
        }

        // One row per physical USB/Bluetooth device (grouped by VID:PID), with vendor-app status.
        public static List<Peripheral> Peripherals(List<Device> inventory)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<Device>> groups = new Dictionary<string, List<Device>>(StringComparer.OrdinalIgnoreCase);
            foreach (Device d in inventory)
            {
                if (string.IsNullOrEmpty(d.Vid) || string.IsNullOrEmpty(d.Pid)) continue;
                string key = d.Vid + ":" + d.Pid;
                List<Device> g;
                if (!groups.TryGetValue(key, out g)) { g = new List<Device>(); groups[key] = g; order.Add(key); }
                g.Add(d);
            }
            List<Peripheral> result = new List<Peripheral>();
            foreach (string key in order)
            {
                List<Device> items = groups[key];
                // Prefer the top-level USB node (no interface suffix) for the display name.
                Device top = null;
                foreach (Device d in items) if (string.Equals(d.Bus, "USB", StringComparison.OrdinalIgnoreCase) && !Rx.IsMatch(d.HardwareId, "&MI_")) { top = d; break; }
                if (top == null) foreach (Device d in items) if (top == null || (d.Name ?? "").Length > (top.Name ?? "").Length) top = d;

                VendorInfo info;
                VendorMap.TryGetValue(top.Vid, out info);
                InstalledApp installed = info != null ? InstalledApps.FindFirst(info.AppMatch) : null;
                string rev = null;
                bool present = false;
                foreach (Device d in items) { if (rev == null && !string.IsNullOrEmpty(d.FirmwareRev)) rev = d.FirmwareRev; if (d.Present) present = true; }

                Peripheral p = new Peripheral();
                p.Name = top.Name;
                if (IsGenericName(top.Name))
                {
                    // Windows calls most gear "USB Composite Device"; the device's own product string says what it is.
                    string own = DeviceProps.BusReportedName(top.InstanceId);
                    foreach (Device d in items) { if (own != null && !IsGenericName(own)) break; own = DeviceProps.BusReportedName(d.InstanceId); }
                    if (own != null && !IsGenericName(own)) p.Name = own;
                }
                p.Vendor = info != null ? info.Name : top.Manufacturer; p.Vid = top.Vid; p.Pid = top.Pid;
                p.FirmwareRev = rev; p.DriverVersion = top.DriverVersion; p.DriverDate = top.DriverDate; p.Bus = top.Bus;
                p.Present = present; p.Interfaces = items.Count; p.Known = info != null;
                if (info != null) { p.VendorApp = info.App; p.AppMatch = info.AppMatch; p.WingetId = info.WingetId; p.Url = info.Url; }
                p.AppInstalled = installed != null;
                p.AppVersion = installed != null ? installed.Version : null;
                result.Add(p);
            }
            return result;
        }

        static bool IsGenericName(string name)
        {
            return string.IsNullOrWhiteSpace(name) || Rx.IsMatch(name.Trim(),
                @"^(USB Composite Device|USB Input Device|USB Device|HID-compliant .*|HID Keyboard Device|HID Mouse|USB Audio Device|Generic .*|USB Hub|Unknown .*|.*\bUSB device)$");
        }

        // Each graphics adapter, its installed driver, and the vendor app that owns the newest driver.
        public static List<Gpu> Gpus()
        {
            List<Gpu> list = new List<Gpu>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, AdapterCompatibility, DriverVersion, DriverDate FROM Win32_VideoController"))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementBaseObject g in rows)
                    {
                        using (g)
                        {
                            Gpu gpu = new Gpu();
                            gpu.Name = Util.Str(g["Name"]);
                            gpu.DriverVersion = Util.Str(g["DriverVersion"]);
                            gpu.DriverDate = Util.CimDate(g["DriverDate"]);
                            string probe = Util.Str(g["AdapterCompatibility"]) + " " + gpu.Name;
                            gpu.Vendor = "Other";
                            if (Rx.IsMatch(probe, "NVIDIA")) gpu.Vendor = "NVIDIA";
                            else if (Rx.IsMatch(probe, "Advanced Micro|AMD|Radeon")) gpu.Vendor = "AMD";
                            else if (Rx.IsMatch(probe, "Intel")) gpu.Vendor = "Intel";
                            gpu.FriendlyVersion = gpu.DriverVersion;
                            if (gpu.Vendor == "NVIDIA") gpu.FriendlyVersion = NvidiaFriendlyVersion(gpu.DriverVersion);
                            VendorInfo info;
                            if (GpuMap.TryGetValue(gpu.Vendor, out info))
                            {
                                InstalledApp app = InstalledApps.FindFirst(info.AppMatch);
                                gpu.VendorApp = info.App; gpu.AppMatch = info.AppMatch; gpu.WingetId = info.WingetId; gpu.Url = info.Url;
                                gpu.AppInstalled = app != null;
                                gpu.AppVersion = app != null ? app.Version : null;
                            }
                            list.Add(gpu);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Diag("Win32_VideoController query failed: " + ex.Message); }
            return list;
        }

        // NVIDIA's marketing version is the last five digits of the WMI version (32.0.15.6094 -> 560.94).
        public static string NvidiaFriendlyVersion(string wmiVersion)
        {
            string digits = System.Text.RegularExpressions.Regex.Replace(wmiVersion ?? "", @"\D", "");
            if (digits.Length < 5) return wmiVersion;
            return digits.Substring(digits.Length - 5).Insert(3, ".");
        }

        // A readable name for a device that has none (no driver installed), built from its hardware ID.
        public static string HardwareDescription(string hardwareId, string cls)
        {
            string hw = hardwareId ?? "";
            string bus = hw.Split('\\')[0].ToUpperInvariant();
            string vendor = null;
            string ven = Rx.Group(hw, "VEN_([0-9A-F]{4})", 1);
            string vid = Rx.Group(hw, "VID_([0-9A-F]{4})", 1);
            string name;
            VendorInfo vi;
            if (ven != null) { if (PciVendorMap.TryGetValue(ven, out name)) vendor = name; }
            else if (vid != null && VendorMap.TryGetValue(vid, out vi)) vendor = vi.Name;
            string kind;
            switch (bus)
            {
                case "HDAUDIO":
                    kind = (vendor == "NVIDIA" || vendor == "AMD" || vendor == "Intel") ? "High Definition Audio (the HDMI / DisplayPort audio on the graphics card)" : "High Definition Audio device";
                    break;
                case "PCI": kind = "PCI device"; break;
                case "USB": kind = "USB device"; break;
                case "HID": kind = "input device"; break;
                case "BTHENUM": kind = "Bluetooth device"; break;
                case "ACPI": kind = "system (ACPI) device"; break;
                default: kind = string.IsNullOrEmpty(cls) ? "device" : cls + " device"; break;
            }
            return vendor != null ? vendor + " " + kind : "Unknown " + kind;
        }

        static bool StartsWithI(string s, string prefix) { return s != null && prefix != null && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); }

        // For a device Windows flags with an error: which update already on the list will fix it, if any (a sentence, or null).
        public static string ProblemHint(Device device, List<WuDriver> wuDrivers, NvidiaDriver nvidia)
        {
            string hw = device.HardwareId ?? "";
            string bus = hw.Split('\\')[0].ToUpperInvariant();
            string ven = Rx.Group(hw, "VEN_([0-9A-F]{4})", 1);
            if (ven != null) ven = ven.ToUpperInvariant();
            // 1) A Windows Update driver on the list that targets this very hardware.
            if (wuDrivers != null)
            {
                foreach (WuDriver d in wuDrivers)
                {
                    string did = d.HardwareId;
                    if (!string.IsNullOrEmpty(did) && hw.Length > 0 && (StartsWithI(hw, did) || StartsWithI(did, hw)))
                        return "The '" + d.Title + "' driver update on the list below is for this device.";
                }
            }
            // 2) Audio devices that live inside a graphics driver package.
            if (bus == "HDAUDIO")
            {
                switch (ven)
                {
                    case "10DE":
                        if (nvidia != null && nvidia.Available) return "This is the NVIDIA HDMI / DisplayPort audio device; its driver is part of the NVIDIA driver update below. Install that and this should clear.";
                        return "This is the NVIDIA HDMI / DisplayPort audio device; its driver comes with the NVIDIA GeForce driver package. Reinstalling the NVIDIA driver (Check Again when nvidia.com has a newer one) restores it.";
                    case "1002": return "This is the AMD HDMI / DisplayPort audio device; its driver comes with the AMD graphics driver (AMD Software: Adrenalin Edition).";
                    case "8086": return "This is the Intel display audio device; its driver comes with the Intel graphics driver (Intel Driver & Support Assistant).";
                    case "10EC": return "This is the Realtek onboard audio; the driver comes from the PC or motherboard maker's support page, or Windows Update.";
                }
            }
            return null;
        }

        // Microsoft Update Catalog search for a hardware ID - the official way to find a driver Windows Update doesn't offer.
        // The catalog indexes vendor/device pairs, so subsystem, revision, interface and collection suffixes are cut off.
        public static string CatalogUrl(string hardwareId)
        {
            string[] parts = (hardwareId ?? "").Split('\\');
            string bus = parts[0];
            string tail = parts[parts.Length - 1];
            tail = Rx.Replace(tail, "&MI_[0-9A-F]{2}.*$", "");
            tail = Rx.Replace(tail, "&Col[0-9A-F]{2}.*$", "");
            tail = Rx.Replace(tail, "&SUBSYS_[0-9A-F]+", "");
            tail = Rx.Replace(tail, "&REV_[0-9A-F]+", "");
            tail = Rx.Replace(tail, "&CC_[0-9A-F]+", "");
            string q = tail;
            if (parts.Length > 1 && Rx.IsMatch(bus, "^(PCI|HDAUDIO|USB|ACPI|HID|BTHENUM|SD|PCMCIA)$")) q = bus + "\\" + tail;
            return "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(q);
        }

        // The present inventory devices a Windows Update driver applies to, by hardware ID (exact or prefix).
        public static List<Device> DevicesForDriver(string driverHardwareId, List<Device> inventory)
        {
            List<Device> hits = new List<Device>();
            string hw = (driverHardwareId ?? "").Trim();
            if (hw.Length == 0) return hits;
            foreach (Device d in inventory)
            {
                if (!d.Present || string.IsNullOrEmpty(d.HardwareId)) continue;
                if (string.Equals(d.HardwareId.Trim(), hw, StringComparison.OrdinalIgnoreCase) || StartsWithI(d.HardwareId, hw)) hits.Add(d);
            }
            return hits;
        }

        public static SystemInfo GetSystemInfo()
        {
            SystemInfo s = new SystemInfo();
            s.Computer = Environment.MachineName;
            try
            {
                using (ManagementObjectSearcher q = new ManagementObjectSearcher(@"root\cimv2", "SELECT Caption, Version, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection rows = q.Get())
                    foreach (ManagementBaseObject o in rows) using (o) { s.OS = Util.Str(o["Caption"]); s.OSVersion = Util.Str(o["Version"]); s.Build = Util.Str(o["BuildNumber"]); s.LastBoot = Util.CimDate(o["LastBootUpTime"]); }
                using (ManagementObjectSearcher q = new ManagementObjectSearcher(@"root\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                using (ManagementObjectCollection rows = q.Get())
                    foreach (ManagementBaseObject o in rows) using (o) { s.Model = (Util.Str(o["Manufacturer"]) + " " + Util.Str(o["Model"])).Trim(); }
            }
            catch (Exception ex) { Log.Diag("System info query failed: " + ex.Message); }
            s.Admin = Util.IsAdmin();
            s.PendingReboot = Util.PendingReboot();
            return s;
        }
    }
}
