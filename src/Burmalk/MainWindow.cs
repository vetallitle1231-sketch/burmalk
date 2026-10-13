using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Burmalk.Models;
using Burmalk.Services;
using Burmalk.Views;

namespace Burmalk
{
    /// <summary>
    /// Code-first main window: left navigation rail + a host for the tab views.
    /// No XAML. Drives refresh from SystemMonitor.SampleUpdated.
    /// Honors SelfTestRequested (set by App before Show) for automated verification:
    ///   "navigate"       -> walk every tab once, then stay open.
    ///   "navigate-exit"  -> walk every tab once, then close (last window => app exits).
    /// </summary>
    public class MainWindow : Window
    {
        public string SelfTestRequested = "";

        private readonly ListBox _nav;
        private readonly Grid _content = new Grid();
        private readonly Image _wallpaper = new Image {Stretch=Stretch.UniformToFill,IsHitTestVisible=false};
        private readonly Border _shade = new Border {Background=Brushes.Black,IsHitTestVisible=false};
        private readonly TextBlock _cpuTemperature=ViewUi.Text("CPU temperature: initializing…");
        private readonly TextBlock _pageTitle = ViewUi.Text("Обзор");
        private readonly Button _startupButton = new Button { Margin = new Thickness(10, 0, 10, 10), Padding = new Thickness(8), HorizontalContentAlignment = HorizontalAlignment.Left };
        private readonly UserControl[] _views;
        private DispatcherTimer _selftest;
        private int _selftestStep;
        private int _benchmarkWaitTicks;

        public MainWindow()
        {
            Title = "Burmalk " + App.Version + (App.IsExperimentalProfile ? " · экспериментальная" : "");
            Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Burmalk.ico"));
            Width = 1440;
            Height = 940;
            MinWidth = 1000;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var app = Application.Current;
            SetResourceReference(BackgroundProperty,"BgDark");

            // Build the tab views once; keep one instance per tab (cheap, and they
            // hold no heavyweight state beyond the shared monitor buffers).
            var labels = new[] { "Dashboard", "CPU", "GPU", "RAM", "Storage", "Disk activity", "Network", "Processes", "Hardware", "System Health", "Gaming", "Gaming History", "AI Monitor", "Benchmark", "Settings", "OVERCLOCK", "3D PC VIEW", "PC PASSPORT", "PC RANK" };
            _views = new UserControl[]
            {
                new DashboardView(),
                new MetricView("CPU"),
                new MetricView("GPU"),
                new MetricView("RAM"),
                new StorageView(),
                new MetricView("Disk activity"),
                new NetworkView(),
                new ProcessesView(),
                new HardwareView(),
                new HealthView(),
                new GamesView(),
                new GamingHistoryView(),
                new AiView(),
                new BenchmarkView(),
                new SettingsView(),
                new OverclockView(), new PcView3D(), new PassportView(), new PcRankView()
            };

            _nav = new ListBox
            {
                Margin = new Thickness(14, 8, 14, 8),
                ItemContainerStyle = app.TryFindResource("NavItem") as Style
            };
            var captions = new[] { "Обзор", "Процессор", "Видеокарта", "Оперативная память", "Хранилище", "Скорость дисков", "Сеть", "Процессы", "Комплектующие", "Состояние ПК", "Игровая библиотека", "История игр", "AI Monitor", "Бенчмарк", "Настройки", "OVERCLOCK", "3D PC VIEW", "PC PASSPORT", "PC RANK" };
            var glyphs = new[] { "\uE80F", "\uE950", "\uE7F4", "\uE964", "\uEDA2", "\uE9D9", "\uE701", "\uE9D9", "\uE770", "\uE95E", "\uE7FC", "\uE81C", "\uE99A", "\uE945", "\uE713", "\uE945", "\uE7F8", "\uE8A5", "\uE734" };
            for (int i = 0; i < labels.Length; i++)
                _nav.Items.Add(new ListBoxItem { Tag = labels[i], ToolTip = captions[i], Content = BrandVisuals.Navigation(glyphs[i], captions[i]),
                    Style = app.TryFindResource(labels[i] == "OVERCLOCK" ? "OverclockNavItem" : "NavItem") as Style });
            _nav.SelectionChanged += (s, e) =>
            {
                if (ReferenceEquals(e.Source, _nav) && _nav.SelectedIndex >= 0)
                    Show(_nav.SelectedIndex);
            };

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _content.Margin = new Thickness(22, 18, 16, 16);
            var navigation = new Grid();
            navigation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            navigation.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            navigation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var brand = new DockPanel { Margin = new Thickness(22, 24, 14, 18) };
            var mark = BrandVisuals.Mark(); mark.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(mark, Dock.Left); brand.Children.Add(mark);
            var wordmark = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            wordmark.Children.Add(new TextBlock { Text = "BURMALK", FontSize = 20, FontWeight = FontWeights.Bold });
            var tagline = ViewUi.Text("Пульс твоего ПК"); tagline.FontSize = 10; wordmark.Children.Add(tagline); brand.Children.Add(wordmark);
            navigation.Children.Add(brand);
            Grid.SetRow(_nav, 1);
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
            var edition = BrandVisuals.Badge((App.IsExperimentalProfile ? "EXPERIMENTAL  /  " : "BURMALK  /  ") + App.Version); edition.Margin = new Thickness(22, 0, 0, 12); footer.Children.Add(edition);
            footer.Children.Add(_startupButton); Grid.SetRow(footer, 2);
            _startupButton.Click += ToggleStartup;
            navigation.Children.Add(_nav);
            navigation.Children.Add(footer);
            var rail = new Border { Child = navigation, BorderThickness = new Thickness(0, 0, 1, 0) };
            rail.SetResourceReference(Border.BackgroundProperty, "ChromeBg"); rail.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
            host.Children.Add(rail);
            var workspace = new DockPanel(); Grid.SetColumn(workspace, 1);
            var topbar = new DockPanel { Margin = new Thickness(24, 0, 24, 0), Height = 57 };
            _cpuTemperature.VerticalAlignment = VerticalAlignment.Center; _cpuTemperature.Text = "Датчики подключаются…";
            DockPanel.SetDock(_cpuTemperature, Dock.Right); topbar.Children.Add(_cpuTemperature);
            _pageTitle.VerticalAlignment = VerticalAlignment.Center; _pageTitle.FontSize = 13; _pageTitle.FontWeight = FontWeights.SemiBold; topbar.Children.Add(_pageTitle);
            var topSurface = new Border { Child = topbar, BorderThickness = new Thickness(0, 0, 0, 1) };
            topSurface.SetResourceReference(Border.BorderBrushProperty, "BorderColor"); DockPanel.SetDock(topSurface, Dock.Top);
            workspace.Children.Add(topSurface); workspace.Children.Add(_content); host.Children.Add(workspace);
            UpdateStartupButton();
            var layers=new Grid(); layers.Children.Add(_wallpaper);layers.Children.Add(_shade);layers.Children.Add(host);Content=layers;
            Appearance.Changed+=UpdateAppearance;UpdateAppearance();

            // Live refresh hook — SampleUpdated is raised on the dispatcher thread.
            if (App.Monitor != null)
                App.Monitor.SampleUpdated += OnSample;

            Loaded += (s, e) =>
            {
                _nav.SelectedIndex = 0;
                if (SelfTestRequested.StartsWith("navigate", StringComparison.Ordinal))
                    StartSelfTest();
            };

            Closed += (s, e) =>
            {
                Appearance.Changed-=UpdateAppearance;
                try { if (App.Monitor != null) App.Monitor.SampleUpdated -= OnSample; }
                catch { }
                try { if (_selftest != null) _selftest.Stop(); }
                catch { }
                foreach (var view in _views) if (view is PcRankView rank) rank.Cancel();
            };
        }

