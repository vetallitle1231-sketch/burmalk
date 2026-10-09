namespace Burmalk.Models
{
    /// <summary>AI (Ollama) monitor status. Values are honest: unavailable => flag false.</summary>
    public class AiStatus
    {
        public string Endpoint = "http://127.0.0.1:11434";
        public bool Connected;
        public string Status = "Disconnected";
        public string ModelName = "Unavailable";
        public string ModelDetail = "Unavailable";
        public double ModelVramGb;     // 0 => not loaded
        public bool TpsAvailable;
        public double Tps;
        public long TotalTokens;
        public bool LatencyAvailable;
        public double LatencyMs;
        public string CurrentRequestStatus = "Idle";

        /// <summary>Member-wise copy so a view never sees a status being half-updated.</summary>
        public AiStatus Clone()
        {
            return new AiStatus
            {
                Endpoint = Endpoint,
                Connected = Connected,
                Status = Status,
                ModelName = ModelName,
                ModelDetail = ModelDetail,
                ModelVramGb = ModelVramGb,
                TpsAvailable = TpsAvailable,
                Tps = Tps,
                TotalTokens = TotalTokens,
                LatencyAvailable = LatencyAvailable,
                LatencyMs = LatencyMs,
                CurrentRequestStatus = CurrentRequestStatus
            };
        }
    }
}

