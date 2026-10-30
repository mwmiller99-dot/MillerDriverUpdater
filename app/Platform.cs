// The parts of a PC whose updates come from neither Windows Update nor winget: the BIOS and board-level drivers (from
// the PC or motherboard maker), the chipset driver (AMD's or Intel's own package) and SSD firmware (each drive maker's
// tool). Nothing here installs anything; it finds the official place and how old the current version is.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace Mdu
{
    public class BoardInfo
    {
        public string Maker, Product, BiosVersion, BiosDate, CpuVendor;   // CpuVendor: AMD / Intel / other
        public bool Laptop, AppInstalled;
        public int BiosAgeMonths = -1;
        public string App, AppMatch, WingetId, SupportUrl, AppVersion;    // the maker's updater and download page
        public string ChipsetInstalled;                                   // AMD Chipset Software version, when installed
    }

    public class DiskInfo
    {
        public string Model, Firmware, Bus, Vendor, Tool, ToolMatch, WingetId, Url;
        public bool ToolInstalled;
        public int Count = 1;   // identical drives (same model and firmware) share one row
    }

    public static class Platform
    {
        static VendorInfo V(string name, string app, string wingetId, string appMatch, string url)
        {
            VendorInfo v = new VendorInfo();
            v.Name = name; v.App = app; v.WingetId = wingetId; v.AppMatch = appMatch; v.Url = url;
            return v;
        }

        // PC / motherboard maker (regex on the maker's name) -> its own updater and driver download page. WingetId values
        // are candidates, verified with winget before they are offered (like the vendor apps).
        static readonly KeyValuePair<string, VendorInfo>[] Makers =
        {
            new KeyValuePair<string, VendorInfo>("ASUS", V("ASUS", "Armoury Crate (desktops) / MyASUS (laptops)", null, "Armoury Crate|MyASUS", "https://www.asus.com/support/download-center/")),
            new KeyValuePair<string, VendorInfo>("Micro-Star|^MSI", V("MSI", "MSI Center", null, "MSI Center", "https://www.msi.com/support/download")),
            new KeyValuePair<string, VendorInfo>("Gigabyte", V("Gigabyte", "GIGABYTE Control Center", null, "GIGABYTE Control Center", "https://www.gigabyte.com/Support/Consumer/Download")),
            new KeyValuePair<string, VendorInfo>("ASRock", V("ASRock", null, null, "^$", "https://www.asrock.com/support/index.asp")),
            new KeyValuePair<string, VendorInfo>("^Dell", V("Dell", "Dell Command | Update", "Dell.CommandUpdate", "Dell Command \\| Update|Dell SupportAssist", "https://www.dell.com/support/home/")),
            new KeyValuePair<string, VendorInfo>("^HP$|Hewlett", V("HP", "HP Support Assistant", null, "HP Support Assistant", "https://support.hp.com/drivers")),
            new KeyValuePair<string, VendorInfo>("Lenovo", V("Lenovo", "Lenovo Vantage / System Update", "Lenovo.SystemUpdate", "Lenovo Vantage|Lenovo System Update|Commercial Vantage", "https://pcsupport.lenovo.com/")),
            new KeyValuePair<string, VendorInfo>("^Acer", V("Acer", "Acer Care Center", null, "Acer Care Center|Care Center S", "https://www.acer.com/support")),
            new KeyValuePair<string, VendorInfo>("Microsoft", V("Microsoft", "Windows Update (Surface firmware ships there)", null, "^$", "https://support.microsoft.com/surface")),
            new KeyValuePair<string, VendorInfo>("Razer", V("Razer", "Razer Synapse", "RazerInc.RazerInstaller", "Razer Synapse", "https://mysupport.razer.com/app/software")),
            new KeyValuePair<string, VendorInfo>("Framework", V("Framework", null, null, "^$", "https://knowledgebase.frame.work/")),
            new KeyValuePair<string, VendorInfo>("Samsung", V("Samsung", "Samsung Update", null, "Samsung Update", "https://www.samsung.com/support/")),
        };

        // Drive model (regex) -> the maker's SSD tool, the only official source of drive firmware.
        static readonly KeyValuePair<string, VendorInfo>[] DriveMakers =
        {
            new KeyValuePair<string, VendorInfo>("Samsung|^MZ", V("Samsung", "Samsung Magician", "Samsung.SamsungMagician", "Samsung Magician", "https://semiconductor.samsung.com/consumer-storage/support/tools/")),
            new KeyValuePair<string, VendorInfo>("^WDC|^WD[ _]|Western Digital|SanDisk", V("WD / SanDisk", "WD Dashboard / SanDisk Dashboard", null, "WD Dashboard|SanDisk Dashboard|Western Digital Dashboard", "https://support-en.wd.com/app/products/product-detailweb/p/126")),
            new KeyValuePair<string, VendorInfo>("Crucial|^CT\\d", V("Crucial", "Crucial Storage Executive", null, "Storage Executive", "https://www.crucial.com/support/storage-executive")),
            new KeyValuePair<string, VendorInfo>("Kingston|^SKC|^SA400|^SNV|^SFYR", V("Kingston", "Kingston SSD Manager", null, "Kingston SSD Manager", "https://www.kingston.com/en/support/technical/ssdmanager")),
            new KeyValuePair<string, VendorInfo>("Corsair", V("Corsair", "Corsair SSD Toolbox", null, "Corsair SSD Toolbox", "https://www.corsair.com/us/en/s/downloads")),
            new KeyValuePair<string, VendorInfo>("Seagate|^ST\\d|FireCuda|BarraCuda", V("Seagate", "SeaTools", null, "SeaTools", "https://www.seagate.com/support/downloads/seatools/")),
            new KeyValuePair<string, VendorInfo>("SK ?hynix|^HFS|^SHGP", V("SK hynix", "SK hynix Drive Manager", null, "SK ?hynix Drive Manager|Drive Manager Easy Kit", "https://ssd.skhynix.com/download/")),
            new KeyValuePair<string, VendorInfo>("Solidigm|^SSDPE|^INTEL SSD", V("Solidigm / Intel", "Solidigm Storage Tool", null, "Solidigm Storage Tool|Intel.*Memory and Storage Tool", "https://www.solidigm.com/support-page/drivers-downloads.html")),
            new KeyValuePair<string, VendorInfo>("ADATA|XPG", V("ADATA / XPG", "ADATA SSD ToolBox", null, "ADATA SSD ToolBox|XPG SSD ToolBox", "https://www.adata.com/us/ss/software-5/")),
            new KeyValuePair<string, VendorInfo>("Sabrent", V("Sabrent", "Sabrent Rocket Control Panel", null, "Rocket Control Panel", "https://sabrent.com/pages/software")),
            new KeyValuePair<string, VendorInfo>("Micron", V("Micron", "Micron Storage Executive", null, "Storage Executive", "https://www.micron.com/products/storage/ssd/storage-executive")),
            new KeyValuePair<string, VendorInfo>("TEAM|T-FORCE", V("Teamgroup", "T-FORCE / TEAMGROUP SSD tool", null, "^$", "https://www.teamgroupinc.com/en/support/download/")),
        };

        static VendorInfo Lookup(KeyValuePair<string, VendorInfo>[] map, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (KeyValuePair<string, VendorInfo> kv in map) if (Rx.IsMatch(text.Trim(), kv.Key)) return kv.Value;
            return null;
        }

        public static VendorInfo MakerFor(string maker) { return Lookup(Makers, maker); }
        public static VendorInfo DriveMakerFor(string model) { return Lookup(DriveMakers, model); }

        static bool Placeholder(string s)
        {
            return string.IsNullOrWhiteSpace(s) || Rx.IsMatch(s.Trim(), "^(System (Product Name|manufacturer)|To be filled by O\\.?E\\.?M\\.?|Default string|O\\.?E\\.?M|None|Not Applicable)$");
        }

        // Pure: whose BIOS and board drivers these are. A brand PC or laptop names itself in the system fields; a home-built
        // desktop leaves placeholders there ("System Product Name"), and the motherboard is what counts.
        public static void Identify(string systemMaker, string systemModel, string boardMaker, string boardProduct, out string maker, out string product)
        {
            bool brandPc = !Placeholder(systemMaker) && !Placeholder(systemModel) && !Rx.IsMatch(systemModel, "^System");
            maker = (brandPc ? systemMaker : boardMaker) ?? "";
            product = (brandPc ? systemModel : boardProduct) ?? "";
            maker = Rx.Replace(maker, "[,.]", " ");
            maker = Rx.Replace(maker, "\\b(COMPUTER|INC|CO|LTD|Corporation|International|Technology)\\b", " ");
            maker = Rx.Replace(maker, "\\s+", " ").Trim();
            if (Rx.IsMatch(maker, "^ASUSTeK")) maker = "ASUS";
            if (Rx.IsMatch(maker, "^Micro-Star")) maker = "MSI";
            if (Rx.IsMatch(maker, "Gigabyte")) maker = "Gigabyte";
        }

        // Pure: whole months between two dates (never negative).
        public static int MonthsBetween(DateTime from, DateTime to)
        {
            int m = (to.Year - from.Year) * 12 + to.Month - from.Month;
            if (to.Day < from.Day) m--;
            return Math.Max(0, m);
        }

        // Pure: "over 5 years old" / "14 months old" / "" (under a year: nothing worth saying).
        public static string AgeText(int months)
        {
            if (months < 12) return "";
            if (months < 24) return months + " months old";
            return "over " + (months / 12) + " years old";
        }

        public static BoardInfo GetBoard()
        {
            BoardInfo b = new BoardInfo();
            string sysMaker = "", sysModel = "", boardMaker = "", boardProduct = "", cpu = "";
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem"))
                using (ManagementObjectCollection rows = s.Get())
                    foreach (ManagementBaseObject o in rows) using (o)
                    {
                        sysMaker = Util.Str(o["Manufacturer"]); sysModel = Util.Str(o["Model"]);
                        try { b.Laptop = Convert.ToInt32(o["PCSystemType"]) == 2; } catch { }
                    }
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                using (ManagementObjectCollection rows = s.Get())
                    foreach (ManagementBaseObject o in rows) using (o) { boardMaker = Util.Str(o["Manufacturer"]); boardProduct = Util.Str(o["Product"]); }
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
                using (ManagementObjectCollection rows = s.Get())
                    foreach (ManagementBaseObject o in rows) using (o)
                    {
                        b.BiosVersion = Util.Str(o["SMBIOSBIOSVersion"]);
                        string raw = Util.Str(o["ReleaseDate"]);
                        if (raw.Length >= 8)
                        {
                            DateTime d;
                            if (DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                            {
                                b.BiosDate = d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                                b.BiosAgeMonths = MonthsBetween(d, DateTime.Today);
                            }
                        }
                    }
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT Manufacturer FROM Win32_Processor"))
                using (ManagementObjectCollection rows = s.Get())
                    foreach (ManagementBaseObject o in rows) using (o) { cpu = Util.Str(o["Manufacturer"]); break; }
            }
            catch (Exception ex) { Log.Diag("Board/BIOS query failed: " + ex.Message); }

            string maker, product;
            Identify(sysMaker, sysModel, boardMaker, boardProduct, out maker, out product);
            b.Maker = maker; b.Product = product;
            b.CpuVendor = Rx.IsMatch(cpu, "AMD") ? "AMD" : Rx.IsMatch(cpu, "Intel") ? "Intel" : "other";
            VendorInfo v = MakerFor(maker);
            if (v != null)
            {
                b.App = v.App; b.AppMatch = v.AppMatch; b.WingetId = v.WingetId; b.SupportUrl = v.Url;
                InstalledApp app = InstalledApps.FindFirst(v.AppMatch);
                b.AppInstalled = app != null;
                b.AppVersion = app != null ? app.Version : null;
            }
            if (b.CpuVendor == "AMD")
            {
                InstalledApp chip = InstalledApps.FindFirst("AMD Chipset Software");
                if (chip != null) b.ChipsetInstalled = chip.Version;
            }
            Log.Diag(string.Format("Board: {0} {1}, BIOS {2} ({3}, {4} months), CPU {5}, maker app {6}", maker, product, b.BiosVersion, b.BiosDate, b.BiosAgeMonths, b.CpuVendor, b.App ?? "none"));
            return b;
        }

        // Each fixed drive with the maker's tool for its firmware. USB sticks and virtual disks are left out.
        public static List<DiskInfo> Drives()
        {
            List<DiskInfo> list = new List<DiskInfo>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", "SELECT Model, FirmwareRevision, InterfaceType, MediaType, PNPDeviceID FROM Win32_DiskDrive"))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementBaseObject o in rows)
                    {
                        using (o)
                        {
                            DiskInfo d = new DiskInfo();
                            d.Model = Util.Str(o["Model"]).Trim();
                            d.Firmware = Util.Str(o["FirmwareRevision"]).Trim();
                            d.Bus = Util.Str(o["InterfaceType"]);
                            string pnp = Util.Str(o["PNPDeviceID"]);
                            if (Rx.IsMatch(pnp, "^(USBSTOR|SWD|ROOT|STORAGE\\\\VOLUME)") || Rx.IsMatch(Util.Str(o["MediaType"]), "Removable")) continue;
                            DiskInfo same = list.Find(x => x.Model == d.Model && x.Firmware == d.Firmware);
                            if (same != null) { same.Count++; continue; }
                            VendorInfo v = DriveMakerFor(d.Model);
                            if (v != null)
                            {
                                d.Vendor = v.Name; d.Tool = v.App; d.ToolMatch = v.AppMatch; d.WingetId = v.WingetId; d.Url = v.Url;
                                d.ToolInstalled = InstalledApps.FindFirst(v.AppMatch) != null;
                            }
                            list.Add(d);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Diag("Drive query failed: " + ex.Message); }
            return list;
        }
    }
}
