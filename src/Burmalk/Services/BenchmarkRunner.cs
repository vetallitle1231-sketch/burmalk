using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Burmalk.Services
{
    public sealed class BenchmarkReport
    {
        public double CpuMillionOpsPerSecond;
        public double Seconds;
        public long MemoryBytesVerified;
        public bool MemoryPassed;
        public double RamMegabytesPerSecond;
        public double CpuSeconds;
    }

    public static class BenchmarkRunner
    {
        private static long _sink;

        /// <summary>One bounded CPU worker plus a fixed 128 MiB write/read check.</summary>
        public static Task<BenchmarkReport> RunSystemAsync(IProgress<string> progress, CancellationToken cancellationToken, Func<bool> safeToContinue = null)
        {
            return Task.Run(() =>
            {
                var report = new BenchmarkReport();
                var total = Stopwatch.StartNew();
                const int memoryBytes = 128 * 1024 * 1024;
                ulong[] memory = new ulong[memoryBytes / sizeof(ulong)];
                progress?.Report("Проверяю запись и чтение 128 МиБ памяти…");
                var ram = Stopwatch.StartNew();
                for (int pass = 0; pass < 4; pass++)
                {
                    for (int i = 0; i < memory.Length; i++)
                    {
                        if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
                        memory[i] = unchecked((ulong)i * 6364136223846793005UL + (ulong)pass);
                    }
                    for (int i = 0; i < memory.Length; i++)
                    {
                        if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
                        if (memory[i] != unchecked((ulong)i * 6364136223846793005UL + (ulong)pass))
                        { report.MemoryPassed = false; return report; }
                    }
                }
                ram.Stop();
                report.RamMegabytesPerSecond = memoryBytes * 8.0 / ram.Elapsed.TotalSeconds / 1e6;
                report.MemoryPassed = true;
                report.MemoryBytesVerified = memoryBytes;

                progress?.Report("Тестирую одно ядро процессора в течение 60 секунд…");
                long value = 1, iterations = 0;
                var cpu = Stopwatch.StartNew();
                while (cpu.Elapsed < TimeSpan.FromSeconds(60))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (safeToContinue != null && !safeToContinue()) throw new InvalidOperationException("Тест остановлен: перегрев, устаревшие датчики или окно закрыто.");
                    for (int i = 0; i < 10000; i++) value = unchecked(value * 1664525 + 1013904223);
                    iterations += 10000;
                }
                Interlocked.Exchange(ref _sink, value);
                report.CpuMillionOpsPerSecond = iterations / cpu.Elapsed.TotalSeconds / 1e6;
                report.CpuSeconds = cpu.Elapsed.TotalSeconds;
                report.Seconds = total.Elapsed.TotalSeconds;
                return report;
            }, cancellationToken);
        }
    }
}
