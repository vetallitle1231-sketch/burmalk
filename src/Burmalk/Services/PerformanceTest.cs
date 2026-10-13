using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Burmalk.Models;
using Burmalk.Views;

namespace Burmalk.Services
{
    internal static class PerformanceTest
    {
        public static bool Busy { get; private set; }
        public static bool TryEnter() { if (Busy) return false; Busy = true; return true; }
        public static void Exit() => Busy = false;
        public static async Task<PerformanceRecord> RunAsync(Window owner, IProgress<string> progress, CancellationToken token)
        {
            if (!TryEnter()) throw new InvalidOperationException("Другой бенчмарк уже работает.");
            var h = App.Monitor.Hardware.Snapshot;
            var r = new PerformanceRecord { MethodVersion = RankMethod.Version, TestedUtc = DateTime.UtcNow,
                CpuModel = h.Cpu.Model, GpuModel = PassportService.GpuName(), RamGb = h.Ram.TotalGb, Status = "Подготовка" };
            void Peaks(Sample s)
            {
                void Max(ref double peak, double value) { if (double.IsFinite(value) && (!double.IsFinite(peak) || value > peak)) peak = value; }
                Max(ref r.MaxCpuTemp, s.CpuTempC); Max(ref r.MaxGpuTemp, s.GpuTempC); Max(ref r.MaxCpuMhz, s.CpuGhz * 1000); Max(ref r.MaxGpuMhz, s.GpuClockMhz);
            }
            App.Monitor.SampleUpdated += Peaks;
            try
            {
                if (!App.Monitor.Hardware.ProbeCompleted) throw new InvalidOperationException("Сбор характеристик ещё не завершён. Подожди несколько секунд.");
                token.ThrowIfCancellationRequested();
                bool Safe() {
                    var sample = App.Monitor.LastSample;
                    return DateTime.UtcNow.Ticks - sample.TimestampUtc < TimeSpan.TicksPerSecond * 15 &&
                        (!double.IsFinite(sample.CpuTempC) || sample.CpuTempC < 95) &&
                        (!double.IsFinite(sample.GpuTempC) || sample.GpuTempC < 90) && !token.IsCancellationRequested;
                }
                progress?.Report("1 / 3 · GPU OpenGL, 2 минуты · фиксированная сцена 1280 × 720");
                var gpu = await NativeGpuWindow.RunAsync(owner, token, Safe, progress);
                r.GpuFps = gpu.Fps; r.GpuSeconds = gpu.Seconds; r.GpuCompleted = gpu.Completed;
                r.RenderTier = 2; r.GpuEngine = NativeGpuBenchmark.Engine; r.GpuRenderer = gpu.Renderer;
                if (!NativeGpuBenchmark.MatchesRenderer(gpu.Renderer, r.GpuModel)) throw new InvalidOperationException("Тест запущен на другой GPU: " + gpu.Renderer + ". Выбери нужную GPU для Burmalk в настройках графики Windows.");
                if (!gpu.Completed) throw new InvalidOperationException("GPU-тест не завершён");
                progress?.Report("2 / 3 · CPU, 60 секунд. 3 / 3 · RAM, чтение и запись 128 МиБ.");
                var cpu = await BenchmarkRunner.RunSystemAsync(progress, token, () =>
                {
                    var sample = App.Monitor.LastSample;
                    return DateTime.UtcNow.Ticks - sample.TimestampUtc < TimeSpan.TicksPerSecond * 15 &&
                        (!double.IsFinite(sample.CpuTempC) || sample.CpuTempC < 95) &&
                        (!double.IsFinite(sample.GpuTempC) || sample.GpuTempC < 90) && !token.IsCancellationRequested;
                });
                r.CpuWorkers = cpu.CpuWorkers; r.CpuMillionOps = cpu.CpuMillionOpsPerSecond; r.CpuSeconds = cpu.CpuSeconds;
                r.RamMegabytesPerSecond = cpu.RamMegabytesPerSecond; r.MemoryBytesVerified = cpu.MemoryBytesVerified;
                r.MemoryPassed = cpu.MemoryPassed; r.CpuCompleted = cpu.CpuSeconds >= 59;
                r.RamCompleted = cpu.MemoryPassed && cpu.RamMegabytesPerSecond > 0;
                r.Status = RankMethod.Complete(r) ? "Завершено" : "Память не прошла проверку";
                return r;
            }
            catch (OperationCanceledException) { r.Status = "Остановлено пользователем"; return r; }
            catch (Exception ex) { r.Status = ex.Message; ErrorLog.Write("PC RANK: " + ex); return r; }
            finally { App.Monitor.SampleUpdated -= Peaks; Exit(); }
        }
    }
}
