using Burmalk.Models;
using Burmalk.Services;

void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
PerformanceRecord Full(double ratio = 1) => new() { MethodVersion = RankMethod.Version, CpuCompleted = true, GpuCompleted = true,
    RamCompleted = true, MemoryPassed = true, CpuSeconds = 60, GpuSeconds = 120, MemoryBytesVerified = 128L*1024*1024, RenderTier = 2, CpuWorkers = 16, GpuEngine = "gl-raymarch-v2-1280x720-96",
    CpuMillionOps = 100 * ratio, RamMegabytesPerSecond = 1000 * ratio, GpuFps = 60 * ratio };
PcRankResult Rate(PerformanceRecord r) => RankMethod.Calculate(r, 100, 1000, 60);
Check(!RankMethod.Evaluate(null).Confirmed, "Unrun benchmark does not invent a rank");
foreach (var (ratio, expected) in new[] { (.25,"BRONZE"), (.65,"SILVER"), (1.0,"GOLD"), (1.4,"DIAMOND"), (1.8,"LEGENDARY"), (.98,"GOLD"), (1.02,"GOLD") })
    Check(Rate(Full(ratio)).Name == expected, "Geometric throughput center " + ratio);
foreach (var (boundary, below, above) in new[] { (.5,"BRONZE","SILVER"), (.8,"SILVER","GOLD"), (1.2,"GOLD","DIAMOND"), (1.6,"DIAMOND","LEGENDARY") })
{
    Check(Rate(Full(boundary - 1e-8)).Name == below, "Immediately below geometric boundary");
    Check(Rate(Full(boundary + 1e-8)).Name == above, "Immediately above geometric boundary");
}
var cpu = Full(); cpu.CpuMillionOps *= 2; Check(Math.Abs(Rate(cpu).Total - 1350) < 1e-8, "CPU contributes exactly 35 percent");
var gpu = Full(); gpu.GpuFps *= 2; Check(Math.Abs(Rate(gpu).Total - 1500) < 1e-8, "GPU contributes exactly 50 percent");
var ram = Full(); ram.RamMegabytesPerSecond *= 2; Check(Math.Abs(Rate(ram).Total - 1150) < 1e-8, "RAM contributes exactly 15 percent");
foreach (Action<PerformanceRecord> invalidate in new Action<PerformanceRecord>[] { r=>r.GpuCompleted=false, r=>r.CpuCompleted=false, r=>r.RamCompleted=false,
    r=>r.MemoryPassed=false, r=>r.GpuSeconds=118, r=>r.CpuSeconds=58, r=>r.RenderTier=0, r=>r.CpuWorkers=0, r=>r.GpuEngine="WPF", r=>r.MethodVersion="older",
    r=>r.CpuMillionOps=double.NaN, r=>r.GpuFps=double.PositiveInfinity, r=>r.CpuSeconds=double.PositiveInfinity, r=>r.GpuSeconds=double.NaN, r=>r.RamMegabytesPerSecond=-1, r=>r.MemoryBytesVerified=4096 })
{ var record = Full(); invalidate(record); Check(!Rate(record).Confirmed, "Incomplete, stale or invalid result is rejected"); }
Check(!RankMethod.Calculate(Full(), 0, 1000, 60).Confirmed, "Uncalibrated reference cannot assign a rank");
var renamed = Full(); renamed.GpuModel="NVIDIA FASTEST TITAN RTX LEGENDARY"; renamed.CpuModel="128 cores";
Check(Rate(renamed).Total == Rate(Full()).Total, "Hardware names never increase the score");
var mismatch = new HardwareSnapshot(); mismatch.Cpu.Model="new CPU";
Check(!RankMethod.MatchesHardware(renamed, mismatch, "new GPU"), "Changed hardware invalidates previous confirmation");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await BenchmarkRunner.RunSystemAsync(null,cancelled.Token); Check(false,"Cancellation must throw"); } catch(OperationCanceledException) { Check(true,"Cancelled benchmark exits before creating load"); }
