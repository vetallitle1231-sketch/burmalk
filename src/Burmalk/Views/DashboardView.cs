using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Burmalk.Controls;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>A compact, movable home HUD. Widget order belongs to the user's settings.</summary>
    public class DashboardView : UserControl, IRefreshable
    {
        public static readonly string[] DefaultOrder = { "cpu", "gpu", "ram", "disk", "network", "game", "music", "system", "processes" };
        private readonly UniformGrid _grid = new UniformGrid { Columns = 3 };
        private readonly Dictionary<string, Border> _cards = new Dictionary<string, Border>();
        private readonly List<string> _order = new List<string>();
        private readonly List<Action<Sample>> _updates = new List<Action<Sample>>();
        private readonly List<LineGraph> _graphs = new List<LineGraph>();
        private readonly TextBlock _status = ViewUi.Text("Подключение датчиков…");
        private readonly TextBlock _gameValue = ViewUi.Text("Нет игры", "BigValue");
        private readonly TextBlock _gameDetail = ViewUi.Text("Игра появится здесь автоматически.");
        private readonly TextBlock _systemValue = ViewUi.Text("", "BigValue");
        private readonly TextBlock _systemDetail = ViewUi.Text("");
        private readonly TextBlock _processValue = ViewUi.Text("Топ процессов", "BigValue");
        private readonly TextBlock _processDetail = ViewUi.Text("Загрузка…");
        private readonly TextBlock _musicValue = ViewUi.Text("Сейчас ничего не играет", "BigValue");
        private readonly TextBlock _musicDetail = ViewUi.Text("Запустите Spotify или SoundCloud.");
        private readonly TextBlock _musicSource = ViewUi.Text("МУЗЫКА · WINDOWS");
        private DateTime _lastProcesses = DateTime.MinValue;
        private DateTime _lastMusic = DateTime.MinValue;
        private bool _loadingProcesses;
        private bool _loadingMusic;
        private Point _dragStart;

        public DashboardView()
        {
            var header = new Grid { Margin = new Thickness(2, 2, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "BURMALK  /  ОБЗОР", FontSize = 21, FontWeight = FontWeights.Bold });
            heading.Children.Add(ViewUi.Text("Все важные показатели на одном экране. Перетащите карточку за заголовок, чтобы изменить порядок."));
            header.Children.Add(heading);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(14, 0, 12, 0);
            Grid.SetColumn(_status, 1);
            header.Children.Add(_status);

            var hub = App.Monitor.Hub;
            AddMetric("cpu", "ПРОЦЕССОР", "◉", ViewUi.Blue, hub.CpuPercent,
                s => Fmt.Pct(s.CpuPercent), s => "Температура  " + Fmt.C(s.CpuTempC) + "    ·    " + MetricView.Number(s.CpuGhz * 1000, "MHz"), 100);
            AddMetric("gpu", "ВИДЕОКАРТА", "◆", ViewUi.Green, hub.GpuPercent,
                s => Fmt.Pct(s.GpuPercent), s => "Температура  " + Fmt.C(s.GpuTempC) + "    ·    VRAM  " + Fmt.Gb(s.GpuVramUsedGb), 100);
            AddMetric("ram", "ОПЕРАТИВНАЯ ПАМЯТЬ", "▤", ViewUi.Purple, hub.RamPercent,
                s => Fmt.Pct(s.RamPercent), s => "Занято  " + Fmt.Gb(s.RamUsedGb) + "  из  " + Fmt.Gb(App.Monitor.Hardware.Snapshot.Ram.TotalGb), 100);
            AddMetric("disk", "ДИСКИ", "▣", ViewUi.Gold, hub.DiskActivePercent,
                s => Fmt.Pct(s.DiskActivePercent), s => "Чтение  " + Fmt.Rate(s.DiskReadKBps) + "\nЗапись  " + Fmt.Rate(s.DiskWriteKBps), 100);
            AddMetric("network", "СЕТЬ", "↕", ViewUi.Blue, hub.NetDownKBps,
                s => "↓  " + Fmt.Rate(s.NetDownKBps), s => "↑  " + Fmt.Rate(s.NetUpKBps) + "    ·    " + (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "Подключено" : "Нет сети"), -1);
            AddSimple("game", "ИГРА И FPS", "▶", ViewUi.Green, _gameValue, _gameDetail);
            AddSimple("music", "СЕЙЧАС ИГРАЕТ", "♫", ViewUi.Purple, _musicValue, _musicDetail, _musicSource);
            AddSimple("system", "СИСТЕМА", "◇", ViewUi.Blue, _systemValue, _systemDetail);
            AddSimple("processes", "ПРОЦЕССЫ", "☷", ViewUi.Gold, _processValue, _processDetail);

            foreach (string id in App.Store.Settings.DashboardLayout ?? Array.Empty<string>())
                if (_cards.ContainsKey(id) && !_order.Contains(id)) _order.Add(id);
            foreach (string id in DefaultOrder)
                if (!_order.Contains(id)) _order.Add(id);
            App.Store.Settings.DashboardLayout = _order.ToArray();
            Rebuild();

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(_grid);
            Content = root;
            SizeChanged += (s, e) => _grid.Columns = ActualWidth >= 1050 ? 3 : ActualWidth >= 620 ? 2 : 1;
            Loaded += (s, e) => { Appearance.Changed += UpdateGlass; UpdateGlass(); Refresh(); };
            Unloaded += (s, e) => Appearance.Changed -= UpdateGlass;
            UpdateGlass();
            Refresh();
        }

        private void AddMetric(string id, string title, string icon, Color color, MetricHistory history,
            Func<Sample, string> value, Func<Sample, string> detail, double max)
        {
            var valueText = ViewUi.Text("—", "BigValue");
            var detailText = ViewUi.Text("");
            var graph = new LineGraph { Source = history, Height = 42, StrokeColor = color, MaxValue = max, Margin = new Thickness(0, 3, 0, 0) };
            _graphs.Add(graph);
            _updates.Add(s => { valueText.Text = value(s); detailText.Text = detail(s); });
            AddSimple(id, title, icon, color, valueText, detailText, graph);
        }

        private void AddSimple(string id, string title, string icon, Color color, params UIElement[] body)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(7), Width = 30, Height = 30,
                Child = new TextBlock { Text = icon, FontSize = 18, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            header.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) });
            header.PreviewMouseLeftButtonDown += (s, e) => _dragStart = e.GetPosition(header);
            header.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var point = e.GetPosition(header);
                if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                DragDrop.DoDragDrop(header, new DataObject("Burmalk.DashboardCard", id), DragDropEffects.Move);
            };
            var stack = new StackPanel();
            stack.Children.Add(header);
            foreach (var element in body) stack.Children.Add(element);
            var card = new Border
            {
                Child = stack, Height = 204, Padding = new Thickness(16, 13, 16, 10),
                Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(1), AllowDrop = true, Tag = id
            };
            card.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent("Burmalk.DashboardCard") ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            card.Drop += (s, e) =>
            {
                if (e.Data.GetData("Burmalk.DashboardCard") is not string dragged || dragged == id || !_order.Contains(dragged)) return;
                MoveCard(dragged, id);
                e.Handled = true;
            };
            _cards.Add(id, card);
        }

        private void Rebuild()
        {
            _grid.Children.Clear();
            foreach (string id in _order) _grid.Children.Add(_cards[id]);
        }

        internal string[] CurrentOrder => _order.ToArray();

        internal void MoveCard(string dragged, string target)
        {
            if (dragged == target || !_order.Contains(dragged) || !_order.Contains(target)) return;
            _order.Remove(dragged);
            _order.Insert(_order.IndexOf(target), dragged);
            App.Store.Settings.DashboardLayout = _order.ToArray();
            App.Store.Save();
            Rebuild();
        }

        private void UpdateGlass()
        {
            var baseColor = (Theme.Brush("PanelBg") as SolidColorBrush)?.Color ?? Color.FromRgb(16, 29, 53);
            var accent = (Theme.Brush("Accent") as SolidColorBrush)?.Color ?? ViewUi.Blue;
            var fill = new SolidColorBrush(Color.FromArgb(App.Store.Settings.Theme == "Light" ? (byte)224 : (byte)190,
                baseColor.R, baseColor.G, baseColor.B));
            var stroke = new SolidColorBrush(Color.FromArgb(110, accent.R, accent.G, accent.B));
            foreach (var card in _cards.Values) { card.Background = fill; card.BorderBrush = stroke; }
        }

        public void Refresh()
        {
            var sample = App.Monitor.LastSample;
            foreach (var update in _updates) update(sample);
            foreach (var graph in _graphs) graph.InvalidateVisual();
            _status.Text = "●  LIVE  ·  " + DateTime.Now.ToString("HH:mm:ss");

            var game = GameService.Current;
            _gameValue.Text = game?.Active == true && game.FpsAvailable ? MetricView.Number(game.FpsCurrent, "FPS") : game?.Active == true ? game.GameName : "Нет игры";
            _gameDetail.Text = game?.Active == true
                ? game.GameName + (game.FpsAvailable ? "    ·    AVG " + MetricView.Number(game.FpsAvg, "FPS") : "    ·    FPS недоступен")
                : "Игра появится здесь автоматически.";

            var hardware = App.Monitor.Hardware.Snapshot;
            _systemValue.Text = DateTime.Now.ToString("HH:mm");
            _systemDetail.Text = hardware.Cpu.Model + "\n" + App.Monitor.Gpu.Last.Name + "\nРаботает  " + Fmt.Dur(Environment.TickCount64 / 1000.0);

            if (!IsVisible) return;
            if (!_loadingProcesses && (DateTime.UtcNow - _lastProcesses).TotalSeconds > 8) _ = RefreshProcessesAsync();
            if (!_loadingMusic && (DateTime.UtcNow - _lastMusic).TotalSeconds > 3) _ = RefreshMusicAsync();
        }

        private async Task RefreshProcessesAsync()
        {
            _loadingProcesses = true;
            try
            {
                var rows = await Task.Run(ProcessSnapshot.Capture);
                _processDetail.Text = string.Join("\n", rows.OrderByDescending(p => p.MemoryMb).Take(3)
                    .Select(p => p.Name + "    ·    " + Fmt.MB(p.MemoryMb)));
                if (_processDetail.Text.Length == 0) _processDetail.Text = "Нет данных о процессах.";
            }
            catch (Exception ex) { ErrorLog.Write("Dashboard processes: " + ex.Message); _processDetail.Text = "Данные недоступны."; }
            finally { _loadingProcesses = false; _lastProcesses = DateTime.UtcNow; }
        }

        private async Task RefreshMusicAsync()
        {
            _loadingMusic = true;
            try
            {
                var track = await NowPlayingService.ReadAsync();
                _musicSource.Text = track == null ? "SPOTIFY  /  SOUNDCLOUD" : track.Source.ToUpperInvariant() + (track.Playing ? "  ·  ИГРАЕТ" : "  ·  ПАУЗА");
                _musicValue.Text = track?.Title ?? "Сейчас ничего не играет";
                _musicValue.ToolTip = track?.Title;
                _musicDetail.Text = track?.Artist ?? "Запустите Spotify или SoundCloud.";
            }
            finally { _loadingMusic = false; _lastMusic = DateTime.UtcNow; }
        }
    }
}
