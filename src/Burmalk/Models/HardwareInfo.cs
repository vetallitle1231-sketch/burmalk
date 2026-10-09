using System;
using System.Collections.Generic;

namespace Burmalk.Models
{
    /// <summary>Immutable per-category hardware snapshot, probed once at startup.</summary>
    public class CpuHardware
    {
        public string Manufacturer = "Unavailable";
        public string Model = "Unavailable";
        public int Cores;
        public int Threads;
        public double BaseGhz = double.NaN;
        public string Architecture = "Unavailable";
    }

    public class GpuHardware
    {
        public string Name = "Unavailable";
        public string DriverVersion = "Unavailable";
        public string DriverDate = "Unavailable";
        public double VramTotalGb = double.NaN;
    }

    public class RamModule
    {
        public string Manufacturer = "Unavailable";
        public string PartNumber = "Unavailable";
        public int CapacityGb;
        public int SpeedMhz;
        public string Slot = "Unavailable";
    }

    public class RamHardware
    {
        public double TotalGb;
        public List<RamModule> Modules = new List<RamModule>();
        public int MaxSpeedMhz;
    }

    public class MotherboardHardware
    {
        public string Manufacturer = "Unavailable";
        public string Model = "Unavailable";
        public string BiosVersion = "Unavailable";
        public string BiosDate = "Unavailable";
        public string Chipset = "Unavailable";
    }

    public class SystemHardware
    {
        public string ComputerName = "Unavailable";
        public string OsName = "Unavailable";
        public string OsVersion = "Unavailable";
        public string OsBuild = "Unavailable";
        public int UptimeSeconds;
        public string Architecture = "Unavailable";
    }

    public class HardwareSnapshot
    {
        public CpuHardware Cpu = new CpuHardware();
        public GpuHardware Gpu = new GpuHardware();
        public RamHardware Ram = new RamHardware();
        public MotherboardHardware Motherboard = new MotherboardHardware();
        public SystemHardware System = new SystemHardware();
    }
}


