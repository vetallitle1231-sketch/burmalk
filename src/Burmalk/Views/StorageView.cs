using System.Collections.Generic;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>Storage: per-physical-disk live rates + logical drive space, from StorageSampler.</summary>
    public class StorageView : UserControl, IRefreshable
    {
        private readonly StorageSampler _storage;
        private readonly List<PhysicalDisk> _liveDisks = new List<PhysicalDisk>();
        private readonly List<StorageDrive> _drives = new List<StorageDrive>();
        private readonly StackPanel _diskStack = new StackPanel();
        private readonly StackPanel _driveStack = new StackPanel();
        private DateTime _lastRefresh=DateTime.MinValue;
        private CancellationTokenSource _scan;
        private readonly TextBox _folder = new TextBox { Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        private readonly TextBlock _scanStatus = ViewUi.Text("Choose a folder to find its largest files.");
        private readonly DataGrid _files = new DataGrid { IsReadOnly=true,CanUserAddRows=false,AutoGenerateColumns=true,Height=260 };

        public StorageView()
        {
            Margin = new Thickness(4);
            _storage = App.Monitor != null ? App.Monitor.Storage : null;

            var root = new StackPanel();
            root.Children.Add(Section("PHYSICAL DISKS"));
            root.Children.Add(new Border { Style = Theme.Style("Panel"), Child = _diskStack });
            root.Children.Add(Section("DRIVE SPACE"));
            root.Children.Add(new Border { Style = Theme.Style("Panel"), Child = _driveStack });
            root.Children.Add(ViewUi.Panel("Storage analyzer",ViewUi.Text("Folder path · reads file sizes, skips links and inaccessible folders"),_folder,ViewUi.Button("Analyze folder",()=>Analyze()),ViewUi.Button("Cancel scan",()=>_scan?.Cancel()),_scanStatus,_files));

            Content = root;
            Unloaded += (s,e) => _scan?.Cancel();

            RefreshSpace();
            Refresh();
            // slow refresh of rates (sample once per view refresh tick driven by MainWindow)
        }

        private static TextBlock Section(string title)
        {
            return new TextBlock
            {
                Style = Theme.Style("Title"),
                Text = title,
                Margin = new Thickness(0, 0, 0, 0)
            };
        }

        private void RefreshSpace()
        {
            _driveStack.Children.Clear();
            if (_storage == null)
            {
                _driveStack.Children.Add(Hint("storage sampler unavailable"));
                return;
            }
            bool any = false;
            try
            {
                foreach (var d in _storage.RefreshSpace())
                {
                    any = true;
                    _driveStack.Children.Add(BuildDriveRow(d));
                }
            }
            catch { }
            if (!any) _driveStack.Children.Add(Hint("no logical drives available"));
        }

        private static UIElement Hint(string text)
        {
            return new TextBlock { Style = Theme.Style("SubValue"), Text = text };
        }

        private static UIElement BuildDriveRow(StorageDrive d)
        {
            string free = d.TotalGb > 0 ? (d.FreeGb.ToString() + " GB free") : "";
            string total = d.TotalGb > 0 ? (d.TotalGb.ToString() + " GB") : "";
            string used = d.TotalGb > 0 ? (d.UsedPercent.ToString("0") + " %") : "";
            return new TextBlock
            {
                Style = Theme.Style("SubValue"),
                Text = (string.IsNullOrEmpty(d.Letter) ? "drive" : d.Letter)
                    + (string.IsNullOrEmpty(d.VolumeName) ? "" : "  " + d.VolumeName)
                    + (total.Length > 0 ? "   " + free + " / " + total : "")
                    + (used.Length > 0 ? "   used " + used : "")
                    + (d.MediaKind != null && d.MediaKind != "Unavailable" ? "   " + d.MediaKind : "")
            };
        }

        private void RefreshDisks()
        {
            _diskStack.Children.Clear();
            if (_storage == null || _storage.PhysicalDrives == null || _storage.PhysicalDrives.Count == 0)
            {
                _diskStack.Children.Add(Hint("no physical disks known (WMI probe pending or counters unavailable)"));
                return;
            }
            // Live per-disk read/write is not exposed per-instance by the aggregator counters;
            // report the WMI-known metadata and mark active as aggregate (honest).
            bool any = false;
            foreach (var p in _storage.PhysicalDrives)
            {
                any = true;
                string cap = p.CapacityGb > 0 ? p.CapacityGb.ToString("0.0") + " GB" : "—";
                string kind = p.Kind != null && p.Kind != "—" ? p.Kind : "—";
                _diskStack.Children.Add(new TextBlock
                {
                    Style = Theme.Style("SubValue"),
                    Text = (p.Name ?? "—")
                        + (p.Model != null && p.Model != "—" ? "  " + p.Model : "")
                        + "   " + kind
                        + "   " + cap
                        + "   S.M.A.R.T. " + (p.SmartStatus != null && p.SmartStatus != "—" ? p.SmartStatus : "—")
                });
            }
            if (!any) _diskStack.Children.Add(Hint("no physical disks found"));
        }

        public void Refresh()
        {
            if((DateTime.UtcNow-_lastRefresh).TotalSeconds<15)return;_lastRefresh=DateTime.UtcNow;
            RefreshDisks();
            RefreshSpace();
        }
        private async void Analyze()
        {
            if(_scan!=null)return;
            if(!Directory.Exists(_folder.Text)){_scanStatus.Text="Folder not found.";return;}
            _scan=new CancellationTokenSource();_scanStatus.Text="Scanning…";string path=_folder.Text;
            try { var result=await Task.Run(()=>StorageAnalyzer.Scan(path,_scan.Token));_files.ItemsSource=result.Largest;
                _scanStatus.Text=result.Files+" files · "+result.TotalGiB.ToString("0.00")+" GiB · "+result.Skipped+" inaccessible entries"+(result.Limited?" · stopped at 100,000 files":"");
            }catch(OperationCanceledException){_scanStatus.Text="Scan cancelled.";}catch(Exception ex){_scanStatus.Text="Scan failed: "+ex.Message;}
            finally{_scan.Dispose();_scan=null;}
        }
    }
}

