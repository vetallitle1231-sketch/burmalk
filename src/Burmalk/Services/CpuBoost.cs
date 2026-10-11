using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Burmalk.Services
{
    internal static class CpuBoost
    {
        private static Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        private static void Check(uint error) { if (error != 0) throw new Win32Exception((int)error); }
        private static Guid Active()
        {
            Check(PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr));
            try { return Marshal.PtrToStructure<Guid>(ptr); } finally { LocalFree(ptr); }
        }
        public static string Probe(List<TuningSetting> settings)
        {
            Guid plan = Active();
            Add(settings, plan, "boost", "Режим буста: 0 выключен · 1 включён · 2 агрессивный", "", "be337238-0d82-4146-a960-4f3749d470c7", 0, 2);
            Add(settings, plan, "state", "Максимальная производительность CPU", "%", "bc5038f7-23e0-4960-96da-33abaf5935ec", 10, 100);
            // Windows stores this setting but some CPUs ignore it. Do not claim a fixed clock.
            Add(settings, plan, "frequency", "Предел частоты Windows (0 = без ограничения)", "MHz", "75b0ae3f-bce0-45a7-8c89-c9611c25e100", 0, 6000);
            return "Настройки текущего плана Windows при питании от сети. CPU сам выбирает частоту в пределах прошивки.\nРучной множитель, напряжение и PPT требуют BIOS / утилиты производителя и здесь недоступны. Ограничение MHz может не поддерживаться процессором.";
        }
        private static void Add(List<TuningSetting> settings, Guid plan, string id, string name, string unit, string guid, int min, int max)
        {
            Guid setting = new(guid);
            if (PowerReadACValueIndex(IntPtr.Zero, ref plan, ref Processor, ref setting, out _) != 0) return;
            double Read() { Check(PowerReadACValueIndex(IntPtr.Zero, ref plan, ref Processor, ref setting, out uint v)); return v; }
            void Write(double v, bool requireActive)
            {
                Guid active = Active();
                if (requireActive && active != plan) throw new InvalidOperationException("План питания изменён. Перезапусти Burmalk перед настройкой CPU.");
                Check(PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref Processor, ref setting, (uint)Math.Round(v)));
                if (active == plan) Check(PowerSetActiveScheme(IntPtr.Zero, ref plan));
            }
            settings.Add(new TuningSetting { Id = "cpu-" + id, Group = "CPU", Name = name, Unit = unit, Minimum = min, Maximum = max, Read = Read, Write = v => Write(v, true), CaptureRestore = () => { double previous = Read(); return () => Write(previous, false); } });
        }
    }
}
