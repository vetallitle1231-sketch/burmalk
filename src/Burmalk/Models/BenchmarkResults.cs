namespace Burmalk.Models
{
    /// <summary>Benchmark results. "Not run" until the user explicitly starts it.</summary>
    public class BenchmarkResults
    {
        public string CpuScore = "Not run";
        public string RamScore = "Not run";
        public string GpuInfo = "Not run";
        public string DiskInfo = "Not run";
        public double DiskReadMbps;
        public double DiskWriteMbps;
    }
}

