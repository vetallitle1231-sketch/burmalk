using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>One sample of aggregate disk traffic (KB/s). NaN = unavailable.</summary>
    public class DiskSample
    {
        public double ReadKBps = double.NaN;
        public double WriteKBps = double.NaN;
        public double ActivePercent = double.NaN;
    }

    /// <summary>
    /// Storage: aggregate read/write (KB/s) from the "PhysicalDisk" perf counter family,
    /// plus a per-drive space listing from DriveInfo + a Win32_DiskDrive kind map.
    /// </summary>
    public class StorageSampler : IDisposable
    {
        private readonly object _gate = new object();
        private readonly System.Diagnostics.PerformanceCounter _read;
        private readonly System.Diagnostics.PerformanceCounter _write;
        private readonly System.Diagnostics.PerformanceCounter _active;
        private List<PhysicalDisk> _physical = new List<PhysicalDisk>();
        private List<StorageDrive> _drives = new List<StorageDrive>();
        
        private bool _initFailed;

        public bool Initialized { get { return !_initFailed && _read != null && _write != null && _active != null; } }

        /// <summary>Physical drive metadata (WMI), populated by Init().</summary>
        public List<PhysicalDisk> PhysicalDrives { get { lock(_gate) { return _physical.ToList(); } } }

        public StorageSampler()
        {
            try
            {
                // PhysicalDisk counters: use the (_Total) pseudo-instance (aggregate across
                // physical disks). "Disk Read/Write Bytes/sec" + "% Idle Time" for activity.
                _read = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _write = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                _active = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total");
                _read.NextValue();
                _write.NextValue();
                _active.NextValue();
            }
            catch (Exception)
            {
                _read?.Dispose();_write?.Dispose();_active?.Dispose();
                // fall back to empty instance if _Total is missing
                try
                {
                    _read = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec");
                    _write = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec");
                    _active = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "% Idle Time");
                    _read.NextValue(); _write.NextValue(); _active.NextValue();
                }
                catch (Exception ex2)
                {
                    ErrorLog.Write("Storage perf counters: " + ex2.Message);
                    _initFailed = true;
                }
            }
            RefreshSpace();
        }

        public void Init()
        {
            // build physical drive list from WMI (name/kind/model/capacity)
            try
            {
                var diskTypes = WmiHelper.Query("SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk", 3000, "root\\Microsoft\\Windows\\Storage");
                var raw = WmiHelper.Query(
                    "SELECT Name, DeviceID, MediaType, Model, Size, Index FROM Win32_DiskDrive");
                foreach (var r in raw)
                {
                    var pd = new PhysicalDisk();
                    try { pd.Name = (string)r["Name"]; } catch { }
                    try {
                        var mediaType = (string)r["MediaType"] ?? "";
                        if (mediaType.IndexOf("SSD", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            pd.Kind = "SSD";
                        else if (mediaType.IndexOf("fixed", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            pd.Kind = "Unavailable";
                        else if (string.IsNullOrWhiteSpace(mediaType))
                            pd.Kind = "Unavailable";
                        else
                            pd.Kind = mediaType.Trim();
                    } catch { }
                    try {
                        var type = diskTypes.FirstOrDefault(t => Convert.ToInt32(t["DeviceId"]) == Convert.ToInt32(r["Index"]));
                        if(type!=null) pd.Kind=Convert.ToInt32(type["MediaType"]) switch {3=>"HDD",4=>"SSD",5=>"SCM",_=>"Unavailable"};
                    }catch{}
                    try { pd.Model = (string)r["Model"]; } catch { }
                    try
                    {
                        long bytes = Convert.ToInt64(r["Size"]);
                        pd.CapacityGb = bytes > 0 ? bytes / (1024L * 1024 * 1024) : 0;
                    }
                    catch { }
                    lock(_gate) { _physical.Add(pd); }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Storage WMI: " + ex.Message);
            }
        }

        /// <summary>Cached aggregate sample (refreshed by the monitor every tick).</summary>
        public DiskSample Sample()
        {
            lock (_gate)
            {
                var s = new DiskSample();
                if (!Initialized) return s;
                try
                {
                    s.ReadKBps = ToKBps(_read.NextValue());
                    s.WriteKBps = ToKBps(_write.NextValue());
                    double qlen = _active.NextValue();
                    s.ActivePercent = double.IsNaN(qlen) ? double.NaN : Math.Clamp(100 - qlen, 0, 100);
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Storage sample: " + ex.Message);
                }
                return s;
            }
        }

        private static double ToKBps(double bytesPerSec)
        {
            if (double.IsNaN(bytesPerSec) || bytesPerSec < 0) return double.NaN;
            return bytesPerSec / 1024.0;
        }

        /// <summary>Refresh per-drive space from DriveInfo (cheap; called on startup and on demand).</summary>
        public IEnumerable<StorageDrive> RefreshSpace()
        {
            lock (_gate)
            {
                _drives.Clear();
                try
                {
                    foreach (var info in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (!info.IsReady) continue;
                            var d = new StorageDrive
                            {
                                Letter = info.Name.TrimEnd('\\', '/'),
                                VolumeName = string.IsNullOrWhiteSpace(info.VolumeLabel)
                                    ? "Local Disk"
                                    : info.VolumeLabel,
                                TotalGb = info.TotalSize / (1024L * 1024 * 1024),
                                FreeGb = info.AvailableFreeSpace / (1024L * 1024 * 1024),
                                UsedGb = (info.TotalSize - info.AvailableFreeSpace) / (1024L * 1024 * 1024),
                                UsedPercent = info.TotalSize > 0
                                    ? (double)(info.TotalSize - info.AvailableFreeSpace) / info.TotalSize * 100.0
                                    : 0
                            };
                            // .NET 8 DriveInfo exposes no MediaType property; report Fixed
                            // for local logical drives and let the view stay honest.
                            d.MediaKind = (info.DriveType == DriveType.Fixed || info.DriveType == DriveType.Ram)
                                ? "Fixed"
                                : (info.DriveType == DriveType.Removable ? "Removable" : string.Empty);
                            _drives.Add(d);
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("DriveInfo: " + ex.Message);
                }
                return _drives.ToList();
            }
        }

        public void Dispose()
        {
            try { _read?.Dispose(); } catch { }
            try { _write?.Dispose(); } catch { }
            try { _active?.Dispose(); } catch { }
        }
    }
}






