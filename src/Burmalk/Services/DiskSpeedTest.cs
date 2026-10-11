using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Burmalk.Services
{
    public sealed class DiskSpeedResult
    {
        public double WriteMBps;
        public double ReadMBps;
        public long Bytes;
    }

    public static class DiskSpeedTest
    {
        public const long DefaultBytes = 128L * 1024 * 1024;
        private const int BlockSize = 1024 * 1024;

        public static string DefaultFolder(string driveRoot)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.Equals(Path.GetPathRoot(local), driveRoot, StringComparison.OrdinalIgnoreCase) ? local : driveRoot;
        }

        // Only a newly created, uniquely named file is opened. Direct I/O avoids measuring the Windows file cache.
        public static DiskSpeedResult Run(string folder, CancellationToken token, IProgress<string> progress = null, long bytes = DefaultBytes)
        {
            if (bytes <= 0 || bytes % BlockSize != 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            token.ThrowIfCancellationRequested();
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Папка для теста не найдена.");
            var drive = new DriveInfo(Path.GetPathRoot(folder));
            if (!drive.IsReady || drive.AvailableFreeSpace < bytes + 32L * 1024 * 1024)
                throw new IOException("Для теста нужно не менее 160 МиБ свободного места.");
            string testFolder = Path.Combine(folder, ".burmalk-speedtest-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(testFolder, "test.tmp");
            IntPtr buffer = IntPtr.Zero;
            try
            {
                Directory.CreateDirectory(testFolder);
                buffer = VirtualAlloc(IntPtr.Zero, (UIntPtr)BlockSize, 0x3000, 0x04);
                if (buffer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                var data = new byte[BlockSize]; Random.Shared.NextBytes(data); Marshal.Copy(data, 0, buffer, data.Length);
                var result = new DiskSpeedResult { Bytes = bytes };
                progress?.Report("Запись…");
                using (var file = Open(path, 0x40000000, 1, 0xA8000080)) // CREATE_NEW, NO_BUFFERING, WRITE_THROUGH, SEQUENTIAL_SCAN
                {
                    var clock = Stopwatch.StartNew();
                    Transfer(file, buffer, bytes, true, token, progress);
                    if (!FlushFileBuffers(file)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    clock.Stop(); result.WriteMBps = bytes / 1000000.0 / Math.Max(clock.Elapsed.TotalSeconds, .000001);
                }
                token.ThrowIfCancellationRequested(); progress?.Report("Чтение…");
                using (var file = Open(path, 0x80000000, 3, 0x28000080)) // OPEN_EXISTING, NO_BUFFERING, SEQUENTIAL_SCAN
                {
                    var clock = Stopwatch.StartNew();
                    Transfer(file, buffer, bytes, false, token, progress);
                    clock.Stop(); result.ReadMBps = bytes / 1000000.0 / Math.Max(clock.Elapsed.TotalSeconds, .000001);
                }
                token.ThrowIfCancellationRequested(); return result;
            }
            finally
            {
                if (buffer != IntPtr.Zero) VirtualFree(buffer, UIntPtr.Zero, 0x8000);
                // Never recursively delete a caller-supplied directory.
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(testFolder)) Directory.Delete(testFolder, false);
            }
        }

        private static SafeFileHandle Open(string path, uint access, uint creation, uint flags)
        {
            var file = CreateFile(path, access, 0, IntPtr.Zero, creation, flags, IntPtr.Zero);
            if (!file.IsInvalid) return file;
            int error = Marshal.GetLastWin32Error(); file.Dispose(); throw new Win32Exception(error);
        }

        private static void Transfer(SafeFileHandle file, IntPtr buffer, long bytes, bool write, CancellationToken token, IProgress<string> progress)
        {
            var update = Stopwatch.StartNew();
            for (long done = 0; done < bytes; done += BlockSize)
            {
                token.ThrowIfCancellationRequested(); uint count;
                bool ok = write ? WriteFile(file, buffer, BlockSize, out count, IntPtr.Zero) : ReadFile(file, buffer, BlockSize, out count, IntPtr.Zero);
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (count != BlockSize) throw new IOException("Неполное чтение или запись тестового файла.");
                if (update.ElapsedMilliseconds > 200)
                {
                    progress?.Report((write ? "Запись… " : "Чтение… ") + ((done + BlockSize) * 100 / bytes) + "%"); update.Restart();
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, uint size, out uint written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint size, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle file);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);
    }
}
