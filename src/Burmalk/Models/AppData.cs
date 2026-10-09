namespace Burmalk.Models
{
    /// <summary>Persisted user settings (settings.json).</summary>
    public class AppSettings
    {
        // Appearance
        public string Theme = "Dark";
        public string BackgroundImage = "";
        public double BackgroundDim = 0.5;
        public bool CompactLayout;
        public bool OverlayTransparent = true;

        // Monitoring
        public int UpdateIntervalMs = 1000;
        public int GraphHistorySeconds = 300;

        // FPS overlay
        public bool OverlayEnabled;
        public string OverlayPosition = "TopLeft";
        public int OverlayRefreshMs = 1000;
        public double OverlayOpacity = 0.85;
        public int OverlayFontSize = 14;
        public string OverlayHotkey = "F8";
        public bool OverlayShowGpu = true;
        public bool OverlayShowCpu = true;
        public bool OverlayShowVram = true;
        public bool OverlayShowRam = true;

        // AI
        public string AiEndpoint = "http://127.0.0.1:11434";

        // Gaming
        public bool GameDetectionEnabled = true;
        public string SessionsFile = "sessions.json";
        public string ProfilesFile = "profiles.json";
    }
}

