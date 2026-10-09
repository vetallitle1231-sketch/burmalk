namespace Burmalk.Models
{
    /// <summary>One logical drive (DriveInfo scope). Space + kind; per-drive speeds are
    /// reported via PhysicalDisk because Windows does not reliably attribute
    /// read/write bytes to logical partitions.</summary>
    public class StorageDrive
    {
        public string Letter = "";
        public string VolumeName = "";
        public string MediaKind = "Unavailable"; // SSD / HDD
        public long TotalGb;
        public long UsedGb;
        public long FreeGb;
        public double UsedPercent;
    }
}

