using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Burmalk.Services
{
    public sealed class InternetSpeedResult
    {
        public double LatencyMs = double.NaN;
        public double JitterMs = double.NaN;
        public double DownloadMbps = double.NaN;
        public double UploadMbps = double.NaN;
        public long DownloadBytes;
        public long UploadBytes;
        public long TrafficBytes;
        public string Server;
        public string Warning;
    }

    public sealed class InternetSpeedProgress
    {
        public string Phase;
        public double Mbps;
    }

    // Public test endpoints. Generated payloads only; no cookies, browser impersonation or user files.
    public static class InternetSpeedTest
    {
        private sealed class TestServer
        {
            public string Name, BaseUrl;
            public bool LibreSpeed;
            public string PingUrl => BaseUrl + (LibreSpeed ? "/empty.php?r=" : "/__down?bytes=0&nonce=") + Guid.NewGuid().ToString("N");
            public string UploadUrl => BaseUrl + (LibreSpeed ? "/empty.php" : "/__up");
            public string DownloadUrl(int bytes) => BaseUrl + (LibreSpeed ? "/garbage.php?ckSize=" + (bytes / 1048576) + "&r=" : "/__down?bytes=" + bytes + "&nonce=") + Guid.NewGuid().ToString("N");
        }
        private static readonly TestServer Cloudflare = new TestServer { Name = "Cloudflare", BaseUrl = "https://speed.cloudflare.com" };
        // Public LibreSpeed endpoint listed in https://librespeed.org/backend-servers/servers.php.
        private static readonly TestServer LibreSpeed = new TestServer { Name = "LibreSpeed · Прага (CESNET)", BaseUrl = "https://speedtest.cesnet.cz/backend", LibreSpeed = true };
        private sealed class Traffic { public long Download, Upload; }
        public static double Mbps(long bytes, double seconds) => bytes * 8.0 / Math.Max(seconds, 0.000001) / 1000000;

        public static async Task<InternetSpeedResult> RunAsync(CancellationToken token,
            IProgress<InternetSpeedProgress> progress = null, HttpClient client = null, string server = "Auto")
        {
            bool ownsClient = client == null;
            client ??= new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(8) }) { Timeout = TimeSpan.FromSeconds(20) };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(75));
            var ct = timeout.Token;
            var traffic = new Traffic();
            var servers = server == "Cloudflare" ? new[] { Cloudflare } : server == "LibreSpeed" ? new[] { LibreSpeed } : new[] { Cloudflare, LibreSpeed };
            InternetSpeedResult partial = null; Exception lastError = null;
            try
            {
                foreach (var target in servers)
                {
                    ct.ThrowIfCancellationRequested();
                    var result = new InternetSpeedResult { Server = target.Name };
                    try
                    {
                        await MeasureServerAsync(client, target, result, traffic, ct, progress).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested(); result.TrafficBytes = traffic.Download + traffic.Upload;
                        return result;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                    {
                        lastError = ex;
                        result.Warning = ExplainFailure(ex);
                        if (double.IsFinite(result.DownloadMbps)) partial = result;
                        if (target != servers[servers.Length - 1])
                            progress?.Report(new InternetSpeedProgress { Phase = "Сервер недоступен. Переключение на LibreSpeed…" });
                    }
                }
                ct.ThrowIfCancellationRequested();
                if (partial != null)
                {
                    partial.TrafficBytes = traffic.Download + traffic.Upload;
                    partial.Warning = "Отдачу измерить не удалось. " + ExplainFailure(lastError);
                    return partial;
                }
                throw new HttpRequestException("Сервисы проверки скорости сейчас недоступны. Попробуйте позже или выберите другой сервер. " + ExplainFailure(lastError), lastError,
                    (lastError as HttpRequestException)?.StatusCode);
            }
            finally { if (ownsClient) client.Dispose(); }
        }

        private static string ExplainFailure(Exception ex)
        {
            if (ex is HttpRequestException http && http.StatusCode is HttpStatusCode.Forbidden)
                return "Сервер отклонил запрос (403). Это не означает отсутствие интернета.";
            if (ex is HttpRequestException limited && limited.StatusCode == HttpStatusCode.TooManyRequests)
                return "Сервис временно ограничил частоту проверок. Повторите позже.";
            return "Не удалось получить корректный ответ сервера.";
        }

        private static HttpRequestMessage Request(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.UserAgent.ParseAdd("Burmalk/1.1.8");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            request.Headers.AcceptEncoding.ParseAdd("identity");
            return request;
        }

        private static async Task MeasureServerAsync(HttpClient client, TestServer server, InternetSpeedResult result, Traffic traffic,
            CancellationToken token, IProgress<InternetSpeedProgress> progress)
        {
            progress?.Report(new InternetSpeedProgress { Phase = server.Name + " · задержка HTTP…" });
            double previous = 0, latency = 0, jitter = 0;
            // Warm up DNS/TLS before collecting four HTTP round trips. Never include server-switch time in rates.
            for (int i = 0; i < 5; i++)
            {
                using var request = Request(HttpMethod.Get, server.PingUrl);
                var clock = Stopwatch.StartNew();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode(); clock.Stop();
                if (i > 0)
                {
                    double value = clock.Elapsed.TotalMilliseconds; latency += value / 4;
                    if (i > 1) jitter += Math.Abs(value - previous) / 3;
                    previous = value;
                }
            }
            result.LatencyMs = latency; result.JitterMs = jitter;
            var down = await MeasureAsync(client, server, false, 96L * 1048576, traffic, token, progress).ConfigureAwait(false);
            result.DownloadBytes = down.bytes; result.DownloadMbps = Mbps(down.bytes, down.seconds);
            var up = await MeasureAsync(client, server, true, 32L * 1048576, traffic, token, progress).ConfigureAwait(false);
            result.UploadBytes = up.bytes; result.UploadMbps = Mbps(up.bytes, up.seconds);
        }

        private static async Task<(long bytes, double seconds)> MeasureAsync(HttpClient client, TestServer server, bool upload, long cap,
            Traffic traffic, CancellationToken token, IProgress<InternetSpeedProgress> progress)
        {
            string phase = server.Name + (upload ? " · отдача…" : " · загрузка…");
            long total = 0; double seconds = 0; int next = 256 * 1024;
            do
            {
                token.ThrowIfCancellationRequested();
                if (!upload && server.LibreSpeed) next = (int)((next + 1048575L) / 1048576 * 1048576);
                int size = (int)Math.Min(next, cap - total);
                var clock = Stopwatch.StartNew(); long transferred = 0;
                using var request = Request(upload ? HttpMethod.Post : HttpMethod.Get, upload ? server.UploadUrl : server.DownloadUrl(size));
                if (upload) request.Content = new TestPayload(size, count => traffic.Upload += count, bytes =>
                    progress?.Report(new InternetSpeedProgress { Phase = phase, Mbps = Mbps(total + bytes, seconds + clock.Elapsed.TotalSeconds) }));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (upload) transferred = size;
                else
                {
                    if (response.Content.Headers.ContentType?.MediaType == "text/html") throw new IOException("Сервер вернул страницу вместо тестовых данных.");
                    using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    var buffer = new byte[64 * 1024]; var update = Stopwatch.StartNew(); int count;
                    while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        transferred += count; traffic.Download += count;
                        if (transferred > size) throw new IOException("Сервер вернул больше данных, чем запрошено.");
                        if (update.ElapsedMilliseconds >= 200)
                        {
                            progress?.Report(new InternetSpeedProgress { Phase = phase, Mbps = Mbps(total + transferred, seconds + clock.Elapsed.TotalSeconds) }); update.Restart();
                        }
                    }
                    if (transferred != size) throw new IOException("Сервер передал неполные тестовые данные.");
                }
                clock.Stop(); total += transferred; seconds += clock.Elapsed.TotalSeconds;
                progress?.Report(new InternetSpeedProgress { Phase = phase, Mbps = Mbps(total, seconds) });
                next = (int)Math.Clamp(transferred / Math.Max(clock.Elapsed.TotalSeconds, .001) * 1.5, 256 * 1024, upload ? 4 * 1048576 : 16 * 1048576);
            } while (seconds < 8 && total < cap);
            return (total, seconds);
        }

        private sealed class TestPayload : HttpContent
        {
            private readonly int _size;
            private readonly Action<int> _count;
            private readonly Action<long> _progress;
            public TestPayload(int size, Action<int> count, Action<long> progress)
            {
                _size = size; _count = count; _progress = progress;
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            }
            protected override bool TryComputeLength(out long length) { length = _size; return true; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => WriteAsync(stream, CancellationToken.None);
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken token) => WriteAsync(stream, token);
            private async Task WriteAsync(Stream stream, CancellationToken token)
            {
                var buffer = new byte[64 * 1024]; Random.Shared.NextBytes(buffer);
                long sent = 0; var update = Stopwatch.StartNew();
                while (sent < _size)
                {
                    int count = (int)Math.Min(buffer.Length, _size - sent);
                    await stream.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); sent += count; _count(count);
                    if (update.ElapsedMilliseconds >= 200) { _progress?.Invoke(sent); update.Restart(); }
                }
            }
        }
    }
}
