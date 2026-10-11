using System;
using System.Collections.Generic;
using System.Linq;

namespace Burmalk.Services
{
    internal sealed class TuningSetting
    {
        public string Id, Group, Name, Unit;
        public string NativeName, HardwareId;
        public double Minimum, Maximum;
        public Func<double> Read;
        public Action<double> Write;
        public Func<Action> CaptureRestore;
        public Action Automatic;
        public override string ToString() => Name;
    }

    // All validation precedes writes. Keep the user's starting values, including
    // settings applied by another utility; never assume factory settings were active.
    internal sealed class TuningSession
    {
        private readonly Dictionary<TuningSetting, Action> _originals = new();
        public bool HasChanges => _originals.Count != 0;
        public void Apply(IReadOnlyDictionary<TuningSetting, double> values)
        {
            foreach (var pair in values)
                if (!double.IsFinite(pair.Value) || pair.Value < pair.Key.Minimum || pair.Value > pair.Key.Maximum)
                    throw new ArgumentOutOfRangeException(pair.Key.Name, "Значение вне диапазона драйвера.");
            var rollback = new List<(TuningSetting setting, Action restore, bool wasOwned)>();
            try
            {
                foreach (var pair in values)
                {
                    var setting = pair.Key;
                    double current = setting.Read();
                    if (Math.Abs(current - pair.Value) < 0.01 && setting.Automatic == null) continue;
                    Action restore = setting.CaptureRestore != null ? setting.CaptureRestore() : () =>
                    {
                        setting.Write(current);
                        double restored = setting.Read();
                        if (!double.IsFinite(restored) || Math.Abs(restored - current) > 0.51) throw new InvalidOperationException("Драйвер не подтвердил восстановление.");
                    };
                    bool owned = _originals.ContainsKey(setting);
                    // Track before writing, since drivers may fail after partially applying.
                    if (!owned) _originals.Add(setting, restore);
                    rollback.Add((setting, restore, owned));
                    setting.Write(pair.Value);
                    if (setting.Automatic == null)
                    {
                        double confirmed = setting.Read();
                        if (!double.IsFinite(confirmed) || Math.Abs(confirmed - pair.Value) > 0.51)
                            throw new InvalidOperationException("Драйвер не подтвердил значение: " + setting.Name);
                    }
                }
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (var item in rollback.AsEnumerable().Reverse())
                {
                    try { item.restore(); if (!item.wasOwned) _originals.Remove(item.setting); }
                    catch (Exception ex) { failures.Add(item.setting.Name + ": " + ex.Message); }
                }
                throw new InvalidOperationException(error.Message + (failures.Count == 0 ? "\nИзменения отменены." : "\nНе удалось восстановить: " + string.Join("; ", failures)), error);
            }
        }
        public void Restore(string group = null)
        {
            var failures = new List<string>();
            foreach (var pair in _originals.Where(p => group == null || p.Key.Group == group).Reverse().ToArray())
            {
                try { pair.Value(); _originals.Remove(pair.Key); }
                catch (Exception ex) { failures.Add(pair.Key.Name + ": " + ex.Message); }
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        }
        public void Automatic(TuningSetting setting)
        {
            if (setting.Automatic == null) throw new InvalidOperationException("Автоматический режим недоступен.");
            if (!_originals.ContainsKey(setting)) _originals.Add(setting, setting.CaptureRestore());
            setting.Automatic();
        }
        public string Protect(double cpuTemperature, double gpuTemperature)
        {
            var messages = new List<string>();
            foreach (string group in new[] { "CPU", "CPU FAN", "GPU", "GPU FAN" })
            {
                double temperature = group.StartsWith("GPU", StringComparison.Ordinal) ? gpuTemperature : cpuTemperature;
                double limit = group.StartsWith("GPU", StringComparison.Ordinal) ? 85 : 90;
                if ((double.IsFinite(temperature) && temperature < limit) || !_originals.Keys.Any(s => s.Group == group)) continue;
                try { Restore(group); messages.Add(group + ": настройки восстановлены из-за высокой или недоступной температуры."); }
                catch (Exception ex) { messages.Add(group + ": ошибка восстановления: " + ex.Message); }
            }
            return string.Join("\n", messages);
        }
    }
}
