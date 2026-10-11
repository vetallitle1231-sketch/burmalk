using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace Burmalk.Services
{
    internal sealed class HardwareTuningService : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<TuningSetting> _settings = new();
        private readonly List<BoardFanSensor> _boardFans = new();
        private readonly Dictionary<IHardware, DateTime> _boardReadAt = new();
        private readonly TuningSession _session = new();
        private readonly NvidiaTuning _nvidia = new();
        private Computer _board;
        private bool _disposed;
        private DateTime _lastPoll;
        private bool _polling;
        public Task Ready { get; }
        public string GpuStatus { get; private set; } = "Проверка драйвера…";
        public string CpuStatus { get; private set; } = "Проверка Windows…";
        public string FanStatus { get; private set; } = "Проверка материнской платы…";
        public string LastProtection { get; private set; } = "";
        public string BoardId { get; private set; } = "";
        public HardwareTuningService() { Ready = Task.Run(Initialize); }
        private void Initialize()
        {
            lock (_gate)
            {
                if (_disposed) return;
                try { GpuStatus = _nvidia.Probe(_settings); } catch (Exception ex) { GpuStatus = ex.Message; }
                try { CpuStatus = CpuBoost.Probe(_settings); } catch (Exception ex) { CpuStatus = ex.Message; }
                if (!CpuSensorAccess.IsAdministrator) { FanStatus = "CPU_FAN: для доступа к плате запусти Burmalk от администратора. Поддержка зависит от платы."; return; }
                try
                {
                    _board = new Computer { IsMotherboardEnabled = true }; _board.Open();
                    try
                    {
                        using var search = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                        using var rows = search.Get();
                        foreach (System.Management.ManagementObject row in rows) { using (row) BoardId += row["Manufacturer"] + "|" + row["Product"] + ";"; }
                    }
                    catch (Exception ex) { ErrorLog.Write("Fan board identity: " + ex.Message); }
                    foreach (var root in _board.Hardware) ScanBoard(root);
                    int count = _settings.Count(s => s.Group == "CPU FAN");
                    FanStatus = count == 0 ? "Плата не предоставила управление вентиляторами. Используй BIOS / ASUS Fan Xpert." : "PWM (%) и обороты (RPM) — разные каналы. Fan # не определяет разъём. Если CPU_FAN не подписан платой, назначь контроллер и датчик по показаниям BIOS и сохрани выбор.";
                }
                catch (Exception ex) { FanStatus = "Управление вентиляторами платы недоступно: " + ex.Message; }
            }
        }
        private void ScanBoard(IHardware hardware)
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Fan))
                _boardFans.Add(new BoardFanSensor
                {
                    Id = sensor.Identifier.ToString(), Name = CpuFanBinding.ChannelName(sensor.Name, true) + " · " + hardware.Name,
                    NativeName = sensor.Name, HardwareId = hardware.Identifier.ToString(),
                    Read = () => { UpdateBoard(hardware); return sensor.Value.HasValue && float.IsFinite(sensor.Value.Value) && sensor.Value.Value >= 0 ? sensor.Value.Value : double.NaN; }
                });
            foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null))
            {
                var control = sensor.Control;
                string label = CpuFanBinding.ChannelName(sensor.Name, false) + " · " + hardware.Name;
                _settings.Add(new TuningSetting
                {
                    Id = sensor.Identifier.ToString(), Group = "CPU FAN", Name = label, Unit = "%", NativeName = sensor.Name, HardwareId = hardware.Identifier.ToString(),
                    Minimum = Math.Max(40, control.MinSoftwareValue), Maximum = Math.Min(100, control.MaxSoftwareValue),
                    Read = () => { UpdateBoard(hardware); return sensor.Value ?? (control.ControlMode == ControlMode.Software ? control.SoftwareValue : double.NaN); },
                    Write = v => { control.SetSoftware((float)v); _boardReadAt.Remove(hardware); }, Automatic = () => { control.SetDefault(); _boardReadAt.Remove(hardware); },
                    CaptureRestore = () => { var mode = control.ControlMode; float value = control.SoftwareValue; return () => { if (mode == ControlMode.Software) control.SetSoftware(value); else control.SetDefault(); _boardReadAt.Remove(hardware); }; }
                });
            }
            foreach (var child in hardware.SubHardware) ScanBoard(child);
        }
        private void UpdateBoard(IHardware hardware)
        {
            var now = DateTime.UtcNow;
            if (_boardReadAt.TryGetValue(hardware, out var last) && now - last < TimeSpan.FromMilliseconds(500)) return;
            hardware.Update(); _boardReadAt[hardware] = now;
        }
        public TuningSetting[] Settings(string group) { lock (_gate) return _settings.Where(s => s.Group == group && s.Minimum <= s.Maximum).ToArray(); }
        public BoardFanSensor[] BoardFans() { lock (_gate) return _boardFans.ToArray(); }
        public double Read(BoardFanSensor sensor) { lock (_gate) { if (_disposed) return double.NaN; return sensor.Read(); } }
        public double Read(TuningSetting setting) { lock (_gate) { if (_disposed) return double.NaN; return setting.Read(); } }
        public void Apply(Dictionary<TuningSetting, double> values)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(HardwareTuningService));
                var sample = App.Monitor.LastSample;
                foreach (var group in values.Keys.Select(s => s.Group).Distinct())
                {
                    if (group.StartsWith("GPU", StringComparison.Ordinal) && !CpuSensorAccess.IsAdministrator)
                        throw new InvalidOperationException("Для изменения настроек NVIDIA нужны права администратора. Нажми «Разрешить доступ к оборудованию».");
                    double temp = group.StartsWith("GPU", StringComparison.Ordinal) ? sample.GpuTempC : sample.CpuTempC;
                    double limit = group.StartsWith("GPU", StringComparison.Ordinal) ? 85 : 90;
                    if (!double.IsFinite(temp)) throw new InvalidOperationException("Температура " + group + " недоступна. Включи датчики перед применением.");
                    if (temp >= limit) throw new InvalidOperationException("Температура слишком высокая для настройки. Дождись охлаждения.");
                }
                _session.Apply(values);
            }
        }
        public void Restore(string group = null) { lock (_gate) _session.Restore(group); }
        public void Automatic(TuningSetting setting)
        {
            lock (_gate)
            {
                if (!CpuSensorAccess.IsAdministrator) throw new InvalidOperationException("Для управления вентиляторами нужны права администратора.");
                _session.Automatic(setting);
            }
        }
        public void Protect(double cpuTemperature, double gpuTemperature)
        {
            // Avoid motherboard I/O and native calls on the UI thread.
            if (!Ready.IsCompleted) return;
            lock (_gate)
            {
                if (_disposed || _polling || DateTime.UtcNow - _lastPoll < TimeSpan.FromSeconds(3) || !_session.HasChanges) return;
                _polling = true; _lastPoll = DateTime.UtcNow;
            }
            _ = Task.Run(() =>
            {
                lock (_gate)
                {
                    try
                    {
                        if (_disposed) return;
                        string result = _session.Protect(cpuTemperature, gpuTemperature);
                        if (result.Length > 0) LastProtection = result;
                    }
                    catch (Exception ex) { LastProtection = "Не удалось восстановить настройки: " + ex.Message; ErrorLog.Write(LastProtection); }
                    finally { _polling = false; }
                }
            });
        }
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                try { _session.Restore(); } catch (Exception ex) { ErrorLog.Write("Tuning reset on exit: " + ex); }
                _board?.Close(); _nvidia.Dispose();
            }
        }
    }
}
