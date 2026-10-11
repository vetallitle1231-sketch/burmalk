using System;
using System.Collections.Generic;
using System.Linq;
using Burmalk.Models;

namespace Burmalk.Services
{
    internal enum HealthLevel { Excellent, Good, Fair, Poor }
    internal sealed record HealthReading(string Group, string Name, string Value, bool Available, HealthLevel? Level, string Detail);
    internal sealed class HealthReport
    {
        public HealthLevel? Level;
        public readonly List<HealthReading> Readings = new();
        public readonly List<string> Missing = new();
        public string Reason = "";
        public bool Stale;
        public int AssessedCount => Readings.Count(r => r.Level.HasValue);
        public bool Partial => Missing.Count > 0;
    }
    internal static class HealthAssessment
    {
        public static string Label(HealthLevel? level) => level switch
        {
            HealthLevel.Excellent => "Отлично", HealthLevel.Good => "Хорошо", HealthLevel.Fair => "Неплохо", HealthLevel.Poor => "Плохо", _ => "Нет данных"
        };
        private static bool Number(double v, double max = double.MaxValue) => double.IsFinite(v) && v >= 0 && v <= max;
        private static HealthLevel Grade(double value, double good, double fair, double poor) =>
            value >= poor ? HealthLevel.Poor : value >= fair ? HealthLevel.Fair : value >= good ? HealthLevel.Good : HealthLevel.Excellent;

