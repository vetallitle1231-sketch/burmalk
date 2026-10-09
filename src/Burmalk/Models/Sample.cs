using System;

namespace Burmalk.Models
{
    /// <summary>One 1-second system snapshot. double.NaN = metric unavailable.</summary>
    public class Sample
    {
        public long TimestampUtc;

        // CPU
        public double CpuPercent = double.NaN;      // 0..100
        public double CpuTempC = double.NaN;        // NaN if unavailable
        public double CpuGhz = double.NaN;          // current frequency, GHz

        // GPU
        public double GpuPercent = double.NaN;      // 0..100
        public double GpuTempC = double.NaN;
        public double GpuVramUsedGb = double.NaN;
        public double GpuVramTotalGb = double.NaN;
        public double GpuClockMhz = double.NaN;     // graphics clock
        public double GpuMemClockMhz = double.NaN;
        public double GpuPowerW = double.NaN;

        // RAM
        public double RamUsedGb = double.NaN;
        public double RamPercent = double.NaN;

        // Aggregate disks
        public double DiskReadKBps = double.NaN;
        public double DiskWriteKBps = double.NaN;
        public double DiskActivePercent = double.NaN;

        // Network
        public double NetDownKBps = double.NaN;
        public double NetUpKBps = double.NaN;
    }
}