        private void UpdateAppearance()
        {
            _wallpaper.Source=Appearance.LoadImage(App.Store.Settings);
            _shade.Opacity=_wallpaper.Source==null?0:Math.Clamp(App.Store.Settings.BackgroundDim,0.1,0.9);
        }

        private void ToggleStartup(object sender, RoutedEventArgs e)
        {
            bool enabled = !App.Store.Settings.StartWithWindows;
            try
            {
                StartupService.Apply(enabled, App.IsExperimentalProfile);
                App.Store.Settings.StartWithWindows = enabled;
                App.Store.Save();
                UpdateStartupButton();
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось настроить запуск с Windows.\n" + ex.Message, "Burmalk"); }
        }

        private void UpdateStartupButton()
        {
            _startupButton.Content = App.Store.Settings.StartWithWindows ? "▣  Автозапуск включён" : "▣  Запускать с Windows";
            _startupButton.ToolTip = "Переключить автозапуск Burmalk при входе в Windows";
        }

        private void Show(int index)
        {
            if (index < 0 || index >= _views.Length) return;
            _nav.ScrollIntoView(_nav.Items[index]);
            _pageTitle.Text = "CONTROL ROOM   /   " + ((_nav.Items[index] as ListBoxItem)?.ToolTip?.ToString() ?? NavLabel(index));
            foreach (UIElement child in _content.Children)
                if (child is ScrollViewer previous) previous.Content = null;
            _content.Children.Clear();
            if (_views[index] is ProcessesView || _views[index] is DashboardView) _content.Children.Add(_views[index]);
            else _content.Children.Add(new ScrollViewer { Content = _views[index], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            try { (_views[index] as IRefreshable)?.Refresh(); }
            catch (Exception ex) { Services.ErrorLog.Write("MainWindow.Show: " + ex.Message); }
            if(SelfTestRequested!="") { UpdateLayout(); Verification.Interact(_views[index],NavLabel(index)); }
        }

        internal void NavigateTo(string page)
        {
            for (int i = 0; i < _nav.Items.Count; i++) if (NavLabel(i) == page) { _nav.SelectedIndex = i; return; }
        }
        private string NavLabel(int index) => _nav.Items[index] is ListBoxItem item ? item.Tag?.ToString() ?? item.Content.ToString() : _nav.Items[index].ToString();

        private void OnSample(Sample s)
        {
            App.Tuning?.Protect(s.CpuTempC, s.GpuTempC);
            _cpuTemperature.Text=(double.IsFinite(s.CpuTempC)?"CPU  "+Fmt.C(s.CpuTempC):"CPU · датчик температуры недоступен")+"    /    "+MetricView.Number(s.CpuGhz*1000,"MHz");
            _cpuTemperature.ToolTip=App.Monitor.CpuSensors.Status;
            int i = _nav.SelectedIndex;
            if (i < 0 || i >= _views.Length) return;
            try { (_views[i] as IRefreshable)?.Refresh(); }
            catch (Exception ex) { Services.ErrorLog.Write("MainWindow.OnSample: " + ex.Message); }
        }

        private void StartSelfTest()
        {
            _selftestStep = 0;
            _selftest = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _selftest.Tick += (s, e) =>
            {
                if(_nav.SelectedIndex==13 && _views[13] is BenchmarkView benchmark && benchmark.IsRunning && _benchmarkWaitTicks++<20)return;
                Verification.Capture(this,NavLabel(_nav.SelectedIndex));
                _selftestStep++;
                if (_selftestStep < _views.Length)
                {
                    _nav.SelectedIndex = _selftestStep;
                }
                else
                {
                    bool exit = SelfTestRequested == "navigate-exit";
                    try { _selftest.Stop(); } catch { }
                    _selftest = null;
                    _nav.SelectedIndex=0;
                    Verification.Capture(this,"Dashboard-final");
                    Width=1000;Height=650;
                    Verification.Capture(this,"Dashboard-compact");
                    Verification.Complete();
                    if (Verification.RunPerformance || Verification.RunGpuOnly) _ = VerifyPerformanceAsync(exit);
                    else if (exit) Close();
                    // "navigate" (no -exit): stop the timer and stay open on the last tab.
                }
            };
            _selftest.Start();
        }

        private async System.Threading.Tasks.Task VerifyPerformanceAsync(bool exit)
        {
            try
            {
                NavigateTo("PC RANK"); var rank = (PcRankView)_views[18];
                Burmalk.Models.PerformanceRecord report;
                if (Verification.RunGpuOnly)
                {
                    report = App.Store.Performance;
                    if (report == null) throw new InvalidOperationException("Missing prior system benchmark for GPU-only verification");
                    var gpu = await NativeGpuWindow.RunAsync(null, default, () =>
                    {
                        var sample = App.Monitor.LastSample;
                        return DateTime.UtcNow.Ticks - sample.TimestampUtc < TimeSpan.TicksPerSecond * 15 && (!double.IsFinite(sample.CpuTempC) || sample.CpuTempC < 95) && (!double.IsFinite(sample.GpuTempC) || sample.GpuTempC < 90);
                    });
                    if (!NativeGpuBenchmark.MatchesRenderer(gpu.Renderer, report.GpuModel)) throw new InvalidOperationException("GPU renderer mismatch");
                    report.GpuFps = gpu.Fps; report.GpuCompleted = gpu.Completed; report.GpuSeconds = gpu.Seconds; report.RenderTier = 2;
                    report.GpuEngine = NativeGpuBenchmark.Engine; report.GpuRenderer = gpu.Renderer; App.Store.SavePerformance(report);
                }
                else report = await rank.RunAsync(false);
                Verification.VerifyPerformance(report);
                NavigateTo("PC PASSPORT"); ((PassportView)_views[17]).Generate(); Verification.Capture(this, "Passport-after-benchmark");
                NavigateTo("PC RANK"); Verification.Capture(this, "Rank-after-benchmark");
                rank.Reveal(PassportService.CurrentRank());
                await System.Threading.Tasks.Task.Delay(1800);
                Verification.Capture(this, "Rank-real-reveal"); rank.DismissReveal();
                NavigateTo("Dashboard"); Width = 1440; Height = 940;
                var dashboard = (DashboardView)_views[0]; dashboard.MoveCard("rank", "gpu"); dashboard.Refresh();
                Verification.Capture(this, "Dashboard-confirmed-rank");
            }
            catch (Exception ex) { Verification.Failures.Add("Performance verification: " + ex); }
            finally { NavigateTo("Dashboard"); Verification.Complete(); if (exit) Close(); }
        }
    }
}

