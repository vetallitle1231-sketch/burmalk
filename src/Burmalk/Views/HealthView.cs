using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    public class HealthView : UserControl, IRefreshable
    {
        private readonly TextBlock _state = ViewUi.Text("Нет данных", "BigValue");
        private readonly TextBlock _reason = ViewUi.Text("Подключение датчиков…");
        private readonly TextBlock _coverage = ViewUi.Text("");
        private readonly Grid _groups = new();
        private StorageDrive[] _drives = Array.Empty<StorageDrive>();
        private DateTime _lastSpace = DateTime.MinValue;

        public HealthView()
        {
            _state.FontSize = 42;
            _groups.ColumnDefinitions.Add(new ColumnDefinition()); _groups.ColumnDefinitions.Add(new ColumnDefinition());
            var root = new StackPanel();
            var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
            foreach (var level in Enum.GetValues<HealthLevel>())
            {
                var label = ViewUi.Text("●  " + HealthAssessment.Label(level)); label.FontWeight = FontWeights.SemiBold; label.SetResourceReference(TextBlock.ForegroundProperty, ColorKey(level));
                var badge = new Border { Child = label, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
                badge.SetResourceReference(Border.BorderBrushProperty, ColorKey(level)); legend.Children.Add(badge);
            }
            root.Children.Add(ViewUi.Panel("Состояние ПК · сейчас", _state, _reason, legend, _coverage,
                ViewUi.Text("Оценка текущих показателей по порогам Burmalk. Это не диагностика исправности всех комплектующих.")));
            root.Children.Add(_groups);
            var rules = new Expander { Header = ViewUi.Text("Как определяется состояние"), Margin = new Thickness(0, 6, 12, 14),
                Content = ViewUi.Text("Итог задаёт худший из оценённых показателей: температура CPU/GPU, занятость RAM/VRAM и заполнение каждого диска. Точные пороги указаны в карточках.\n\nЧастоты, мощность, нагрузка, обороты и сеть показаны для контекста: без ограничений конкретной модели они не доказывают неисправность.\n\nЕсли важный показатель отсутствует, оценка отмечена как неполная и не получает зелёное «Отлично». Показания старше 30 секунд исключаются. SMART сейчас не получен.") };
            root.Children.Add(rules); Content = root; Refresh();
        }

        private static string ColorKey(HealthLevel? level) => level.HasValue ? "Health" + level.Value : "FgMuted";
        public void Refresh()
        {
            if (DateTime.UtcNow - _lastSpace > TimeSpan.FromSeconds(10))
            {
                _drives = App.Monitor.Storage.RefreshSpace().Where(d => d.MediaKind is "Fixed" or "Removable").ToArray();
                _lastSpace = DateTime.UtcNow;
            }
            double cpuRpm = double.NaN, gpuFan = double.NaN;
            if (App.Tuning?.Ready.IsCompletedSuccessfully == true)
            {
                var saved = App.Store.Settings;
                var bound = CpuFanBinding.Resolve(App.Tuning.Settings("CPU FAN"), App.Tuning.BoardFans(), App.Tuning.BoardId,
                    saved.CpuFanBoardId, saved.CpuFanControlId, saved.CpuFanRpmSensorId);
                try { if (bound.Sensor != null) cpuRpm = App.Tuning.Read(bound.Sensor); } catch { }
                try { var fan = App.Tuning.Settings("GPU FAN").FirstOrDefault(); if (fan != null) gpuFan = App.Tuning.Read(fan); } catch { }
            }
            bool? network = null;
            try { network = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); } catch { }
            var report = HealthAssessment.Evaluate(App.Monitor.LastSample, _drives, DateTime.UtcNow.Ticks, network, cpuRpm, gpuFan);
            Render(report);
        }

        internal void Render(HealthReport report)
        {
            _state.Text = HealthAssessment.Label(report.Level); _state.SetResourceReference(TextBlock.ForegroundProperty, ColorKey(report.Level));
            _reason.Text = report.Reason;
            _coverage.Text = "Оценено показателей: " + report.AssessedCount + " · " + (report.Partial ? "НЕПОЛНАЯ ОЦЕНКА\nНет данных: " + string.Join(", ", report.Missing) : "Основные показатели доступны")
                + (report.Stale ? "\nНет свежего снимка датчиков — старые показания не используются." : "");
            _groups.Children.Clear(); _groups.RowDefinitions.Clear(); int index = 0;
            foreach (var group in report.Readings.GroupBy(r => r.Group))
            {
                var rows = new StackPanel();
                foreach (var item in group)
                {
                    var heading = new DockPanel();
                    var value = ViewUi.Text(item.Value); value.FontSize = 15; value.FontWeight = FontWeights.SemiBold;
                    value.SetResourceReference(TextBlock.ForegroundProperty, ColorKey(item.Level));
                    value.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(value, Dock.Right); heading.Children.Add(value);
                    heading.Children.Add(ViewUi.Text(item.Name)); rows.Children.Add(heading);
                    var note = ViewUi.Text(item.Level.HasValue ? HealthAssessment.Label(item.Level) + " · " + item.Detail : item.Detail);
                    note.FontSize = 11; note.Margin = new Thickness(0, 2, 0, 13); note.ToolTip = item.Detail;
                    rows.Children.Add(note);
                }
                if (index % 2 == 0) _groups.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var panel = ViewUi.Panel(group.Key, rows); Grid.SetColumn(panel, index % 2); Grid.SetRow(panel, index / 2);
                _groups.Children.Add(panel); index++;
            }
        }
    }
}
