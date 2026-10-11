using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Burmalk.Services
{
    internal sealed class NvidiaTuning : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Simple();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handle(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUInt(IntPtr device, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetInt(IntPtr device, out int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetUInt(IntPtr device, uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetInt(IntPtr device, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UIntRange(IntPtr device, out uint min, out uint max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntRange(IntPtr device, out int min, out int max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FanGet(IntPtr device, uint fan, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FanSet(IntPtr device, uint fan, uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NameGet(IntPtr device, StringBuilder name, uint length);
        [StructLayout(LayoutKind.Sequential)] private struct ClockOffset { public uint Version, Type, Pstate; public int Offset, Minimum, Maximum; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClockQuery(IntPtr device, ref ClockOffset value);
        private IntPtr _library;
        private bool _initialized;
        private T Export<T>(string name) where T : Delegate => NativeLibrary.TryGetExport(_library, name, out var ptr) ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;
        private static void Check(int code)
        {
            if (code == 0) return;
            throw new InvalidOperationException(code switch
            {
                3 => "Драйвер NVIDIA не поддерживает эту настройку.",
                4 => "Драйвер отказал в доступе. Запусти Burmalk от администратора.",
                2 => "Драйвер отклонил значение.",
                15 => "Видеокарта недоступна; требуется восстановление драйвера.",
                _ => "Ошибка драйвера NVIDIA: " + code
            });
        }
        public string Probe(List<TuningSetting> settings)
        {
            foreach (string path in new[] { Path.Combine(Environment.SystemDirectory, "nvml.dll"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll") })
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out _library)) break;
            if (_library == IntPtr.Zero) return "Драйвер NVIDIA NVML не найден. Управление этой GPU недоступно.";
            var init = Export<Simple>("nvmlInit_v2");
            if (init == null) return "NVML не поддерживается.";
            Check(init()); _initialized = true;
            var count = Export<GetCount>("nvmlDeviceGetCount_v2");
            Check(count(out uint devices));
            var handle = Export<Handle>("nvmlDeviceGetHandleByIndex_v2");
            var names = new List<string>();
            // The shared telemetry samples the first NVIDIA adapter. Do not expose
            // tuning for extra adapters whose temperature is not monitored here.
            for (uint index = 0; index < Math.Min(devices, 1u); index++)
            {
                Check(handle(index, out var device));
                var name = new StringBuilder(128);
                Check(Export<NameGet>("nvmlDeviceGetName")(device, name, 128));
                string prefix = "GPU " + index + " · " + name;
                names.Add(prefix);
                AddOffset(settings, device, prefix, index, "Gpc", "Смещение ядра", 250);
                AddOffset(settings, device, prefix, index, "Mem", "Смещение памяти", 1000);
                var range = Export<UIntRange>("nvmlDeviceGetPowerManagementLimitConstraints");
                var read = Export<GetUInt>("nvmlDeviceGetPowerManagementLimit");
                var write = Export<SetUInt>("nvmlDeviceSetPowerManagementLimit");
                if (range != null && read != null && write != null && range(device, out uint min, out uint max) == 0 && read(device, out _) == 0)
                    settings.Add(new TuningSetting { Id = "gpu-" + index + "-power", Group = "GPU", Name = prefix + " · Лимит мощности", Unit = "W", Minimum = Math.Ceiling(min / 1000.0), Maximum = Math.Floor(max / 1000.0), Read = () => { Check(read(device, out uint v)); return v / 1000.0; }, Write = v => Check(write(device, checked((uint)Math.Round(v * 1000)))) });
                AddFans(settings, device, prefix, index);
            }
            return string.Join("\n", names) + "\nНастройка первого адаптера NVIDIA. Доступность записи зависит от разрешений и драйвера. Напряжение не поддерживается.";
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetCount(out uint count);
        private void AddOffset(List<TuningSetting> settings, IntPtr device, string prefix, uint index, string kind, string label, int conservativeMaximum)
        {
            var getClock = Export<ClockQuery>("nvmlDeviceGetClockOffsets");
            var setClock = Export<ClockQuery>("nvmlDeviceSetClockOffsets");
            ClockOffset Query()
            {
                var value = new ClockOffset { Version = (uint)Marshal.SizeOf<ClockOffset>() | (1u << 24), Type = kind == "Gpc" ? 0u : 2u, Pstate = 0 };
                Check(getClock(device, ref value)); return value;
            }
            if (getClock != null && setClock != null)
            {
                try
                {
                    var clock = Query();
                    settings.Add(new TuningSetting
                    {
                        Id = "gpu-" + index + "-" + kind, Group = "GPU", Name = prefix + " · " + label + " (P0)", Unit = "MHz",
                        Minimum = Math.Max(clock.Minimum, -conservativeMaximum), Maximum = Math.Min(clock.Maximum, conservativeMaximum), Read = () => Query().Offset,
                        Write = v => { var value = Query(); value.Offset = (int)Math.Round(v); Check(setClock(device, ref value)); }
                    });
                    return;
                }
                catch (InvalidOperationException) { /* Older drivers may only expose the legacy API. */ }
            }
            var range = Export<IntRange>("nvmlDeviceGet" + kind + "ClkMinMaxVfOffset");
            var read = Export<GetInt>("nvmlDeviceGet" + kind + "ClkVfOffset");
            var write = Export<SetInt>("nvmlDeviceSet" + kind + "ClkVfOffset");
            if (range == null || read == null || write == null || range(device, out int min, out int max) != 0 || read(device, out _) != 0) return;
            settings.Add(new TuningSetting { Id = "gpu-" + index + "-" + kind, Group = "GPU", Name = prefix + " · " + label, Unit = "MHz", Minimum = Math.Max(min, -conservativeMaximum), Maximum = Math.Min(max, conservativeMaximum), Read = () => { Check(read(device, out int v)); return v; }, Write = v => Check(write(device, (int)Math.Round(v))) });
        }
        private void AddFans(List<TuningSetting> settings, IntPtr device, string prefix, uint index)
        {
            var count = Export<GetUInt>("nvmlDeviceGetNumFans");
            var range = Export<UIntRange>("nvmlDeviceGetMinMaxFanSpeed");
            var read = Export<FanGet>("nvmlDeviceGetFanSpeed_v2");
            var target = Export<FanGet>("nvmlDeviceGetTargetFanSpeed");
            var policy = Export<FanGet>("nvmlDeviceGetFanControlPolicy_v2");
            var setPolicy = Export<FanSet>("nvmlDeviceSetFanControlPolicy");
            var write = Export<FanSet>("nvmlDeviceSetFanSpeed_v2");
            if (count == null || read == null || policy == null || setPolicy == null || write == null || count(device, out uint fans) != 0) return;
            uint min = 40, max = 100;
            if (range != null && range(device, out uint driverMin, out uint driverMax) == 0) { min = Math.Max(min, driverMin); max = Math.Min(max, driverMax); }
            if (min > max) return;
            for (uint f = 0; f < fans; f++)
            {
                uint fan = f;
                if (read(device, fan, out _) != 0 || policy(device, fan, out _) != 0) continue;
                settings.Add(new TuningSetting
                {
                    Id = "gpu-" + index + "-fan-" + fan, Group = "GPU FAN", Name = prefix + " · Вентилятор " + (fan + 1), Unit = "%", Minimum = min, Maximum = max,
                    Read = () => { Check(read(device, fan, out uint speed)); return speed; },
                    Write = v => Check(write(device, fan, (uint)Math.Round(v))),
                    Automatic = () => Check(setPolicy(device, fan, 0)),
                    CaptureRestore = () =>
                    {
                        Check(policy(device, fan, out uint oldPolicy)); Check(read(device, fan, out uint oldSpeed));
                        if (oldPolicy == 1 && target != null && target(device, fan, out uint previousTarget) == 0) oldSpeed = previousTarget;
                        return () => { if (oldPolicy == 1) Check(write(device, fan, oldSpeed)); Check(setPolicy(device, fan, oldPolicy)); };
                    }
                });
            }
        }
        public void Dispose()
        {
            if (_library == IntPtr.Zero) return;
            if (_initialized) Export<Simple>("nvmlShutdown")?.Invoke();
            NativeLibrary.Free(_library); _library = IntPtr.Zero;
        }
    }
}
