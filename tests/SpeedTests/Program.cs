using System.IO;
using System.Net;
using System.Net.Http;
using Burmalk.Services;

int checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
Check(InternetSpeedTest.Mbps(1_000_000, 1) == 8, "Internet rate uses bits, decimal megabits and elapsed seconds");
using (var handler = new FakeServer())
using (var http = new HttpClient(handler))
{
    var result = await InternetSpeedTest.RunAsync(CancellationToken.None, client: http);
    Check(result.DownloadBytes == 96L * 1024 * 1024 && result.UploadBytes == 32L * 1024 * 1024, "Internet test obeys download/upload traffic caps");
    Check(handler.Uploaded == result.UploadBytes && handler.Downloaded == result.DownloadBytes, "Internet test measures actual payload sizes in both directions");
    Check(result.DownloadMbps > 0 && result.UploadMbps > 0 && result.LatencyMs > 0 && result.JitterMs >= 0, "Internet test calculates throughput and HTTP timing");
}
using (var handler = new FakeServer { Truncate = true })
using (var http = new HttpClient(handler))
{
    try { await InternetSpeedTest.RunAsync(CancellationToken.None, client: http); throw new Exception("Truncated response accepted"); }
    catch (HttpRequestException) { Check(true, "Incomplete download is rejected instead of reporting false throughput"); }
}
using (var handler = new FakeServer { Error = true })
using (var http = new HttpClient(handler))
{
    try { await InternetSpeedTest.RunAsync(CancellationToken.None, client: http); throw new Exception("Server error accepted"); }
    catch (HttpRequestException) { Check(true, "Server errors are reported"); }
}
using (var cancel = new CancellationTokenSource())
using (var http = new HttpClient(new FakeServer()))
{
    cancel.Cancel();
    try { await InternetSpeedTest.RunAsync(cancel.Token, client: http); throw new Exception("Cancelled network test ran"); }
    catch (OperationCanceledException) { Check(true, "Network test supports cancellation"); }
}
using (var handler = new FakeServer { CloudflareForbidden = true })
using (var http = new HttpClient(handler))
{
    var result = await InternetSpeedTest.RunAsync(CancellationToken.None, client: http);
    Check(result.Server.StartsWith("LibreSpeed") && result.DownloadMbps > 0 && result.UploadMbps > 0, "A Cloudflare 403 automatically switches to the independent LibreSpeed server");
    Check(result.TrafficBytes == result.DownloadBytes + result.UploadBytes, "Fallback traffic accounting matches actual generated payloads");
}
using (var handler = new FakeServer { CloudflareFailAfterDownload = true })
using (var http = new HttpClient(handler))
{
    var result = await InternetSpeedTest.RunAsync(CancellationToken.None, client: http);
    Check(result.Server.StartsWith("LibreSpeed") && result.DownloadBytes == 96L * 1048576, "A mid-download rejection restarts measurement without mixing servers");
    Check(result.TrafficBytes > result.DownloadBytes + result.UploadBytes && result.TrafficBytes <= 256L * 1048576, "Traffic includes failed primary-server transfers and remains bounded");
}
using (var handler = new FakeServer { UploadForbidden = true })
using (var http = new HttpClient(handler))
{
    var result = await InternetSpeedTest.RunAsync(CancellationToken.None, client: http);
    Check(result.DownloadMbps > 0 && double.IsNaN(result.UploadMbps) && result.Warning.Contains("403"), "Blocked uploads preserve measured download and explicitly mark upload unavailable");
}
using (var handler = new FakeServer { CloudflareForbidden = true })
using (var http = new HttpClient(handler))
{
    try { await InternetSpeedTest.RunAsync(CancellationToken.None, client: http, server: "Cloudflare"); throw new Exception("Forbidden server was accepted"); }
    catch (HttpRequestException ex) { Check(handler.Requests == 1 && ex.Message.Contains("Попробуйте"), "Manual server selection is respected and failures have an actionable message"); }
}
using (var handler = new FakeServer { CloudflareForbidden = true })
using (var http = new HttpClient(handler))
using (var cancel = new CancellationTokenSource())
{
    var progress = new ImmediateNetworkProgress(p => { if (p.Phase.Contains("Переключение")) cancel.Cancel(); });
    try { await InternetSpeedTest.RunAsync(cancel.Token, progress, http); throw new Exception("Cancelled fallback ran"); }
    catch (OperationCanceledException) { Check(handler.Requests == 1, "Cancellation prevents fallback requests"); }
}
if (!args.Contains("--no-disk"))
{
string fixture = Path.Combine(Path.GetTempPath(), "BurmalkFeatureChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture); string sentinel = Path.Combine(fixture, "keep.txt"); File.WriteAllText(sentinel, "preserve me");
try
{
    var result = await Task.Run(() => DiskSpeedTest.Run(fixture, CancellationToken.None, bytes: 8L * 1024 * 1024));
    Check(result.ReadMBps > 0 && result.WriteMBps > 0 && result.Bytes == 8L * 1024 * 1024, "Real disk direct I/O read/write test returns measured rates");
    Check(Directory.GetFileSystemEntries(fixture).Length == 1 && File.ReadAllText(sentinel) == "preserve me", "Successful disk test cleans up only its own file and folder");
    using (var cancel = new CancellationTokenSource())
    {
        var progress = new ImmediateProgress(text => { if (text.StartsWith("Чтение")) cancel.Cancel(); });
        try { await Task.Run(() => DiskSpeedTest.Run(fixture, cancel.Token, progress, 8L * 1024 * 1024)); throw new Exception("Cancelled disk test ran"); }
        catch (OperationCanceledException) { Check(true, "Disk test supports cancellation between write and read"); }
    }
    Check(Directory.GetFileSystemEntries(fixture).Length == 1 && File.ReadAllText(sentinel) == "preserve me", "Cancelled disk test removes temporary data and preserves existing files");
    try { DiskSpeedTest.Run(Path.Combine(fixture, "missing"), CancellationToken.None); throw new Exception("Missing folder accepted"); }
    catch (DirectoryNotFoundException) { Check(true, "Missing test folder is rejected without creating user data"); }
    try { DiskSpeedTest.Run(fixture, CancellationToken.None, bytes: 123); throw new Exception("Unaligned size accepted"); }
    catch (ArgumentOutOfRangeException) { Check(true, "Direct I/O rejects unaligned sizes"); }
}
finally { File.Delete(sentinel); Directory.Delete(fixture); }
}
// Minimal real-endpoint compatibility probe only: no speed/load test during verification.
if (args.Contains("--live"))
using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
{
    using var down = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=256&nonce=" + Guid.NewGuid().ToString("N"));
    down.EnsureSuccessStatusCode();
    Check((await down.Content.ReadAsByteArrayAsync()).Length == 256, "Cloudflare live download endpoint returns the requested test payload");
    using var up = await http.PostAsync("https://speed.cloudflare.com/__up", new ByteArrayContent(new byte[256]));
    up.EnsureSuccessStatusCode(); Check(true, "Cloudflare live upload endpoint accepts generated test data");
}
if (args.Contains("--fallback-full")) { var r = await InternetSpeedTest.RunAsync(CancellationToken.None, server: "LibreSpeed"); Console.WriteLine($"Live fallback: down {r.DownloadMbps:0.0}, up {r.UploadMbps:0.0} Mbps, warning {r.Warning}"); if (!double.IsFinite(r.DownloadMbps) || !double.IsFinite(r.UploadMbps)) throw new Exception("Live fallback did not complete"); }
if (args.Contains("--full")) { var r = await InternetSpeedTest.RunAsync(CancellationToken.None); Console.WriteLine($"Live result: down {r.DownloadMbps:0.0}, up {r.UploadMbps:0.0} Mbps"); }
Console.WriteLine($"Completed {checks} feature checks.");

sealed class ImmediateProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
sealed class ImmediateNetworkProgress(Action<InternetSpeedProgress> action) : IProgress<InternetSpeedProgress> { public void Report(InternetSpeedProgress value) => action(value); }
sealed class FakeServer : HttpMessageHandler
{
    public bool Truncate, Error, CloudflareForbidden, CloudflareFailAfterDownload, UploadForbidden;
    public int Requests;
    public long Downloaded, Uploaded;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        await Task.Delay(2, token); Requests++;
        if (request.Headers.UserAgent.ToString() != "Burmalk/" + typeof(InternetSpeedTest).Assembly.GetName().Version?.ToString(3)) throw new Exception("Missing honest application User-Agent");
        bool primary = request.RequestUri!.Host == "speed.cloudflare.com";
        if ((primary && CloudflareForbidden) || (primary && CloudflareFailAfterDownload && Downloaded > 0) || (UploadForbidden && request.Method == HttpMethod.Post)) return new HttpResponseMessage(HttpStatusCode.Forbidden);
        if (Error) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (request.Method == HttpMethod.Post)
        {
            Uploaded += (await request.Content!.ReadAsByteArrayAsync(token)).Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
        string query = request.RequestUri!.Query;
        int start = query.IndexOf(primary ? "bytes=" : "ckSize=", StringComparison.Ordinal);
        if (start < 0) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        start += primary ? 6 : 7;
        int end = query.IndexOf('&', start);
        int size = int.Parse(query[start..(end < 0 ? query.Length : end)]);
        if (!primary) size *= 1048576;
        if (Truncate && size > 0) size--;
        Downloaded += size;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[size]) };
    }
}
