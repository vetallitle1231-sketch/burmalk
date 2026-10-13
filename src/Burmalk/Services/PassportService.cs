using System;
using System.Linq;
using Burmalk.Models;

namespace Burmalk.Services
{
    internal static class PassportService
    {
        public static string Text(string value) => string.IsNullOrWhiteSpace(value) || value == "Unavailable" || value == "Unknown" ? "Нет данных" : value.Trim();
        public static string Number(double value, string unit) => double.IsFinite(value) && value >= 0 ? value.ToString("0.##") + " " + unit : "Нет данных";
        public static string GpuName() => Text(App.Monitor.Gpu.Last.Name) != "Нет данных" ? App.Monitor.Gpu.Last.Name : App.Monitor.Hardware.Snapshot.Gpu.Name;
        public static PcRankResult CurrentRank()
        {
            var r = App.Store.Performance;
            if (r != null && App.Monitor.Hardware.ProbeCompleted && !RankMethod.MatchesHardware(r, App.Monitor.Hardware.Snapshot, GpuName()))
                return new PcRankResult { Reason = "Комплектующие изменились. Пройди тест заново." };
            return RankMethod.Evaluate(r);
        }
        public static PcPassport Create(bool hidePersonal, string style)
        {
            var h = App.Monitor.Hardware.Snapshot; var s = App.Monitor.LastSample;
            bool fresh = DateTime.UtcNow.Ticks - s.TimestampUtc < TimeSpan.TicksPerSecond * 15;
            double vram = fresh && s.GpuVramTotalGb > 0 ? s.GpuVramTotalGb : double.NaN; // AdapterRAM is 32-bit and can misreport >4 GiB GPUs.
            var passport = new PcPassport { CreatedLocal = DateTime.Now, Style = style, Rank = CurrentRank(),
                OwnerLabel = hidePersonal ? "ТВОЙ ПК" : Text(h.System.ComputerName), TestedLocal = App.Store.Performance?.TestedUtc.ToLocalTime() };
            void Add(string icon, string title, params string[] lines) => passport.Sections.Add(new PassportSection { Title = title, Icon = icon, Lines = lines.ToList() });
            Add("◉", "ПРОЦЕССОР", Text(h.Cpu.Model), (h.Cpu.Cores > 0 ? h.Cpu.Cores + " ядер / " + h.Cpu.Threads + " потоков" : "Ядра и потоки: Нет данных"),
                "Макс. частота WMI: " + Number(h.Cpu.BaseGhz * 1000, "MHz"), "Текущая: " + Number(fresh ? s.CpuGhz * 1000 : double.NaN, "MHz"));
            Add("◆", "ВИДЕОКАРТА", Text(GpuName()), "VRAM: " + Number(vram, "GiB"), "Драйвер: " + Text(h.Gpu.DriverVersion), "Ядро: " + Number(fresh ? s.GpuClockMhz : double.NaN, "MHz"));
            Add("▤", "ОПЕРАТИВНАЯ ПАМЯТЬ", Number(h.Ram.TotalGb, "GiB") + " / " + Text(h.Ram.MemoryType),
                "Скорость: " + Number(h.Ram.MaxSpeedMhz, "MT/s"), "Модули: " + (h.Ram.Modules.Count > 0 ? h.Ram.Modules.Count.ToString() : "Нет данных"),
                string.Join("; ", h.Ram.Modules.Select(m => Text(m.Manufacturer) + " " + Text(m.PartNumber) + " · " + Number(m.CapacityGb, "GiB"))));
            Add("▦", "МАТЕРИНСКАЯ ПЛАТА", Text(h.Motherboard.Manufacturer) + " " + Text(h.Motherboard.Model), "BIOS: " + Text(h.Motherboard.BiosVersion), "Дата BIOS: " + Text(h.Motherboard.BiosDate));
            var disks = App.Monitor.Storage.PhysicalDrives;
            Add("▣", "НАКОПИТЕЛИ", disks.Count == 0 ? new[] { "Нет данных" } : disks.Select(d => Text(d.Model) + " / " + Text(d.Kind) + " / " + Number(d.CapacityGb, "GiB")).ToArray());
            Add("◇", "СИСТЕМА", Text(h.System.OsName) + " · " + Text(h.System.Architecture), "Версия: " + Text(h.System.OsVersion) + " / build " + Text(h.System.OsBuild),
                "Мониторы: " + (h.System.Monitors.Count == 0 ? "Нет данных" : string.Join("; ", h.System.Monitors)));
            var r = App.Store.Benchmarks ?? App.Store.Performance;
            if (r != null && (!RankMethod.MatchesHardware(r, h, GpuName()) || r.MethodVersion != RankMethod.Version)) r = null;
            Add("ϟ", "ПРОИЗВОДИТЕЛЬНОСТЬ", "GPU: " + (r?.GpuCompleted == true ? Math.Round(r.GpuFps * 100) + " баллов / " + r.GpuFps.ToString("0.0") + " FPS сцены" : "Не протестировано"),
                "CPU: " + (r?.CpuCompleted == true ? r.CpuMillionOps.ToString("0.00") + " млн операций/с" : "Не протестировано"),
                "RAM: " + (r?.RamCompleted == true ? r.RamMegabytesPerSecond.ToString("0") + " MB/s / Хорошо · 128 МиБ" : "Не протестировано"),
                "BURMALK: " + (passport.Rank.Confirmed ? Math.Floor(passport.Rank.Total).ToString("0") + " / " + passport.Rank.Name + " · RANK " + passport.Rank.Letter : "Не протестировано"),
                "Методика: " + RankMethod.Version);
            return passport;
        }
    }
}
