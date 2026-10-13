using Burmalk.Services;

// Explicit opt-in hardware test. Ordinary runs verify cancellation and renderer selection only.
void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
Check(NativeGpuBenchmark.MatchesRenderer("NVIDIA GeForce RTX 5070/PCIe/SSE2", "NVIDIA GeForce RTX 5070"), "Exact tested GPU accepts driver suffix");
Check(!NativeGpuBenchmark.MatchesRenderer("Intel UHD Graphics", "NVIDIA GeForce RTX 5070"), "Another GPU cannot inherit the monitored GPU score");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await NativeGpuBenchmark.RunAsync(null, cancel.Token, null); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "Cancelled run creates no GPU load"); }
}
if (!args.Contains("--hardware")) return;
byte[] pixels = null;
var progress = new InlineProgress<GpuFrame>(frame => pixels = frame.Pixels);
var result = await NativeGpuBenchmark.RunAsync(progress, default, () => true, 3);
Check(result.Completed && result.Seconds >= 3 && result.Fps > 0, "Hardware renderer completes fixed offscreen scene");
Check(pixels?.Length == 1280 * 720 * 4, "Framebuffer readback has fixed dimensions");
var colors = new HashSet<int>();
for (int i = 0; i < pixels.Length; i += 4 * 53) colors.Add(pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16);
Check(colors.Count > 100, "Framebuffer contains shaded geometry rather than a blank image");
Directory.CreateDirectory(".build/native-gpu-proof"); File.WriteAllBytes(".build/native-gpu-proof/frame.bgra", pixels);
Console.WriteLine(result.Renderer + " / " + result.Fps.ToString("0.0") + " FPS");
sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
