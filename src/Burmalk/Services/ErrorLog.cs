using System;
using System.IO;

namespace Burmalk.Services
{
    /// <summary>Minimal error log under %AppData%\Burmalk\error.log (no crash, just append).</summary>
    public static class ErrorLog
    {
        private static readonly object _gate = new object();
        private static string _dir;

        public static string LogDir
        {
            get
            {
                if (_dir == null)
                {
                    try
                    {
                        _dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Burmalk");
                        Directory.CreateDirectory(_dir);
                    }
                    catch { _dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
                }
                return _dir;
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (_gate)
                {
                    File.AppendAllText(Path.Combine(LogDir, "error.log"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}

