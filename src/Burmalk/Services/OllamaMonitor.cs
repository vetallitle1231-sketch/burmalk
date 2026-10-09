using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>
    /// Ollama (AI) monitor. Polls the local Ollama HTTP API in the background and keeps a
    /// single-threaded <see cref="AiStatus"/>. Everything is honest: if the daemon is absent
    /// or a field is unavailable the status reports "Disconnected"/zero, never a fabricated
    /// number. Runs on a background timer so the UI thread is never blocked.
    /// </summary>
    public class OllamaMonitor : IDisposable
    {
        private readonly object _gate = new object();
        private AiStatus _status = new AiStatus();
        private string _endpoint = "http://127.0.0.1:11434";
        private readonly System.Threading.Timer _timer;
        private int _polling;
        private bool _disposed;

        public OllamaMonitor()
        {
            // Poll every 3 s; light GETs, no keep-alive pressure on a tiny daemon.
            _timer = new System.Threading.Timer(
                Poll, null,
                0, (int)TimeSpan.FromSeconds(3).TotalMilliseconds);
        }

        /// <summary>Thread-safe snapshot of the latest AI status.</summary>
        public AiStatus Status
        {
            get { lock (_gate) { return _status.Clone(); } }
        }

        /// <summary>Set the base URL (with or without trailing slash). Persists nothing.</summary>
        public void SetEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return;
            string ep = endpoint.Trim();
            if (ep.EndsWith("/")) ep = ep.Substring(0, ep.Length - 1);
            lock (_gate) { _endpoint = ep; }
            Poll(null);
        }

        private void Poll(object state)
        {
            if (_disposed) return;
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;  // one poll at a time
            Task.Run(() =>
            {
                try
                {
                    string ep; lock (_gate) { ep = _endpoint; }
                    var st = new AiStatus();
                    st.Endpoint = ep;

                    using (var http = new HttpClient())
                    {
                        http.BaseAddress = new Uri(ep);
                        http.Timeout = TimeSpan.FromSeconds(2);
                        // 1) Daemon liveness: GET /api/version.
                        try
                        {
                            string verRaw = http.GetStringAsync("/api/version").Result;
                            if (string.IsNullOrWhiteSpace(verRaw))
                            {
                                st.Status = "Disconnected";
                                lock (_gate) { _status = st; }
                                return;
                            }
                            st.Connected = true;
                            st.Status = "Connected";
                        }
                        catch
                        {
                            st.Connected = false;
                            st.Status = "Disconnected";
                            lock (_gate) { _status = st; }
                            return;
                        }

                        // 2) Loaded models (only models currently resident on the GPU).
                        try
                        {
                            var res = http.GetFromJsonAsync<ModelsDto>("/api/ps").Result;
                            if (res != null && res.Models != null && res.Models.Length > 0)
                            {
                                var m = res.Models[0];
                                st.ModelName = (m.Name ?? (m.Model == null ? "Unavailable" : m.Model));
                                st.ModelDetail = (!string.IsNullOrEmpty(m.QuantizationLevel))
                                    ? m.QuantizationLevel
                                    : (m.ParameterSize.HasValue
                                        ? m.ParameterSize.Value.ToString("0") + " params"
                                        : "Unavailable");
                                st.ModelVramGb = m.VramSize > 0 ? m.VramSize / (1024.0 * 1024 * 1024) : 0.0;
                                st.TotalTokens = m.TotalTokens ?? 0L;
                            }
                        }
                        catch { /* keep "Unavailable" for model fields */ }
                    }

                    // Latency / live TPS are not exposed by /api/ps in a stable way. Mark honestly.
                    st.TpsAvailable = false;
                    st.Tps = 0.0;
                    st.LatencyAvailable = false;
                    st.LatencyMs = 0.0;
                    st.CurrentRequestStatus = st.Connected ? "Unavailable" : "Idle";

                    lock (_gate) { _status = st; }
                }
                catch { /* keep prior status; UI shows "Disconnected" or last value */ }
                finally { Interlocked.Exchange(ref _polling, 0); }
            });
        }

        public void Dispose()
        {
            _disposed = true;
            try { _timer?.Dispose(); } catch { }
        }
    }

    // ---- DTOs for Ollama's JSON API (minimal, tolerant) ----
    internal class ModelsDto
    {
        public ModelDto[] Models { get; set; }
    }

    internal class ModelDto
    {
        public string Name { get; set; }
        public string Model { get; set; }
        public string QuantizationLevel { get; set; }
        public long? ParameterSize { get; set; }
        public long TotalSize { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("size_vram")] public long VramSize { get; set; }
        public long? TotalTokens { get; set; }
    }
}


