using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>
    /// Task-manager style process snapshot. Reads only; NEVER terminates anything.
    /// A manual kill action in the UI is available only when the user explicitly
    /// confirms it (handled in the view, not here).
    /// </summary>
    public static class ProcessSnapshot
    {
        private static readonly object Gate = new object();
        private static Dictionary<int, (long Start, double Cpu, long Time)> Previous = new Dictionary<int, (long, double, long)>();
        public static List<ProcessRow> Capture() { lock (Gate) { return CaptureCore(); } }
        private static List<ProcessRow> CaptureCore()
        {
            var rows = new List<ProcessRow>();
            var next = new Dictionary<int, (long, double, long)>();
            long now = Stopwatch.GetTimestamp();
            Process[] procs = null;
            try
            {
                procs = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("ProcessSnapshot: " + ex.Message);
                return rows;
            }
            for (int i = 0; i < procs.Length; i++)
            {
                var p = procs[i];
                try
                {
                    double memoryMb = (p.WorkingSet64) / (1024d * 1024d);
                    double percent = double.NaN;
                    try {
                        long start = p.StartTime.ToUniversalTime().Ticks; double cpu = p.TotalProcessorTime.TotalSeconds;
                        if (Previous.TryGetValue(p.Id, out var old) && old.Start == start && now > old.Time)
                            percent = Math.Clamp((cpu - old.Cpu) / ((now-old.Time)/(double)Stopwatch.Frequency) / Environment.ProcessorCount * 100,0,100);
                        next[p.Id] = (start,cpu,now);
                    } catch { }
                    rows.Add(new ProcessRow
                    {
                        Pid = p.Id,
                        Name = SafeName(p),
                        // Per-process CPU is not directly exposed by the Process API on Windows
                        // without a two-sample delta; left 0 (honest) here.
                        CpuPercent = percent,
                        MemoryMb = memoryMb,
                        Executable = ExecutablePath(p)
                    });
                }
                catch { }
                try { p.Dispose(); } catch { }
            }
            Previous = next;
            return rows;
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; }
            catch
            {
                try
                {
                    p.Refresh();
                    return p.ProcessName;
                }
                catch { return "Unavailable"; }
            }
        }

        private static string ExecutablePath(Process p)
        {
            try
            {
                var path = p.MainModule != null ? p.MainModule.FileName : null;
                if (string.IsNullOrWhiteSpace(path))
                {
                    try { path = p.StartInfo.FileName; } catch { }
                }
                return string.IsNullOrWhiteSpace(path) ? "Unavailable" : path;
            }
            catch
            {
                return "Unavailable";
            }
        }
    }
}