        public static HealthReport Evaluate(Sample sample, IEnumerable<StorageDrive> drives, long nowUtc,
            bool? networkConnected = null, double cpuFanRpm = double.NaN, double gpuFanPercent = double.NaN)
        {
            var report = new HealthReport();
            report.Stale = sample == null || sample.TimestampUtc <= 0 || sample.TimestampUtc > nowUtc + TimeSpan.TicksPerSecond * 5 || nowUtc - sample.TimestampUtc > TimeSpan.TicksPerSecond * 30;
            var s = report.Stale ? new Sample() : sample;
            void Rated(string group, string name, double value, bool available, string unit, double good, double fair, double poor, string detail, bool essential = true)
            {
                var level = available ? Grade(value, good, fair, poor) : (HealthLevel?)null;
                report.Readings.Add(new(group, name, available ? value.ToString("0.#") + " " + unit : "—", available, level,
                    available ? detail : "Данные недоступны; показатель не оценён."));
                if (!available && essential) report.Missing.Add(name);
            }
            void Info(string group, string name, double value, string unit, string detail, double max = double.MaxValue)
            {
                bool available = Number(value, max);
                report.Readings.Add(new(group, name, available ? value.ToString("0.#") + " " + unit : "—", available, null, available ? detail : "Данные недоступны."));
            }

            Rated("Процессор", "Температура CPU", s.CpuTempC, Number(s.CpuTempC, 150) && s.CpuTempC > 0, "°C", 70, 80, 90, "Пороги Burmalk: 70 / 80 / 90 °C.");
            Info("Процессор", "Загрузка CPU", s.CpuPercent, "%", "Высокая загрузка во время работы или игры сама по себе не означает неисправность.", 100);
            Info("Процессор", "Частота CPU", s.CpuGhz * 1000, "MHz", "Текущая частота зависит от нагрузки и энергосбережения.");
            Rated("Видеокарта", "Температура GPU", s.GpuTempC, Number(s.GpuTempC, 150) && s.GpuTempC > 0, "°C", 65, 75, 85, "Пороги Burmalk: 65 / 75 / 85 °C.");
            Info("Видеокарта", "Загрузка GPU", s.GpuPercent, "%", "Нагрузка показана для контекста; 100% в игре не считается поломкой.", 100);
            Info("Видеокарта", "Частота ядра", s.GpuClockMhz, "MHz", "Показания драйвера NVIDIA.");
            Info("Видеокарта", "Частота видеопамяти", s.GpuMemClockMhz, "MHz", "По шкале драйвера NVIDIA; отдельно от частоты ядра.");
            Info("Видеокарта", "Мощность GPU", s.GpuPowerW, "W", "Справочное потребление; допустимые пределы зависят от модели.");
            Rated("Память", "Занято RAM", s.RamPercent, Number(s.RamPercent, 100), "%", 75, 90, 97, "75 / 90 / 97%: уменьшается запас свободной оперативной памяти.");
            Info("Память", "Используется RAM", s.RamUsedGb, "GiB", "Объём занятой оперативной памяти.");
            bool vramAvailable = Number(s.GpuVramUsedGb) && Number(s.GpuVramTotalGb) && s.GpuVramTotalGb > 0 && s.GpuVramUsedGb <= s.GpuVramTotalGb;
            Rated("Память", "Занято VRAM", vramAvailable ? s.GpuVramUsedGb / s.GpuVramTotalGb * 100 : double.NaN, vramAvailable, "%", 80, 92, 98, "80 / 92 / 98%: уменьшается запас видеопамяти.");
            var localDrives = (drives ?? Array.Empty<StorageDrive>()).Where(d => d != null).ToArray();
            if (localDrives.Length == 0)
            {
                report.Missing.Add("Свободное место на дисках");
                report.Readings.Add(new("Диски", "Свободное место", "—", false, null, "Нет доступных томов для оценки."));
            }
            foreach (var drive in localDrives)
            {
                bool valid = drive.TotalGb > 0 && drive.FreeGb >= 0 && drive.FreeGb <= drive.TotalGb && Number(drive.UsedPercent, 100);
                var level = valid ? Grade(drive.UsedPercent, 85, 93, 98) : (HealthLevel?)null;
                report.Readings.Add(new("Диски", "Свободно на " + drive.Letter, valid ? drive.FreeGb + " GiB" : "—", valid, level,
                    valid ? "Занято " + drive.UsedPercent.ToString("0.#") + "%. Пороги заполнения: 85 / 93 / 98%." : "Место на томе не оценено."));
                if (!valid) report.Missing.Add("Свободное место " + drive.Letter);
            }
            Info("Диски", "Активность дисков", s.DiskActivePercent, "%", "Занятость дисков под нагрузкой; не проверка исправности накопителей.", 100);
            Info("Диски", "Чтение", s.DiskReadKBps, "KiB/s", "Текущий поток чтения.");
            Info("Диски", "Запись", s.DiskWriteKBps, "KiB/s", "Текущий поток записи.");
            report.Readings.Add(new("Диски", "SMART", "Не получен", false, null, "Состояние накопителей по SMART не проверено и не входит в оценку."));
            Info("Охлаждение", "Кулер CPU", cpuFanRpm, "RPM", "Только назначенный CPU_FAN. Обороты не оцениваются без данных о модели кулера.");
            Info("Охлаждение", "Кулер GPU", gpuFanPercent, "%", "0% может быть штатной остановкой кулера на холодной карте.", 100);
            report.Readings.Add(new("Сеть", "Подключение", networkConnected.HasValue ? networkConnected.Value ? "Есть сеть" : "Нет сети" : "—", networkConnected.HasValue, null, "Отсутствие сети не означает неисправность ПК."));
            Info("Сеть", "Получение", s.NetDownKBps, "KiB/s", "Текущий сетевой трафик.");
            Info("Сеть", "Отправка", s.NetUpKBps, "KiB/s", "Текущий сетевой трафик.");

            var rated = report.Readings.Where(r => r.Level.HasValue).ToArray();
            if (rated.Length == 0) { report.Reason = "Недостаточно показаний для оценки. Дождись датчиков или разреши доступ к оборудованию."; return report; }
            report.Level = rated.Max(r => r.Level.Value);
            // Never present a complete green assessment when an essential input is missing.
            if (report.Level == HealthLevel.Excellent && report.Partial) report.Level = HealthLevel.Good;
            var worst = rated.Where(r => r.Level > HealthLevel.Excellent).OrderByDescending(r => r.Level).Take(3).ToArray();
            report.Reason = worst.Length > 0 ? string.Join(" · ", worst.Select(r => r.Name + ": " + r.Value)) : report.Partial
                ? "В доступных показаниях нет превышений, но часть важных данных отсутствует."
                : "Температуры, запас памяти и свободное место находятся в зелёной зоне Burmalk.";
            return report;
        }
    }
}
