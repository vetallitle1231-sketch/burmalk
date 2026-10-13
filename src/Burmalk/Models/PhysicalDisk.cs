namespace Burmalk.Models
{
    /// <summary>One physical disk (perf counter scope) plus its mapped kind.</summary>
    public class PhysicalDisk
    {
        public string Name = "Unavailable";
        public string Kind = "Unavailable";      // SSD / HDD
        public string Model = "Unavailable";
        public long CapacityGb;
        public long CapacityBytes;
        public int Index;
        public System.Collections.Generic.List<string> VolumeLetters = new();
        public double ReadKBps;                  // current
        public double WriteKBps;                 // current
        public double ActivePercent;             // 0..100
        public string SmartStatus = "Unavailable";
    }
}

