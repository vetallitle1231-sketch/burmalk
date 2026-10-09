namespace Burmalk.Models
{
    public class ProcessRow
    {
        public string CpuDisplay => double.IsNaN(CpuPercent) ? "Unavailable" : CpuPercent.ToString("0.0");
        public int Pid { get; set; }
        public string Name { get; set; } = "";
        public double CpuPercent { get; set; } = double.NaN;
        public double MemoryMb { get; set; }
        public string Executable { get; set; } = "Unavailable";
    }
}


