using System.Windows;
using System.Windows.Controls;
using System;
using System.Threading;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>Network: live down/up sparklines + per-adapter names from NetworkSampler.</summary>
    public class NetworkView : UserControl, IRefreshable
    {
        private readonly NetworkSampler _net;
        private readonly StackPanel _adapterStack = new StackPanel();
        private readonly TextBlock _downLabel, _upLabel;
        private Controls.LineGraph _downGraph,_upGraph;
        private System.DateTime _lastAdapters=System.DateTime.MinValue;
        private CancellationTokenSource _speedTest;
        private readonly TextBlock _speedStatus = ViewUi.Text("Нажмите «Проверить скорость», чтобы начать.");
        private readonly TextBlock _speedResult = ViewUi.Text("↓ — Мбит/с     ↑ — Мбит/с", "BigValue");
        private Button _speedStart, _speedStop;
        private readonly ComboBox _speedServer = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };

        public NetworkView()
        {
            Margin = new Thickness(4);
            _net = App.Monitor != null ? App.Monitor.Network : null;

            var root = new StackPanel();
            _speedStart = ViewUi.Button("Проверить скорость интернета", () => RunSpeedTest());
            _speedStop = ViewUi.Button("Остановить тест", () => _speedTest?.Cancel());
            _speedStop.IsEnabled = false;
            var actions = new WrapPanel(); actions.Children.Add(_speedStart); actions.Children.Add(_speedStop);
            foreach (string option in new[] { "Автоматически · с резервным сервером", "Cloudflare", "LibreSpeed · Прага" }) _speedServer.Items.Add(option);
            _speedServer.SelectedIndex = 0;
            root.Children.Add(ViewUi.Panel("СКОРОСТЬ ИНТЕРНЕТА", _speedResult,
                ViewUi.Text("Загрузка, отдача и задержка HTTP. До 128 МиБ трафика на сервер; при переключении на резервный — до 256 МиБ."), _speedServer,
                actions, _speedStatus));
            Unloaded += (s, e) => _speedTest?.Cancel();

            // live rates row
            var hub = (App.Monitor != null) ? App.Monitor.Hub : null;
            var downGraph = new Controls.LineGraph { Height = 80, Suffix = "" };
            var upGraph = new Controls.LineGraph { Height = 80, Suffix = "" };
            _downGraph=downGraph;_upGraph=upGraph;downGraph.Height=145;upGraph.Height=145;
            downGraph.ShowAxes=true;upGraph.ShowAxes=true;downGraph.Suffix="KiB/s";upGraph.Suffix="KiB/s";
            upGraph.StrokeColor=ViewUi.Purple;
            if (hub != null)
            {
                downGraph.Source = hub.NetDownKBps;
                upGraph.Source = hub.NetUpKBps;
            }
            _downLabel = new TextBlock { Style = Theme.Style("BigValue"), Text = "down —" };
            _upLabel = new TextBlock { Style = Theme.Style("BigValue"), Text = "up —" };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(downGraph, 0); Grid.SetRow(downGraph, 0);
            Grid.SetColumn(upGraph, 1); Grid.SetRow(upGraph, 0);
            Grid.SetColumn(_downLabel, 0); Grid.SetRow(_downLabel, 1);
            Grid.SetColumn(_upLabel, 1); Grid.SetRow(_upLabel, 1);
            grid.Children.Add(downGraph);
            grid.Children.Add(upGraph);
            grid.Children.Add(_downLabel);
            grid.Children.Add(_upLabel);

            var livePanel = new Border
            {
                Style = Theme.Style("Panel"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Style = Theme.Style("Title"), Text = "LIVE THROUGHPUT", Margin = new Thickness(0,0,0,6) },
                        grid
                    }
                }
            };
            root.Children.Add(livePanel);

            root.Children.Add(new TextBlock { Style = Theme.Style("Title"), Text = "ADAPTERS", Margin = new Thickness(0,0,0,6) });
            _adapterStack.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(new Border { Style = Theme.Style("Panel"), Child = _adapterStack, Margin = new Thickness(0,0,0,8) });

            Content = root;
            RefreshAdapters();
            Refresh();
        }

        private async void RunSpeedTest()
        {
            if (_speedTest != null) return;
            _speedTest = new CancellationTokenSource();
            _speedStart.IsEnabled = false; _speedStop.IsEnabled = true;
            _speedServer.IsEnabled = false;
            _speedResult.Text = "↓ — Мбит/с     ↑ — Мбит/с";
            try
            {
                var progress = new Progress<InternetSpeedProgress>(p =>
                { if (_speedTest != null) _speedStatus.Text = p.Phase + (p.Mbps > 0 ? "  " + p.Mbps.ToString("0.0") + " Мбит/с" : ""); });
                string server = _speedServer.SelectedIndex == 1 ? "Cloudflare" : _speedServer.SelectedIndex == 2 ? "LibreSpeed" : "Auto";
                var result = await InternetSpeedTest.RunAsync(_speedTest.Token, progress, server: server);
                _speedResult.Text = "↓ " + Rate(result.DownloadMbps) + "     ↑ " + Rate(result.UploadMbps);
                _speedStatus.Text = (result.Warning == null ? "Готово" : "Частично завершено") + " · " + DateTime.Now.ToString("HH:mm") + " · " + result.Server + " · задержка HTTP " + result.LatencyMs.ToString("0") + " мс · джиттер " + result.JitterMs.ToString("0") +
                    " мс\nТрафик: " + (result.TrafficBytes / 1048576.0).ToString("0.0") + " МиБ. Скорость зависит от Wi-Fi, сервера и других загрузок." +
                    (result.Warning == null ? "" : "\n" + result.Warning);
            }
            catch (OperationCanceledException) { _speedStatus.Text = _speedTest.IsCancellationRequested ? "Тест остановлен." : "Истекло время ожидания. Повторите тест."; }
            catch (Exception ex) { _speedStatus.Text = "Не удалось завершить тест: " + ex.Message; }
            finally { _speedTest.Dispose(); _speedTest = null; _speedStart.IsEnabled = _speedServer.IsEnabled = true; _speedStop.IsEnabled = false; }
        }

        private static string Rate(double value) => double.IsFinite(value) ? value.ToString("0.0") + " Мбит/с" : "недоступно";

        private void RefreshAdapters()
        {
            _adapterStack.Children.Clear();
            bool any = false;
            try
            {
                // The network adapter detail is read fresh each refresh via WMI-backed sampler.
                if (_net != null)
                {
                    foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if(adapter.NetworkInterfaceType==System.Net.NetworkInformation.NetworkInterfaceType.Loopback)continue;
                        string name=adapter.Name+" · "+adapter.OperationalStatus;
                        try { foreach(var ip in adapter.GetIPProperties().UnicastAddresses) if(ip.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork) name+=" · "+ip.Address; }catch{}
                        any = true;
                        _adapterStack.Children.Add(new TextBlock
                        {
                            Style = Theme.Style("SubValue"),
                            Text = name
                        });
                    }
                }
            }
            catch { }
            if (!any)
            {
                _adapterStack.Children.Add(new TextBlock
                {
                    Style = Theme.Style("SubValue"),
                    Text = "no operational adapters with traffic detected"
                });
            }
        }

        public void Refresh()
        {
            _downGraph.InvalidateVisual();_upGraph.InvalidateVisual();
            if((System.DateTime.UtcNow-_lastAdapters).TotalSeconds>10){RefreshAdapters();_lastAdapters=System.DateTime.UtcNow;}
            Sample s = App.Monitor != null ? App.Monitor.LastSample : null;
            if (s != null)
            {
                double v1 = s.NetDownKBps;
                double v2 = s.NetUpKBps;
                _downLabel.Text = "down " + Fmt.Rate(v1);
                _upLabel.Text = "up " + Fmt.Rate(v2);
            }
        }
    }
}


