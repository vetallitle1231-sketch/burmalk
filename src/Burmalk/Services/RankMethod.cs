using System;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>Relative throughput index, not an industry-wide gaming-power percentile.</summary>
    public static class RankMethod
    {
        public const string Version = "burmalk-relative-v1";
        // Filled from a complete measured calibration before shipping, not model-name guesses.
        public const double ReferenceCpu = 1144.1312189202115;
        public const double ReferenceRam = 9799.534218545652;
        public const double ReferenceGpu = 131.25828396867522;
        public static bool Calibrated => ReferenceCpu > 0 && ReferenceRam > 0 && ReferenceGpu > 0;
        public static bool Complete(PerformanceRecord r) => r != null && r.MethodVersion == Version &&
            r.CpuCompleted && r.RamCompleted && r.GpuCompleted && r.MemoryPassed && r.RenderTier > 0 &&
            r.CpuSeconds >= 59 && r.GpuSeconds >= 119 && r.MemoryBytesVerified >= 128L * 1024 * 1024 &&
            Positive(r.CpuMillionOps) && Positive(r.RamMegabytesPerSecond) && Positive(r.GpuFps);
        private static bool Positive(double n) => double.IsFinite(n) && n > 0;
        public static PcRankResult Evaluate(PerformanceRecord r)
        {
            if (r == null) return new PcRankResult();
            if (r.MethodVersion != Version) return new PcRankResult { Reason = "Методика изменилась. Пройди тест заново." };
            if (!Complete(r)) return new PcRankResult { Reason = "Тест не завершён полностью: " + r.Status };
            if (!Calibrated) return new PcRankResult { Reason = "Калибровка методики ещё не завершена." };
            return Calculate(r, ReferenceCpu, ReferenceRam, ReferenceGpu);
        }
        public static PcRankResult Calculate(PerformanceRecord r, double cpuReference, double ramReference, double gpuReference)
        {
            if (!Complete(r) || !Positive(cpuReference) || !Positive(ramReference) || !Positive(gpuReference)) return new PcRankResult();
            var result = new PcRankResult { Confirmed = true, Cpu = 1000 * r.CpuMillionOps / cpuReference,
                Ram = 1000 * r.RamMegabytesPerSecond / ramReference, Gpu = 1000 * r.GpuFps / gpuReference };
            result.Total = .5 * result.Gpu + .35 * result.Cpu + .15 * result.Ram;
            if (!Positive(result.Total) || !Positive(result.Cpu) || !Positive(result.Ram) || !Positive(result.Gpu)) return new PcRankResult { Reason = "Недопустимые результаты теста." };
            // Centers: 1/4, 1/2, 1, 2 and 4 reference throughputs. Geometric midpoints
            // put the reference in the center of GOLD, rather than on a noisy band edge.
            double midpoint = Math.Sqrt(2);
            (result.Name, result.Letter) = result.Total < 250 * midpoint ? ("BRONZE", "D") : result.Total < 500 * midpoint ? ("SILVER", "C") :
                result.Total < 1000 * midpoint ? ("GOLD", "B") : result.Total < 2000 * midpoint ? ("DIAMOND", "A") : ("LEGENDARY", "S");
            result.Reason = "GPU 50% · CPU 35% · RAM 15% / " + Version;
            return result;
        }
        public static bool MatchesHardware(PerformanceRecord r, HardwareSnapshot h, string liveGpu) => r == null ||
            (string.Equals(r.CpuModel?.Trim(), h.Cpu.Model?.Trim(), StringComparison.OrdinalIgnoreCase) &&
             string.Equals(r.GpuModel?.Trim(), liveGpu?.Trim(), StringComparison.OrdinalIgnoreCase) && Math.Abs(r.RamGb - h.Ram.TotalGb) < .5);
    }
}
