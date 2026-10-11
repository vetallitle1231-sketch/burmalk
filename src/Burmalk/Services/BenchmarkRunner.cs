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
    }

    public static class BenchmarkRunner
    {
        private static long _sink;

        /// <summary>One bounded CPU worker plus a fixed 128 MiB write/read check.</summary>
        public static Task<BenchmarkReport> RunSystemAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var report = new BenchmarkReport();
                var total = Stopwatch.StartNew();
                const int memoryBytes = 128 * 1024 * 1024;
                byte[] memory = new byte[memoryBytes];
                progress?.Report("Проверяю запись и чтение 128 МиБ памяти…");
                for (int i = 0; i < memory.Length; i += 4096)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    memory[i] = (byte)((i / 4096 * 31 + 17) & 255);
                }
                for (int i = 0; i < memory.Length; i += 4096)
                    if (memory[i] != (byte)((i / 4096 * 31 + 17) & 255)) { report.MemoryPassed = false; return report; }
                report.MemoryPassed = true;
                report.MemoryBytesVerified = memory.Length;

                progress?.Report("Тестирую одно ядро процессора в течение 60 секунд…");
                long value = 1, iterations = 0;
                var cpu = Stopwatch.StartNew();
                while (cpu.Elapsed < TimeSpan.FromSeconds(60))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int i = 0; i < 10000; i++) value = unchecked(value * 1664525 + 1013904223);
                    iterations += 10000;
                }
                Interlocked.Exchange(ref _sink, value);
                report.CpuMillionOpsPerSecond = iterations / cpu.Elapsed.TotalSeconds / 1e6;
                report.Seconds = total.Elapsed.TotalSeconds;
                return report;
            }, cancellationToken);
        }
    }
}
