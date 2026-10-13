using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>
    /// Probes static (change-rarely) hardware facts once at startup via WMI + nvidia-smi.
    /// Honest data only: anything that cannot be read is left "Unavailable".
    /// </summary>
    public class HardwareInfoService
    {
        public HardwareSnapshot Snapshot { get; private set; } = new HardwareSnapshot();

        public bool ProbeCompleted { get; private set; }

        // background probe so we do not block the first UI paint
        public event Action Probed;

        public HardwareInfoService()
        {
        }

        public void StartProbe()
        {
            var t = new Thread(ProbeNow) { IsBackground = true, Name = "HardwareProbe" };
            t.Start();
        }

        public void ProbeNow()
        {
            if (ProbeCompleted) return;
            var snap = new HardwareSnapshot();
            try { ProbeCpu(snap); } catch (Exception ex) { ErrorLog.Write("ProbeCpu: " + ex.Message); }
            try { ProbeGpu(snap); } catch (Exception ex) { ErrorLog.Write("ProbeGpu: " + ex.Message); }
            try { ProbeRam(snap); } catch (Exception ex) { ErrorLog.Write("ProbeRam: " + ex.Message); }
            try { ProbeMotherboard(snap); } catch (Exception ex) { ErrorLog.Write("ProbeMotherboard: " + ex.Message); }
            try { ProbeSystem(snap); } catch (Exception ex) { ErrorLog.Write("ProbeSystem: " + ex.Message); }

            Snapshot = snap;
            ProbeCompleted = true;
            Probed?.Invoke();
        }

        private static string S(object v)
        {
            if (v == null) return "Unavailable";
            string s = v.ToString();
            return string.IsNullOrWhiteSpace(s) ? "Unavailable" : s;
        }

        private void ProbeCpu(HardwareSnapshot snap)
        {
            // CPU manufacturer + model from Win32_Processor
            var cpus = WmiHelper.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Architecture FROM Win32_Processor");
            if (cpus.Count > 0)
            {
                var c = cpus[0];
                snap.Cpu.Manufacturer = S(c["Manufacturer"]);
                snap.Cpu.Model = S(c["Name"]);
                try { snap.Cpu.Cores = (int)Convert.ToInt64(c["NumberOfCores"]); } catch { }
                try { snap.Cpu.Threads = (int)Convert.ToInt64(c["NumberOfLogicalProcessors"]); } catch { }
                try
                {
                    long mhz = Convert.ToInt64(c["MaxClockSpeed"]);
                    snap.Cpu.BaseGhz = mhz > 0 ? mhz / 1000.0 : 0.0;
                }
                catch { }
                // Architecture: Win32_Processor may expose it; fall back to runtime
                if (c["Architecture"] == null)
                {
                    snap.Cpu.Architecture = IntPtr.Size == 8 ? "64-bit (x64)" : "32-bit (x86)";
                }
                else
                {
                    snap.Cpu.Architecture = (int)Convert.ToInt64(c["Architecture"]) == 9 ? "x64" : S(c["Architecture"]);
                }
            }

            // OS CPU architecture (authoritative)
            var os = WmiHelper.Query("SELECT Caption FROM Win32_OperatingSystem");
            if (os.Count > 0 && S(os[0]["Caption"]).Contains("64"))
                snap.Cpu.Architecture = "64-bit (x64)";
        }

        private void ProbeGpu(HardwareSnapshot snap)
        {
            // Static GPU facts from Win32_VideoController (name, driver version, VRAM).
            // Live driver/build date comes from nvidia-smi during sampling; here we set a
            // best-effort name + VRAM so the Hardware page is populated even before first sample.
            // (No WHERE clause: the ConfigManagerUserPU filter produced invalid-query errors
            //  on this host. Discrete-GPU selection is done in code below instead.)
            var gpus = WmiHelper.Query("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");

            if (gpus.Count > 0)
            {
                // Prefer a row that looks like a discrete GPU (has VRAM) — first entry with AdapterRAM
                var pick = gpus.FirstOrDefault(r => r["AdapterRAM"] != null) ?? gpus[0];
                snap.Gpu.Name = S(pick["Name"]);
                try { snap.Gpu.DriverVersion = S(pick["DriverVersion"]); } catch { }
                try
                {
                    long bytes = Convert.ToInt64(pick["AdapterRAM"]);
                    snap.Gpu.VramTotalGb = bytes > 0 ? bytes / (1024d * 1024 * 1024) : 0.0;
                }
                catch { }
            }
        }

        private void ProbeRam(HardwareSnapshot snap)
        {
            var os = WmiHelper.Query("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            if (os.Count > 0 && os[0]["TotalVisibleMemorySize"] != null)
            {
                long kb = Convert.ToInt64(os[0]["TotalVisibleMemorySize"]);
                snap.Ram.TotalGb = kb > 0 ? kb / (1024.0 * 1024) : 0;
            }

            var modules = WmiHelper.Query("SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, DeviceLocator FROM Win32_PhysicalMemory");
            foreach (var m in modules)
            {
                var mod = new RamModule
                {
                    Manufacturer = S(m["Manufacturer"]),
                    PartNumber = S(m["PartNumber"]),
                    Slot = S(m["DeviceLocator"])
                };
                try
                {
                    long bytes = Convert.ToInt64(m["Capacity"]);
                    mod.CapacityGb = bytes > 0 ? (int)(bytes / (1024 * 1024 * 1024)) : 0;
                }
                catch { }
                try { mod.SpeedMhz = (int)Convert.ToInt64(m["Speed"]); } catch { }
                try { int configured = Convert.ToInt32(m["ConfiguredClockSpeed"]); if (configured > 0) mod.SpeedMhz = configured; } catch { }
                try { mod.MemoryType = Convert.ToInt32(m["SMBIOSMemoryType"]) switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "Unavailable" }; } catch { }
                snap.Ram.Modules.Add(mod);
                if (mod.SpeedMhz > snap.Ram.MaxSpeedMhz) snap.Ram.MaxSpeedMhz = mod.SpeedMhz;
            }

            if (snap.Ram.TotalGb <= 0 && snap.Ram.Modules.Count > 0)
                snap.Ram.TotalGb = snap.Ram.Modules.Sum(m => m.CapacityGb);
            snap.Ram.MemoryType = string.Join(" / ", snap.Ram.Modules.Select(m => m.MemoryType).Where(t => t != "Unavailable").Distinct());
            if (snap.Ram.MemoryType.Length == 0) snap.Ram.MemoryType = "Unavailable";
        }

        private void ProbeMotherboard(HardwareSnapshot snap)
        {
            var boards = WmiHelper.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            if (boards.Count > 0)
            {
                snap.Motherboard.Manufacturer = S(boards[0]["Manufacturer"]);
                snap.Motherboard.Model = S(boards[0]["Product"]);
            }

            var bios = WmiHelper.Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            if (bios.Count > 0)
            {
                snap.Motherboard.BiosVersion = S(bios[0]["SMBIOSBIOSVersion"]);
                try
                {
                    var rd = bios[0]["ReleaseDate"];
                    if (rd != null)
                    {
                        // DateTimeObject returns a DateTime
                        if (rd is DateTime dt)
                            snap.Motherboard.BiosDate = dt.ToString("yyyy-MM-dd");
                        else snap.Motherboard.BiosDate = ManagementDateTimeConverter.ToDateTime(rd.ToString()).ToString("yyyy-MM-dd");
                    }
                }
                catch { }
            }
        }

        private void ProbeSystem(HardwareSnapshot snap)
        {
            // Machine name: Environment.MachineName is the canonical, always-available
            // source (WMI Win32_ComputerSystem.ComputerName errors as invalid-query on this host).
            snap.System.ComputerName = Environment.MachineName;
            foreach (var row in WmiHelper.Query("SELECT UserFriendlyName FROM WmiMonitorID", 3000, "root\\WMI"))
            {
                try { if (row["UserFriendlyName"] is ushort[] codes) { string name = new string(codes.Where(c => c != 0).Select(c => (char)c).ToArray()); if (name.Length > 0) snap.System.Monitors.Add(name); } } catch { }
            }
            foreach (var row in WmiHelper.Query("SELECT CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController", 3000))
            {
                try { int w = Convert.ToInt32(row["CurrentHorizontalResolution"]), h = Convert.ToInt32(row["CurrentVerticalResolution"]), hz = Convert.ToInt32(row["CurrentRefreshRate"]); if (w > 0 && h > 0) snap.System.Monitors.Add(w + " × " + h + (hz > 1 ? " · " + hz + " Hz" : "")); } catch { }
            }

            var os = WmiHelper.Query("SELECT Caption, Version, BuildNumber, LastBootUpTime, OSArchitecture FROM Win32_OperatingSystem");
            if (os.Count > 0)
            {
                snap.System.OsName = S(os[0]["Caption"]);
                snap.System.OsVersion = S(os[0]["Version"]);
                snap.System.OsBuild = S(os[0]["BuildNumber"]);
                snap.System.Architecture = S(os[0]["OSArchitecture"]);
                try
                {
                    var boot = os[0]["LastBootUpTime"];
                    if (boot != null)
                    {
                        DateTime bootDt;
                        if (boot is DateTime dt) bootDt = dt.ToLocalTime();
                        else bootDt = ManagementDateTimeConverter.ToDateTime(S(boot)).ToLocalTime();
                        snap.System.UptimeSeconds = (int)(DateTime.Now - bootDt).TotalSeconds;
                    }
                }
                catch { }
            }
        }
    }
}

