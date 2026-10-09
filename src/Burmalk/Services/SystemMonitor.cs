using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>
    /// Single dispatcher-timer hub. Every tick (default 1 s) it samples CPU/RAM (fast perf
    /// counters), refreshes GPU (nvidia-smi) at a reduced rate, refreshes disk/network
    /// (perf counters) and pushes each value into a MetricHistory ring buffer. Views bind to
    /// the buffers + LastSample; they never touch WMI/perf counters directly.
    /// CPU stays low: fast counters every tick, slow processes (nvidia-smi) at reduced rate.
    /// </summary>
    public class SystemMonitor : IDisposable
    {
        private readonly DataStore _store;
        private readonly DispatcherTimer _timer;
        private readonly MetricHub _hub = new MetricHub();
        private readonly GpuSampler _gpu = new GpuSampler();
        public CpuSensors CpuSensors {get;} = new CpuSensors();
        private readonly NetworkSampler _net = new NetworkSampler();
        private readonly StorageSampler _storage = new StorageSampler();
        private long _lastGpuTickUtc = 0;
        private int _tickCount;
        private bool _disposed;

        private const int GpuEveryNTicks = 2;   // nvidia-smi every ~2 s
        private bool _sampling;

        public MetricHub Hub { get { return _hub; } }
        public Sample LastSample { get; private set; } = new Sample();
        public HardwareInfoService Hardware { get; private set; }

        /// <summary>Raised on the dispatcher thread after each tick.</summary>
        public event Action<Sample> SampleUpdated;

        /// <summary>Sample handles exposed for views (no re-creation on the view side).</summary>
        public StorageSampler Storage { get { return _storage; } }
        public NetworkSampler Network { get { return _net; } }
        public GpuSampler Gpu { get { return _gpu; } }

        public SystemMonitor(DataStore store)
        {
            _store = store;
            _hub = new MetricHub(12000); // supports an hour at the shortest sampling interval
            Hardware = new HardwareInfoService();

            _timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(300, store.Settings.UpdateIntervalMs))
            };
            _timer.Tick += (s, e) => Tick();
        }

        public async void Start()
        {
            _sampling = true;
            try { await Task.Run(() =>
            {
            try
            {
                _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpu.NextValue(); // seed the pair
            }
            catch (Exception ex)
            {
                ErrorLog.Write("CPU perf counter init: " + ex.Message);
                _cpu = null;
            }

            try
            {
                _freeMem = new PerformanceCounter("Memory", "Available MBytes");
                _freeMem.NextValue();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Memory perf counter init: " + ex.Message);
                _freeMem = null;
            }

            try { _storage.Init(); } catch (Exception ex) { ErrorLog.Write("StorageSampler.Init: " + ex.Message); }
            try { _net.Init(); } catch (Exception ex) { ErrorLog.Write("NetworkSampler.Init: " + ex.Message); }

            Hardware.StartProbe();
            CpuSensors.Open();
            }); }
            catch(Exception ex) { ErrorLog.Write("Monitor initialization: "+ex); }
            finally { _sampling = false; }
            if(_disposed) { DisposeHandles(); return; }
            _timer.Start();
            // immediate first sample so graphs do not start empty
            Tick();
        }

        // ---- perf counter handles (lazy, guarded) ----
        private PerformanceCounter _cpu;
        private PerformanceCounter _freeMem;

        private async void Tick()
        {
            if (_disposed || _sampling) return;
            _sampling = true;
            try
            {
            var s = await Task.Run(CollectSample);
            if (_disposed) return;
            Publish(s);
            }
            catch (Exception ex) { ErrorLog.Write("Monitor tick: " + ex); }
            finally { _sampling = false; if (_disposed) DisposeHandles(); }
        }

        private Sample CollectSample()
        {
            _tickCount++;

            var s = new Sample { TimestampUtc = DateTime.UtcNow.Ticks };

            // CPU usage (fast)
            try
            {
                if (_cpu != null) s.CpuPercent = Math.Clamp(_cpu.NextValue(), 0, 100);
            }
            catch (Exception ex) { ErrorLog.Write("CPU sample: " + ex.Message); }
            var cpuSensors=CpuSensors.Sample();
            s.CpuTempC=cpuSensors.Temperature;s.CpuGhz=cpuSensors.Ghz;

            // RAM usage
            try
            {
                double totalGb = Hardware.Snapshot.Ram.TotalGb;
                if (totalGb > 0 && _freeMem != null)
                {
                    double availMb = _freeMem.NextValue();
                    double totalMb = totalGb * 1024.0;
                    s.RamUsedGb = Math.Max(0, (totalMb - availMb) / 1024.0);
                    s.RamPercent = s.RamUsedGb / totalGb * 100;
                }
            }
            catch (Exception ex) { ErrorLog.Write("RAM sample: " + ex.Message); }

            // GPU at reduced rate
            if (_gpu.Available)
            {
                if (DateTime.UtcNow.Ticks - _lastGpuTickUtc >= GpuEveryNTicks * TimeSpan.TicksPerSecond)
                {
                    _lastGpuTickUtc = DateTime.UtcNow.Ticks;
                    var g = _gpu.Sample();
                    s.GpuPercent = g.Percent;
                    s.GpuTempC = g.TempC;
                    s.GpuVramUsedGb = g.VramUsedGb;
                    s.GpuVramTotalGb = g.VramTotalGb;
                    s.GpuClockMhz = g.ClockMhz;
                    s.GpuMemClockMhz = g.MemClockMhz;
                    s.GpuPowerW = g.PowerW;
                }
                else
                {
                    var g = _gpu.Last;
                    s.GpuPercent = g.Percent;
                    s.GpuTempC = g.TempC;
                    s.GpuVramUsedGb = g.VramUsedGb;
                    s.GpuVramTotalGb = g.VramTotalGb;
                    s.GpuClockMhz = g.ClockMhz;
                    s.GpuMemClockMhz = g.MemClockMhz;
                    s.GpuPowerW = g.PowerW;
                }
            }

            // Disk + network (perf counters)
            try
            {
                var d = _storage.Sample();
                s.DiskReadKBps = d.ReadKBps;
                s.DiskWriteKBps = d.WriteKBps;
                s.DiskActivePercent = d.ActivePercent;
            }
            catch (Exception ex) { ErrorLog.Write("Disk sample: " + ex.Message); }
            try
            {
                var n = _net.Sample();
                s.NetDownKBps = n.DownKBps;
                s.NetUpKBps = n.UpKBps;
            }
            catch (Exception ex) { ErrorLog.Write("Net sample: " + ex.Message); }

            return s;
        }

        private void Publish(Sample s)
        {
            // One slot per sample, including unavailable sensors.
            var h = _hub;
            h.CpuPercent.Push(s.CpuPercent,s.TimestampUtc);
            h.CpuTempC.Push(s.CpuTempC,s.TimestampUtc);
            h.CpuGhz.Push(s.CpuGhz,s.TimestampUtc);
            h.GpuPercent.Push(s.GpuPercent,s.TimestampUtc);
            h.GpuTempC.Push(s.GpuTempC,s.TimestampUtc);
            h.GpuVramGb.Push(s.GpuVramUsedGb,s.TimestampUtc);
            h.GpuClockMhz.Push(s.GpuClockMhz,s.TimestampUtc);
            h.GpuPowerW.Push(s.GpuPowerW,s.TimestampUtc);
            h.RamUsedGb.Push(s.RamUsedGb,s.TimestampUtc);
            h.RamPercent.Push(s.RamPercent,s.TimestampUtc);
            if(Hardware.ProbeCompleted && double.IsFinite(s.GpuVramTotalGb)) Hardware.Snapshot.Gpu.VramTotalGb=s.GpuVramTotalGb;
            h.DiskReadKBps.Push(s.DiskReadKBps,s.TimestampUtc);
            h.DiskWriteKBps.Push(s.DiskWriteKBps,s.TimestampUtc);
            h.DiskActivePercent.Push(s.DiskActivePercent,s.TimestampUtc);
            h.NetDownKBps.Push(s.NetDownKBps,s.TimestampUtc);
            h.NetUpKBps.Push(s.NetUpKBps,s.TimestampUtc);

            LastSample = s;
            SampleUpdated?.Invoke(s);
        }

        public void SetInterval(int ms)
        {
            if (_timer != null && Math.Abs(_timer.Interval.TotalMilliseconds - ms) > 50)
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(300, ms));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _timer?.Stop(); } catch { }
            if (_sampling) return;
            DisposeHandles();
        }
        private void DisposeHandles()
        {
            try { _cpu?.Dispose(); } catch { }
            try { CpuSensors.Dispose(); } catch { }
            try { _freeMem?.Dispose(); } catch { }
            try { _storage.Dispose(); } catch { }
            try { _net.Dispose(); } catch { }
        }
    }

    /// <summary>Central ring buffers bound one-to-one with Sample fields.</summary>
    public class MetricHub
    {
        public MetricHistory CpuPercent { get; private set; }
        public MetricHistory CpuTempC { get; private set; }
        public MetricHistory CpuGhz { get; private set; }
        public MetricHistory GpuPercent { get; private set; }
        public MetricHistory GpuTempC { get; private set; }
        public MetricHistory GpuVramGb { get; private set; }
        public MetricHistory GpuClockMhz { get; private set; }
        public MetricHistory GpuPowerW { get; private set; }
        public MetricHistory RamUsedGb { get; private set; }
        public MetricHistory RamPercent { get; private set; }
        public MetricHistory DiskReadKBps { get; private set; }
        public MetricHistory DiskWriteKBps { get; private set; }
        public MetricHistory DiskActivePercent { get; private set; }
        public MetricHistory NetDownKBps { get; private set; }
        public MetricHistory NetUpKBps { get; private set; }

        public MetricHub(int capacity = 600)
        {
            CpuPercent = new MetricHistory(capacity);
            CpuTempC = new MetricHistory(capacity);
            CpuGhz = new MetricHistory(capacity);
            GpuPercent = new MetricHistory(capacity);
            GpuTempC = new MetricHistory(capacity);
            GpuVramGb = new MetricHistory(capacity);
            GpuClockMhz = new MetricHistory(capacity);
            GpuPowerW = new MetricHistory(capacity);
            RamUsedGb = new MetricHistory(capacity);
            RamPercent = new MetricHistory(capacity);
            DiskReadKBps = new MetricHistory(capacity);
            DiskWriteKBps = new MetricHistory(capacity);
            DiskActivePercent = new MetricHistory(capacity);
            NetDownKBps = new MetricHistory(capacity);
            NetUpKBps = new MetricHistory(capacity);
        }
    }
}



