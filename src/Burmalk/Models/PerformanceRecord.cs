using System;

namespace Burmalk.Models
{
    public sealed class PerformanceRecord
    {
        public string MethodVersion = "";
        public DateTime TestedUtc;
        public string CpuModel = "", GpuModel = "";
        public double RamGb;
        public double CpuMillionOps, RamMegabytesPerSecond, GpuFps;
        public double CpuSeconds, GpuSeconds;
        public long MemoryBytesVerified;
        public bool CpuCompleted, RamCompleted, GpuCompleted, MemoryPassed;
        public int RenderTier;
        public string Status = "Не протестировано";
        public double MaxCpuTemp = double.NaN, MaxGpuTemp = double.NaN;
        public double MaxCpuMhz = double.NaN, MaxGpuMhz = double.NaN;
    }

    public sealed class PcRankResult
    {
        public bool Confirmed;
        public string Name = "Не протестировано", Letter = "—", Reason = "Пройди полный тест PC RANK";
        public double Total, Cpu, Gpu, Ram;
    }
}
