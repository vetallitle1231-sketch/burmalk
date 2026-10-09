using System.Windows;
using System.Windows.Controls;
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

        public NetworkView()
        {
            Margin = new Thickness(4);
            _net = App.Monitor != null ? App.Monitor.Network : null;

            var root = new StackPanel();

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


