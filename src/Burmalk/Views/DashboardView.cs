using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Burmalk.Controls;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>A compact, movable home HUD. Widget order belongs to the user's settings.</summary>
    public class DashboardView : UserControl, IRefreshable
    {
        public static readonly string[] DefaultOrder = { "cpu", "gpu", "ram", "disk", "network", "game", "music", "system", "processes", "rank", "pc3d" };
        private readonly Burmalk.Controls.RankEmblem _rankEmblem = new Burmalk.Controls.RankEmblem { Width = 100, Height = 100 };
        private readonly TextBlock _rankName = ViewUi.Text("ПОЛУЧИТЬ РАНГ");
        private readonly TextBlock _rankDetail = ViewUi.Text("Пройди полный тест PC RANK");
        private readonly Burmalk.Controls.RankEmblem _rankHeaderEmblem = new Burmalk.Controls.RankEmblem { Width = 60, Height = 60 };
        private readonly TextBlock _rankHeaderText = ViewUi.Text("");
        private readonly Button _rankHeaderButton = new Button { ContentTemplate = null, Visibility = Visibility.Collapsed, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 3, 0, 0) };
        private readonly PcView3D _pcModel = new PcView3D(true);
        internal PcView3D PcModelWidget => _pcModel;
        private readonly Canvas _grid = new Canvas { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly ScrollViewer _workspace = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly Dictionary<string, Border> _cards = new Dictionary<string, Border>();
        private readonly List<string> _order = new List<string>();
        private readonly HashSet<string> _visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly WrapPanel _widgetToggles = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        private readonly List<Action<Sample>> _updates = new List<Action<Sample>>();
        private readonly List<LineGraph> _graphs = new List<LineGraph>();
        private readonly Dictionary<LineGraph, Color> _graphColors = new();
        private readonly TextBlock _status = ViewUi.Text("Подключение датчиков…");
        private readonly TextBlock _machine = ViewUi.Text("");
        private readonly Dictionary<string, Color> _cardColors = new();
        private readonly TextBlock _gameValue = ViewUi.Text("Нет игры", "BigValue");
        private readonly TextBlock _gameDetail = ViewUi.Text("Игра появится здесь автоматически.");
        private readonly TextBlock _systemValue = ViewUi.Text("", "BigValue");
        private readonly TextBlock _systemDetail = ViewUi.Text("");
        private readonly TextBlock _processValue = ViewUi.Text("Топ процессов", "BigValue");
        private readonly TextBlock _processDetail = ViewUi.Text("Загрузка…");
        private readonly TextBlock _musicValue = ViewUi.Text("Сейчас ничего не играет", "BigValue");
        private readonly TextBlock _musicDetail = ViewUi.Text("Запустите Spotify или SoundCloud.");
        private readonly TextBlock _musicSource = ViewUi.Text("МУЗЫКА · WINDOWS");
        private readonly Image _musicArtwork = new Image { Width = 76, Height = 76, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 2, 12, 0), Visibility = Visibility.Collapsed };
        private string _artworkPath;
        private DateTime _lastProcesses = DateTime.MinValue;
        private DateTime _lastMusic = DateTime.MinValue;
        private bool _loadingProcesses;
        private bool _loadingMusic;
        private Point _dragOffset;
        private string _dragged;
        private int _topLayer;

        public DashboardView()
        {
            _musicValue.FontSize = 22; _musicValue.TextWrapping = TextWrapping.Wrap; _musicValue.MaxHeight = 90;
            _musicDetail.TextTrimming = TextTrimming.CharacterEllipsis; _musicDetail.MaxHeight = 38;
            var hub = App.Monitor.Hub;
            var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition()); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            var introduction = new StackPanel { Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
            introduction.Children.Add(BrandVisuals.Badge("BURMALK  /  PULSE"));
            var titleRow = new DockPanel();
            var headerRank = new DockPanel(); DockPanel.SetDock(_rankHeaderEmblem, Dock.Left); headerRank.Children.Add(_rankHeaderEmblem);
            _rankHeaderText.FontSize = 11; _rankHeaderText.VerticalAlignment = VerticalAlignment.Center; headerRank.Children.Add(_rankHeaderText);
            _rankHeaderButton.Content = headerRank; _rankHeaderButton.Click += (s, e) => (Window.GetWindow(this) as MainWindow)?.NavigateTo("PC RANK");
            DockPanel.SetDock(_rankHeaderButton, Dock.Right); titleRow.Children.Add(_rankHeaderButton);
            titleRow.Children.Add(new TextBlock { Text = "Твой ПК", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 12) }); introduction.Children.Add(titleRow);
            _machine.FontSize = 11; _machine.TextWrapping = TextWrapping.NoWrap; _machine.TextTrimming = TextTrimming.CharacterEllipsis; introduction.Children.Add(_machine);
            hero.Children.Add(introduction);
            var pulse = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var pulseHeading = new DockPanel();
            var pulseTitle = ViewUi.Text("ПУЛЬС ПК"); pulseTitle.FontSize = 11; pulseTitle.FontWeight = FontWeights.SemiBold; pulseHeading.Children.Add(pulseTitle);
            _status.FontSize = 10; _status.HorizontalAlignment = HorizontalAlignment.Right; _status.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); pulseHeading.Children.Add(_status);
            pulse.Children.Add(pulseHeading);
            AddPulse(pulse, "CPU", hub.CpuPercent, s => s.CpuPercent, Color.FromRgb(78, 219, 210));
            AddPulse(pulse, "GPU", hub.GpuPercent, s => s.GpuPercent, ViewUi.Green);
            AddPulse(pulse, "RAM", hub.RamPercent, s => s.RamPercent, ViewUi.Purple);
            var pulseSurface = new Border { Child = pulse, Padding = new Thickness(20, 0, 0, 0), BorderThickness = new Thickness(1, 0, 0, 0) };
            pulseSurface.SetResourceReference(Border.BorderBrushProperty, "BorderColor"); Grid.SetColumn(pulseSurface, 1); hero.Children.Add(pulseSurface);
            var header = new Border { Child = hero, Padding = new Thickness(24, 18, 24, 18), CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 14) };
            header.SetResourceReference(Border.BackgroundProperty, "HeroBg"); header.SetResourceReference(Border.BorderBrushProperty, "BorderColor");

            AddMetric("cpu", "ПРОЦЕССОР", "◉", ViewUi.Blue, hub.CpuPercent,
                s => Fmt.Pct(s.CpuPercent), s => (double.IsFinite(s.CpuTempC) ? "Температура  " + Fmt.C(s.CpuTempC) : "Температура  —") + "    ·    " + MetricView.Number(s.CpuGhz * 1000, "MHz"), 100);
            AddMetric("gpu", "ВИДЕОКАРТА", "◆", ViewUi.Green, hub.GpuPercent,
                s => Fmt.Pct(s.GpuPercent), s => "Температура  " + Fmt.C(s.GpuTempC) + "    ·    VRAM  " + Fmt.Gb(s.GpuVramUsedGb), 100);
            AddMetric("ram", "ОПЕРАТИВНАЯ ПАМЯТЬ", "▤", ViewUi.Purple, hub.RamPercent,
                s => Fmt.Pct(s.RamPercent), s => "Занято " + Fmt.Gb(s.RamUsedGb) + " · установлено " + PassportService.Number(App.Monitor.Hardware.Snapshot.Ram.InstalledGb, "GiB"), 100);
            AddMetric("disk", "ДИСКИ", "▣", ViewUi.Gold, hub.DiskActivePercent,
                s => Fmt.Pct(s.DiskActivePercent), s => "Чтение  " + Fmt.Rate(s.DiskReadKBps) + "\nЗапись  " + Fmt.Rate(s.DiskWriteKBps), 100);
            AddMetric("network", "СЕТЬ", "↕", ViewUi.Blue, hub.NetDownKBps,
                s => "↓  " + Fmt.Rate(s.NetDownKBps), s => "↑  " + Fmt.Rate(s.NetUpKBps) + "    ·    " + (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "Подключено" : "Нет сети"), -1);
            AddSimple("game", "ИГРА И FPS", "▶", ViewUi.Green, _gameValue, _gameDetail);
            AddSimple("music", "СЕЙЧАС ИГРАЕТ", "♫", ViewUi.Purple, _musicArtwork, _musicValue, _musicDetail, _musicSource);
            AddSimple("system", "СИСТЕМА", "◇", ViewUi.Blue, _systemValue, _systemDetail);
            AddSimple("processes", "ПРОЦЕССЫ", "☷", ViewUi.Gold, _processValue, _processDetail);
            var rankBody = new DockPanel(); DockPanel.SetDock(_rankEmblem, Dock.Left); rankBody.Children.Add(_rankEmblem);
            var rankInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
            _rankName.FontSize = 15; _rankName.FontWeight = FontWeights.SemiBold; _rankDetail.FontSize = 10;
            rankInfo.Children.Add(_rankName); rankInfo.Children.Add(_rankDetail); rankBody.Children.Add(rankInfo);
            AddSimple("rank", "MY PC RANK", "✦", ViewUi.Purple, rankBody, ViewUi.Button("ПОЛУЧИТЬ РАНГ / ТЕСТ", () => (Window.GetWindow(this) as MainWindow)?.NavigateTo("PC RANK")));
            AddSimple("pc3d", "МОЙ ПК · 3D", "◇", ViewUi.Blue, _pcModel);
            if (!App.Store.Settings.PcModelWidgetIntroduced)
            {
                App.Store.Settings.DashboardVisible = (App.Store.Settings.DashboardVisible ?? DefaultOrder).Append("pc3d").Distinct().ToArray();
                App.Store.Settings.PcModelWidgetIntroduced = true; App.Store.Save();
            }
            if (!App.Store.Settings.RankWidgetIntroduced)
            {
                App.Store.Settings.DashboardVisible = (App.Store.Settings.DashboardVisible ?? DefaultOrder).Concat(new[] { "rank" }).Distinct().ToArray();
                App.Store.Settings.RankWidgetIntroduced = true; App.Store.Save();
            }

            foreach (string id in App.Store.Settings.DashboardLayout ?? Array.Empty<string>())
                if (_cards.ContainsKey(id) && !_order.Contains(id)) _order.Add(id);
            foreach (string id in DefaultOrder)
                if (!_order.Contains(id)) _order.Add(id);
            App.Store.Settings.DashboardLayout = _order.ToArray();
            string[] savedVisible = App.Store.Settings.DashboardVisible ?? DefaultOrder;
            foreach (string id in savedVisible) if (_cards.ContainsKey(id)) _visible.Add(id);
            foreach (string id in _visible.OrderBy(x => Array.IndexOf(DefaultOrder, x)))
            {
                var toggle = new CheckBox { Content = DashboardTitle(id), IsChecked = true, Margin = new Thickness(0, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center, Tag = id };
                toggle.Checked += WidgetVisibilityChanged;
                toggle.Unchecked += WidgetVisibilityChanged;
                _widgetToggles.Children.Add(toggle);
            }
            // Include widgets hidden on a previous launch so they can be restored.
            foreach (string id in DefaultOrder.Where(id => !_visible.Contains(id)))
            {
                var toggle = new CheckBox { Content = DashboardTitle(id), IsChecked = false, Margin = new Thickness(0, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center, Tag = id };
                toggle.Checked += WidgetVisibilityChanged;
                toggle.Unchecked += WidgetVisibilityChanged;
                _widgetToggles.Children.Add(toggle);
            }
            Rebuild();

            var controls = new StackPanel();
            controls.Visibility = Visibility.Collapsed;
            controls.Children.Add(ViewUi.Text("Выбирай карточки и перетаскивай их за заголовок. Расположение сохраняется."));
            controls.Children.Add(_widgetToggles);
            controls.Children.Add(ViewUi.Button("Сбросить расположение", ResetPositions));
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 2, 10) };
            var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var configure = ViewUi.Button("＋  Виджеты", () => controls.Visibility = controls.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
            tools.Children.Add(configure);
            tools.Children.Add(ViewUi.Button("PC RANK  ✦", () => (Window.GetWindow(this) as MainWindow)?.NavigateTo("PC RANK")));
            tools.Children.Add(ViewUi.Button("Игры  ↗", () => (Window.GetWindow(this) as MainWindow)?.NavigateTo("Gaming")));
            var benchmark = ViewUi.Button("Проверить ПК  ↗", () => (Window.GetWindow(this) as MainWindow)?.NavigateTo("Benchmark"));
            benchmark.SetResourceReference(Button.BackgroundProperty, "Accent"); benchmark.SetResourceReference(Button.ForegroundProperty, "AccentInk"); tools.Children.Add(benchmark);
            DockPanel.SetDock(tools, Dock.Right); toolbar.Children.Add(tools);
            var workspaceCaption = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            workspaceCaption.Children.Add(new TextBlock { Text = "Моё пространство", FontSize = 17, FontWeight = FontWeights.SemiBold });
            toolbar.Children.Add(workspaceCaption);
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.Children.Add(header); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar); Grid.SetRow(controls, 2); root.Children.Add(controls);
            _workspace.Content = _grid; Grid.SetRow(_workspace, 3); root.Children.Add(_workspace);
            Content = root;
            SizeChanged += (s, e) => UpdateWorkspaceSize();
            Loaded += (s, e) => { Appearance.Changed += UpdateGlass; UpdateGlass(); Refresh(); };
            Unloaded += (s, e) => { Appearance.Changed -= UpdateGlass; EndDrag(); };
            UpdateGlass();
            Refresh();
        }

        private void AddPulse(StackPanel parent, string label, MetricHistory history, Func<Sample, double> read, Color color)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 27 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(53) });
            var name = ViewUi.Text(label); name.FontSize = 11; name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name);
            var graph = new LineGraph { Source = history, Height = 25, MaxValue = 100, StrokeColor = color }; _graphs.Add(graph); _graphColors[graph] = color; Grid.SetColumn(graph, 1); row.Children.Add(graph);
            var value = ViewUi.Text("—"); value.FontFamily = new FontFamily("Consolas"); value.FontSize = 15; value.HorizontalAlignment = HorizontalAlignment.Right; value.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(value, 2); row.Children.Add(value);
            _updates.Add(s => value.Text = Fmt.Pct(read(s))); parent.Children.Add(row);
        }

        private void AddMetric(string id, string title, string icon, Color color, MetricHistory history,
            Func<Sample, string> value, Func<Sample, string> detail, double max)
        {
            var valueText = ViewUi.Text("—", "BigValue"); valueText.FontSize = 36; valueText.FontFamily = new FontFamily("Segoe UI Variable Display");
            var detailText = ViewUi.Text("");
            var graph = new LineGraph { Source = history, Height = 42, StrokeColor = color, MaxValue = max, Margin = new Thickness(0, 3, 0, 0) };
            _graphs.Add(graph);
            _graphColors[graph] = color;
            _updates.Add(s => { valueText.Text = value(s); detailText.Text = detail(s); });
            AddSimple(id, title, icon, color, valueText, detailText, graph);
        }

        private void AddSimple(string id, string title, string icon, Color color, params UIElement[] body)
        {
            var header = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 12), ToolTip = "Перетащи карточку в удобное место" };
            var grip = ViewUi.Text("⠿"); grip.FontSize = 18; grip.Opacity = .5; grip.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(grip, Dock.Right); header.Children.Add(grip);
            header.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(7), Width = 30, Height = 30,
                Child = new TextBlock { Text = icon, FontSize = 18, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            header.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            header.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _dragged = id;
                var point = e.GetPosition(_grid);
                _dragOffset = new Point(point.X - Canvas.GetLeft(_cards[id]), point.Y - Canvas.GetTop(_cards[id]));
                Panel.SetZIndex(_cards[id], ++_topLayer);
                header.CaptureMouse(); e.Handled = true;
            };
            header.PreviewMouseMove += (s, e) =>
            {
                if (_dragged != id || !header.IsMouseCaptured) return;
                if (e.LeftButton != MouseButtonState.Pressed) { header.ReleaseMouseCapture(); return; }
                var point = e.GetPosition(_grid);
                MoveCardTo(id, point.X - _dragOffset.X, point.Y - _dragOffset.Y, false);
                e.Handled = true;
            };
            header.PreviewMouseLeftButtonUp += (s, e) => { if (header.IsMouseCaptured) header.ReleaseMouseCapture(); e.Handled = true; };
            header.LostMouseCapture += (s, e) => EndDrag();
            var stack = new StackPanel();
            stack.Children.Add(header);
            if (id == "music")
            {
                var row = new DockPanel(); DockPanel.SetDock(body[0], Dock.Left); row.Children.Add(body[0]);
                var text = new StackPanel(); foreach (var element in body.Skip(1)) text.Children.Add(element);
                row.Children.Add(text); stack.Children.Add(row);
            }
            else foreach (var element in body) stack.Children.Add(element);
            var card = new Border
            {
                Child = stack, Width = 320, Height = 240, Padding = new Thickness(16, 13, 16, 10),
                CornerRadius = new CornerRadius(17),
                BorderThickness = new Thickness(1), Tag = id, ClipToBounds = true
            };
            _cardColors.Add(id, color);
            _cards.Add(id, card);
        }

        private void Rebuild()
        {
            _grid.Children.Clear();
            foreach (string id in _order)
            {
                if (!App.Store.Settings.DashboardPositions.TryGetValue(id, out var position))
                {
                    int index = _order.IndexOf(id);
                    position = new DashboardPosition { X = index % 3 * 332, Y = index / 3 * 252 };
                    App.Store.Settings.DashboardPositions[id] = position;
                }
                Canvas.SetLeft(_cards[id], position.X); Canvas.SetTop(_cards[id], position.Y);
                if (_visible.Contains(id)) _grid.Children.Add(_cards[id]);
            }
            UpdateWorkspaceSize();
        }

        private void UpdateWorkspaceSize()
        {
            _grid.Width = Math.Max(700, _visible.Select(id => Canvas.GetLeft(_cards[id]) + 332).DefaultIfEmpty(0).Max());
            _grid.Height = Math.Max(400, _visible.Select(id => Canvas.GetTop(_cards[id]) + 252).DefaultIfEmpty(0).Max());
        }

        internal void MoveCardTo(string id, double x, double y, bool save = true)
        {
            if (!_cards.ContainsKey(id) || !double.IsFinite(x) || !double.IsFinite(y)) return;
            var position = new DashboardPosition { X = Math.Clamp(x, 0, 10000), Y = Math.Clamp(y, 0, 10000) };
            App.Store.Settings.DashboardPositions[id] = position;
            Canvas.SetLeft(_cards[id], position.X); Canvas.SetTop(_cards[id], position.Y);
            UpdateWorkspaceSize(); if (save) App.Store.Save();
        }

        private void EndDrag()
        {
            if (_dragged == null) return;
            _dragged = null; App.Store.Save();
        }

        private void ResetPositions()
        {
            EndDrag(); App.Store.Settings.DashboardPositions.Clear(); Rebuild(); App.Store.Save();
            _workspace.ScrollToHome();
        }

        private static string DashboardTitle(string id) => id switch
        {
            "cpu" => "Процессор", "gpu" => "Видеокарта", "ram" => "Память", "disk" => "Диски",
            "network" => "Сеть", "game" => "Игра", "music" => "Музыка", "system" => "Система",
            "processes" => "Процессы", "rank" => "MY PC RANK", "pc3d" => "Мой ПК · 3D", _ => id
        };

        private void WidgetVisibilityChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox check || check.Tag is not string id) return;
            if (check.IsChecked == true) _visible.Add(id); else _visible.Remove(id);
            App.Store.Settings.DashboardVisible = _visible.ToArray();
            App.Store.Save();
            Rebuild();
        }

        internal string[] CurrentOrder => _order.ToArray();

        internal void MoveCard(string dragged, string target)
        {
            if (dragged == target || !_order.Contains(dragged) || !_order.Contains(target)) return;
            _order.Remove(dragged);
            _order.Insert(_order.IndexOf(target), dragged);
            var positions = App.Store.Settings.DashboardPositions;
            (positions[dragged], positions[target]) = (positions[target], positions[dragged]);
            App.Store.Settings.DashboardLayout = _order.ToArray();
            App.Store.Save();
            Rebuild();
        }

        private void UpdateGlass()
        {
            var baseColor = (Theme.Brush("PanelBg") as SolidColorBrush)?.Color ?? Color.FromRgb(16, 29, 53);
            bool wallpaper = App.Store.Settings.Theme is "Anime" or "Custom photo";
            var fill = new SolidColorBrush(Color.FromArgb(wallpaper ? (byte)215 : (byte)255,
                baseColor.R, baseColor.G, baseColor.B));
            foreach (var pair in _cards)
            {
                var color = _cardColors[pair.Key];
                pair.Value.Background = new LinearGradientBrush(fill.Color, Color.FromArgb(fill.Color.A,
                    (byte)(baseColor.R * .94 + color.R * .06), (byte)(baseColor.G * .94 + color.G * .06), (byte)(baseColor.B * .94 + color.B * .06)), 35);
                pair.Value.BorderBrush = Theme.Brush("BorderColor");
            }
            foreach (var pair in _graphColors)
            {
                var color = pair.Value;
                pair.Key.StrokeColor = App.Store.Settings.Theme == "Light" ? Color.FromRgb((byte)(color.R * .52), (byte)(color.G * .52), (byte)(color.B * .52)) : color;
                pair.Key.InvalidateVisual();
            }
        }

        public void Refresh()
        {
            _pcModel.Refresh();
            var rank = PassportService.CurrentRank(); _rankEmblem.Rank = rank;
            _rankHeaderEmblem.Rank = rank; _rankHeaderButton.Visibility = rank.Confirmed ? Visibility.Visible : Visibility.Collapsed;
            _rankHeaderText.Text = rank.Confirmed ? rank.Name + "\n" + Math.Floor(rank.Total).ToString("0") + " баллов" : "";
            _rankHeaderButton.ToolTip = rank.Confirmed ? "PC RANK · " + App.Store.Performance.TestedUtc.ToLocalTime().ToString("dd.MM.yyyy") : null;
            _rankName.Text = rank.Confirmed ? rank.Name + " / " + rank.Letter : "ПОЛУЧИТЬ РАНГ";
            _rankName.Foreground = new SolidColorBrush(Burmalk.Controls.RankEmblem.ColorFor(rank.Name));
            _rankDetail.Text = rank.Confirmed ? Math.Floor(rank.Total).ToString("0") + " баллов\n" + App.Store.Performance.TestedUtc.ToLocalTime().ToString("dd.MM.yyyy") : rank.Reason;
            var sample = App.Monitor.LastSample;
            foreach (var update in _updates) update(sample);
            foreach (var graph in _graphs) graph.InvalidateVisual();
            _status.Text = "● LIVE  " + DateTime.Now.ToString("HH:mm:ss");

            var game = GameService.Current;
            _gameValue.Text = game?.Active == true && game.FpsAvailable ? MetricView.Number(game.FpsCurrent, "FPS") : game?.Active == true ? game.GameName : "Нет игры";
            _gameDetail.Text = game?.Active == true
                ? game.GameName + (game.FpsAvailable ? "    ·    AVG " + MetricView.Number(game.FpsAvg, "FPS") + " · " + game.FpsSource : "    ·    Нажми RTSS во вкладке Gaming")
                : "Игра появится здесь автоматически.";

            var hardware = App.Monitor.Hardware.Snapshot;
            _machine.Text = hardware.Cpu.Model.Trim() + "  /  " + App.Monitor.Gpu.Last.Name;
            _machine.ToolTip = _machine.Text;
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
                if (!string.Equals(_artworkPath, track?.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
                {
                    _artworkPath = track?.ThumbnailPath;
                    _musicArtwork.Source = null;
                    _musicArtwork.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(_artworkPath) && System.IO.File.Exists(_artworkPath))
                    {
                        var image = new BitmapImage();
                        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(_artworkPath); image.EndInit(); image.Freeze();
                        _musicArtwork.Source = image;
                        _musicArtwork.Visibility = Visibility.Visible;
                    }
                }
            }
            finally { _loadingMusic = false; _lastMusic = DateTime.UtcNow; }
        }
    }
}
