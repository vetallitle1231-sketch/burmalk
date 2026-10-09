using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>Formatting used across views: NaN => "—", never invented numbers.</summary>
    internal static class Fmt
    {
        public static string Pct(double v)
        {
            if (double.IsNaN(v)) return "Unavailable";
            return v.ToString("0") + " %";
        }

        public static string Gb(double v)
        {
            if (double.IsNaN(v)) return "Unavailable";
            return v.ToString("0.0") + " GiB";
        }

        public static string MB(double v)
        {
            if (double.IsNaN(v)) return "Unavailable";
            return v.ToString("0") + " MiB";
        }

        /// <summary>KB/s => human readable.</summary>
        public static string Rate(double kbps)
        {
            if (double.IsNaN(kbps)) return "Unavailable";
            if (kbps >= 1024 * 1024) return (kbps / (1024.0 * 1024.0)).ToString("0.0") + " GiB/s";
            if (kbps >= 1024) return (kbps / 1024.0).ToString("0.0") + " MiB/s";
            return kbps.ToString("0") + " KiB/s";
        }

        public static string C(double v)
        {
            if (double.IsNaN(v)) return "Unavailable";
            return v.ToString("0") + " °C";
        }

        public static string W(double v)
        {
            if (double.IsNaN(v)) return "Unavailable";
            return v.ToString("0") + " W";
        }

        public static string Dur(double totalSeconds)
        {
            if (totalSeconds <= 0) return "0 sec";
            int s = (int)totalSeconds;
            return (s / 3600).ToString() + ":" +
                   ((s % 3600) / 60).ToString("D2") + ":" +
                   (s % 60).ToString("D2");
        }
    }

    /// <summary>Processes view: read-only task manager + refresh.</summary>
    public class ProcessesView : UserControl, IRefreshable
    {
        private readonly DispatcherTimer _auto;
        private readonly DataGrid _grid;
        private readonly TextBox _search = new TextBox { ToolTip = "Search process name or PID", Width = 240 };
        private List<ProcessRow> _rows = new List<ProcessRow>();
        private bool _busy;

        public ProcessesView()
        {
            Margin = new Thickness(8);

            var root = new DockPanel();

            // toolbar
            var toolbar = new DockPanel();
            var refreshBtn = new Button { Content = "Refresh", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            refreshBtn.Click += (s, e) => Capture();
            DockPanel.SetDock(refreshBtn, Dock.Left);
            var autoCb = new CheckBox { Content = "Auto refresh (2 s)", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            _auto = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(2000) };
            _auto.Tick += (s, e) => Capture();
            autoCb.Checked += (s, e) => { if (_auto != null) _auto.Start(); Capture(); };
            autoCb.Unchecked += (s, e) => { if (_auto != null) _auto.Stop(); };
            toolbar.Children.Add(refreshBtn);
            toolbar.Children.Add(_search);
            _search.TextChanged += (s,e) => Filter();
            toolbar.Children.Add(autoCb);
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Insert(0, toolbar);

            // grid
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = new List<ProcessRow>()
            };
            var pidCol = new DataGridTextColumn { Header = "PID", Width = 80, Binding = new System.Windows.Data.Binding("Pid") };
            var nameCol = new DataGridTextColumn { Header = "Process", Width = 220, Binding = new System.Windows.Data.Binding("Name") };
            var cpuCol = new DataGridTextColumn { Header = "CPU %", SortMemberPath = "CpuPercent", Width = 115, Binding = new System.Windows.Data.Binding("CpuDisplay") };
            var memCol = new DataGridTextColumn { Header = "Memory (MiB)", Width = 130, Binding = new System.Windows.Data.Binding("MemoryMb") {StringFormat="{0:0.0}"} };
            var exeCol = new DataGridTextColumn { Header = "Executable", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 280, Binding = new System.Windows.Data.Binding("Executable") };
            _grid.Columns.Add(pidCol);
            _grid.Columns.Add(nameCol);
            _grid.Columns.Add(cpuCol);
            _grid.Columns.Add(memCol);
            _grid.Columns.Add(exeCol);
            root.Children.Add(_grid);

            Content = root;
            Loaded += (s,e) => { if(autoCb.IsChecked==true) _auto.Start(); Capture(); };
            Unloaded += (s,e) => _auto.Stop();

            // initial capture
            Capture();
        }

        public void Refresh() { if (_rows.Count==0) Capture(); }
        private void Filter()
        {
            var sort=_grid.Items.SortDescriptions.ToArray();
            var view=CollectionViewSource.GetDefaultView(_rows);
            view.SortDescriptions.Clear();foreach(var description in sort)view.SortDescriptions.Add(description);
            view.Filter = o => o is ProcessRow r && (r.Name.IndexOf(_search.Text,StringComparison.OrdinalIgnoreCase)>=0 || r.Pid.ToString().Contains(_search.Text));
            _grid.ItemsSource=view;
        }
        private async void Capture()
        {
            if (_busy) return; _busy=true;
            try
            {
                var rows = await Task.Run(ProcessSnapshot.Capture);
                rows.Sort((a, b) => -a.MemoryMb.CompareTo(b.MemoryMb));
                _rows=rows; Filter();
            }
            catch { /* guard above already logs */ }
            finally { _busy=false; }
        }
    }

    internal static class DataGridCol
    {
        // placeholder to keep namespace tidy; columns use GridLength below.
    }
}



