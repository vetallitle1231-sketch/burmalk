using System;
using System.Collections.Generic;

namespace Burmalk.Models
{
    /// <summary>Live state of the current (single) detected game session.</summary>
    public class GameCurrent
    {
        public bool Active;
        public string GameName;
        public string Executable;
        public int Pid;
        public DateTime StartedUtc;
        public double DurationSeconds;

        // FPS: honest availability (no fake values)
        public bool FpsAvailable;
        public string FpsSource = "";
        public double FpsCurrent;
        public double FpsAvg;
        public double FpsMin = double.MaxValue;
        public double FpsMax;
        public double Fps1Low;
        public double Fps01Low;
        public int FpsSamples;

        // Live system context (copied from Sample each tick)
        public double GpuPercent;
        public double GpuTempC;
        public double GpuVramUsedGb;
        public double CpuPercent;
        public double CpuTempC = double.NaN;
        public double RamUsedGb;
    }

    /// <summary>A persisted finished session.</summary>
    public class GameSession
    {
        public string GameName;
        public DateTime StartedUtc;
        public double DurationSeconds;
        public bool FpsAvailable;
        public double FpsAvg;
        public double FpsMin;
        public double FpsMax;
        public double Fps1Low;
        public double Fps01Low;
        public double GpuAvgPercent;
        public double CpuAvgPercent;
        public double CpuTempAvg = double.NaN;
        public double GpuTempAvg;
        public double GpuVramAvgGb;
        public double RamAvgGb;
    }

    /// <summary>User-configurable per-game preferences.</summary>
    public class GameProfile
    {
        public string GameName;
        public bool OverlayEnabled = true;
        public int OverlayRefreshMs = 1000;
        public bool ShowGpu = true;
        public bool ShowCpu = true;
        public bool ShowVram = true;
        public bool ShowRam = true;
    }
}

