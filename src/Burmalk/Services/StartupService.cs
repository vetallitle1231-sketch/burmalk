using System;
using Microsoft.Win32;

namespace Burmalk.Services
{
    internal static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Burmalk";

        public static void Apply(bool enabled, bool experimental = false)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                {
                    string executable = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Не удалось определить путь к Burmalk.exe.");
                    key.SetValue(ValueName, "\"" + executable + "\"" + (experimental ? " --profile-experimental" : ""), RegistryValueKind.String);
                }
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
