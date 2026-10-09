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
        private readonly UserControl[] _views;
        private DispatcherTimer _selftest;
        private int _selftestStep;
        private int _benchmarkWaitTicks;

        public MainWindow()
        {
            Title = "Burmalk";
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
            var labels = new[] { "Dashboard", "CPU", "GPU", "RAM", "Storage", "Disk activity", "Network", "Processes", "Hardware", "System Health", "Gaming", "Gaming History", "AI Monitor", "Benchmark", "Settings" };
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
                new SettingsView()
            };

            _nav = new ListBox
            {
                Margin = new Thickness(10),
                ItemContainerStyle = app.TryFindResource("NavItem") as Style
            };
            foreach (string label in labels)
                _nav.Items.Add(label);
            _nav.SelectionChanged += (s, e) =>
            {
                if (ReferenceEquals(e.Source, _nav) && _nav.SelectedIndex >= 0)
                    Show(_nav.SelectedIndex);
            };

            var header = new TextBlock
            {
                Style = app.TryFindResource("BigValue") as Style,
                Text = "Burmalk",
                Margin = new Thickness(12, 10, 12, 0)
            };

            var root = new DockPanel();
            var branding = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(12,8,0,0) };
            branding.Children.Add(new Image { Source=new BitmapImage(new Uri("pack://application:,,,/Assets/Burmalk.png")),Width=34,Height=34,Margin=new Thickness(0,0,10,0) });
            header.Margin=new Thickness(0);
            branding.Children.Add(header);
            _cpuTemperature.Margin=new Thickness(22,6,0,0);branding.Children.Add(_cpuTemperature);
            DockPanel.SetDock(branding, Dock.Top);

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _content.Margin = new Thickness(8, 10, 12, 10);
            Grid.SetColumn(_nav, 0);
            Grid.SetColumn(_content, 1);
            host.Children.Add(_nav);
            host.Children.Add(_content);

            var headerSurface=new Border {Child=branding};headerSurface.SetResourceReference(Border.BackgroundProperty,"ChromeBg");DockPanel.SetDock(headerSurface,Dock.Top);
            _nav.SetResourceReference(ListBox.BackgroundProperty,"ChromeBg");
            root.Children.Add(headerSurface);
            root.Children.Add(host);
            var layers=new Grid(); layers.Children.Add(_wallpaper);layers.Children.Add(_shade);layers.Children.Add(root);Content=layers;
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
            };
        }

        private void UpdateAppearance()
        {
            _wallpaper.Source=Appearance.LoadImage(App.Store.Settings);
            _shade.Opacity=_wallpaper.Source==null?0:Math.Clamp(App.Store.Settings.BackgroundDim,0.1,0.9);
        }

        private void Show(int index)
        {
            if (index < 0 || index >= _views.Length) return;
            foreach (UIElement child in _content.Children)
                if (child is ScrollViewer previous) previous.Content = null;
            _content.Children.Clear();
            if (_views[index] is ProcessesView) _content.Children.Add(_views[index]);
            else _content.Children.Add(new ScrollViewer { Content = _views[index], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            try { (_views[index] as IRefreshable)?.Refresh(); }
            catch (Exception ex) { Services.ErrorLog.Write("MainWindow.Show: " + ex.Message); }
            if(SelfTestRequested!="") { UpdateLayout(); Verification.Interact(_views[index],_nav.Items[index].ToString()); }
        }

        private void OnSample(Sample s)
        {
            _cpuTemperature.Text="CPU  "+Fmt.C(s.CpuTempC)+"  ·  "+MetricView.Number(s.CpuGhz*1000,"MHz");
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
                Verification.Capture(this,_nav.Items[_nav.SelectedIndex].ToString());
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
                    if (exit) Close();
                    // "navigate" (no -exit): stop the timer and stay open on the last tab.
                }
            };
            _selftest.Start();
        }
    }
}

