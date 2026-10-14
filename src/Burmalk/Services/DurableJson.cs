using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Burmalk.Services;

// A same-volume replacement never exposes a partially written JSON file.
internal static class DurableJson
{
    internal static IDisposable Lock(string path)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        var mutex = new Mutex(false, @"Local\Burmalk-Data-" + name);
        try
        {
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new IOException("Файл настроек занят другой копией Burmalk."); }
            catch (AbandonedMutexException) { }
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }
    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
    internal static void Write(string path, string json)
    {
        using var parsed = JsonDocument.Parse(json);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Flush(temporary, json);
            bool valid = false;
            try { using var old = JsonDocument.Parse(File.ReadAllText(path)); valid = old.RootElement.ValueKind != JsonValueKind.Null; } catch { }
            if (valid) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path, true);
            // The newest committed state and the previous state are both recoverable.
            string copy = path + ".lastgood." + Guid.NewGuid().ToString("N") + ".tmp";
            try { Flush(copy, json); File.Move(copy, path + ".lastgood", true); }
            finally { if (File.Exists(copy)) File.Delete(copy); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Flush(string path, string text)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(text); file.Write(bytes); file.Flush(true);
    }
}
