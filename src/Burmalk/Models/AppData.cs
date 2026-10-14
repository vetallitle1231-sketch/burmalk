using System;
using System.Collections.Generic;

namespace Burmalk.Models
{
    public class DashboardPosition
    {
        public double X;
        public double Y;
    }
    /// <summary>Persisted user settings (settings.json).</summary>
    public class AppSettings
    {
        // Appearance
        public string Theme = "Burmalk";
        public string BackgroundImage = "";
        public string BackgroundImageName = "";
        public double BackgroundDim = 0.5;
        public bool CompactLayout;
        public bool ReducedEffects;
        public string PassportStyle = "Pulse";
        public bool PassportHidePersonal = true;
        public bool RankWidgetIntroduced;
        public bool PcModelWidgetIntroduced;
        public string PcModelChassis = "Auto";
        public string PcModelCooling = "Auto";
        public PcModelOptions PcModel = PcModelOptions.CompleteTower();
        public bool PcModelAnimateFans = true;
        public double PcWidgetYaw = 20, PcWidgetPitch = 12, PcWidgetDistance = 18;
        public string[] DashboardLayout = { "cpu", "gpu", "ram", "disk", "network", "game", "music", "system", "processes" };
        public string[] DashboardVisible = { "cpu", "gpu", "ram", "disk", "network", "game", "music", "system", "processes" };
        public Dictionary<string, DashboardPosition> DashboardPositions = new Dictionary<string, DashboardPosition>();

        // Monitoring
        public int UpdateIntervalMs = 1000;
        public bool CpuSensorsEnabled;
        // Channel identity only. Manual PWM values are never applied at startup.
        public string CpuFanBoardId = "";
        public string CpuFanControlId = "";
        public string CpuFanRpmSensorId = "";
        public int GraphHistorySeconds = 300;

        // FPS overlay
        public bool OverlayEnabled;
        public int OverlayRefreshMs = 1000;
        public string OverlayHotkey = "F8";
        public bool OverlayShowGpu = true;
        public bool OverlayShowCpu = true;
        public bool OverlayShowVram = true;
        public bool OverlayShowRam = true;
        public bool OverlayShowFps = true;
        public bool OverlayShowAverage = true;
        public bool OverlayShowFrameTime = true;
        public bool OverlayShowNetwork = true;
        public string OverlayLayout = "Lines";
        public string OverlayColor = "FFFFFF";
        public int OverlayTextScale = 100;

        // AI
        public string AiEndpoint = "http://127.0.0.1:11434";

        // Gaming
        public bool GameDetectionEnabled = true;
        public string SessionsFile = "sessions.json";
        public string ProfilesFile = "profiles.json";
        public bool StartWithWindows;
        public string[] GameLibraryPaths = Array.Empty<string>();
    }
}

