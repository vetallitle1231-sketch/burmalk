using System;
using System.Diagnostics;

namespace Burmalk.Services
{
    /// <summary>A single GPU sample (NaN = unavailable).</summary>
    public class GpuSample
    {
        public double Percent = double.NaN;
        public double TempC = double.NaN;
        public double VramUsedGb = double.NaN;
        public double VramTotalGb = double.NaN;
        public double ClockMhz = double.NaN;      // graphics
        public double MemClockMhz = double.NaN;   // memory
        public double PowerW = double.NaN;
        public string Name = "Unavailable";
        public string DriverVersion = "Unavailable";

        public GpuSample Clone()
        {
            return new GpuSample
            {
                Percent = Percent, TempC = TempC,
                VramUsedGb = VramUsedGb, VramTotalGb = VramTotalGb,
                ClockMhz = ClockMhz, MemClockMhz = MemClockMhz,
                PowerW = PowerW, Name = Name, DriverVersion = DriverVersion
            };
        }
    }

    /// <summary>
    /// GPU source: nvidia-smi (verified on this box). Values are NaN until a real parse
    /// succeeds; the app never invents a number.
    /// </summary>
    public class GpuSampler
    {
        private static readonly object _gate = new object();
        private GpuSample _last = new GpuSample();
        private string _availCheck;
        private bool _availabilityKnown;

        public GpuSample Last { get { lock (_gate) { return _last.Clone(); } } }

        public bool Available
        {
            get
            {
                lock (_gate)
                {
                    if (!_availabilityKnown)
                    {
                        _availabilityKnown = true;
                        _availCheck = Probe();
                    }
                    return _availCheck == "ok";
                }
            }
        }

        private string Probe()
        {
            try
            {
                var outStr = RunSmi("--query-gpu=name,driver_version --format=csv,noheader,nounits");
                if (!string.IsNullOrWhiteSpace(outStr) && !outStr.Contains("NVML")
                    && !outStr.ToLower().Contains("error"))
                    return "ok";
            }
            catch (Exception) { }
            return "no";
        }

        public GpuSample Sample()
        {
            {
                var s = new GpuSample();
                if (!Available)
                    return Remember(s);

                string line;
                try
                {
                    line = RunSmi(
                        "--query-gpu=name,driver_version,temperature.gpu,utilization.gpu," +
                        "clocks.current.graphics,clocks.current.memory,power.draw,memory.used,memory.total " +
                        "--format=csv,noheader,nounits");
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("GpuSampler: " + ex.Message);
                    return Remember(s);
                }
                if (string.IsNullOrWhiteSpace(line)) return Remember(s);

                var parts = SplitCsv(line.Trim().Split((char)10)[0]);
                try
                {
                    s.Name = Clean(parts[0]);
                    s.DriverVersion = Clean(parts[1]);
                    s.TempC = ParseDouble(parts[2]);
                    s.Percent = ParseDouble(parts[3]);
                    s.ClockMhz = ParseDouble(parts[4]);
                    s.MemClockMhz = ParseDouble(parts[5]);
                    s.PowerW = ParseDouble(parts[6]);
                    double usedMiB = ParseDouble(parts[7]);
                    double totalMiB = ParseDouble(parts[8]);
                    s.VramUsedGb = MiBToGb(usedMiB);
                    s.VramTotalGb = MiBToGb(totalMiB);
                }
                catch { /* keep prior value on partial parse */ }

                return Remember(s);
            }
        }
        private GpuSample Remember(GpuSample s)
        {
            lock(_gate){_last=s.Clone();} return s;
        }

        /// <summary>Total VRAM for the Hardware page (from driver-reported MiB).</summary>
        public double TotalVramGb
        {
            get
            {
                var s = Last;
                return s.VramTotalGb > 0 ? s.VramTotalGb : double.NaN;
            }
        }

        public static double MiBToGb(double mib)
        {
            if (double.IsNaN(mib)) return double.NaN;
            return mib / 1024.0;
        }

        public static double ParseDouble(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return double.NaN;
            double v;
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return double.NaN;
        }

        private static string Clean(string s)
        {
            s = (s ?? "").Trim();
            return s.Length == 0 ? "Unavailable" : s;
        }

        private static string[] SplitCsv(string line)
        {
            var raw = line.Trim().Split(',');
            return raw;
        }

        private static string RunSmi(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEndAsync();
                var errors = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(3000)) { p.Kill(); return ""; }
                string outStr = output.GetAwaiter().GetResult();
                return outStr;
            }
        }
    }
}



