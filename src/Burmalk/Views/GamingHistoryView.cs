using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Models;
using Burmalk.Controls;

namespace Burmalk.Views
{
    public class GamingHistoryView : UserControl,IRefreshable
    {
        private readonly TextBox _filter=new TextBox{Text="",ToolTip="Filter by game name"};
        private readonly DataGrid _grid=new DataGrid{AutoGenerateColumns=true,IsReadOnly=true,CanUserAddRows=false,MinHeight=180};
        private readonly TextBox _game=new TextBox{ToolTip="Executable process name without .exe"};
        private readonly CheckBox _enabled=new CheckBox{Content="Overlay enabled for this game",IsChecked=true};
        private readonly TextBox _refresh=new TextBox{Text="1000"};
        private readonly TextBlock _message=ViewUi.Text("");
        private readonly LineGraph _cpuTrend=new LineGraph{Height=90,MaxValue=100,StrokeColor=ViewUi.Blue,UseMonitoringWindow=false};
        private readonly LineGraph _gpuTrend=new LineGraph{Height=90,MaxValue=100,StrokeColor=ViewUi.Green,UseMonitoringWindow=false};
        private readonly CheckBox _cpu=new CheckBox{Content="CPU",IsChecked=true},_gpu=new CheckBox{Content="GPU",IsChecked=true},_ram=new CheckBox{Content="RAM",IsChecked=true},_vram=new CheckBox{Content="VRAM",IsChecked=true};
        public GamingHistoryView()
        {
            var root=new StackPanel();
            root.Children.Add(ViewUi.Panel("Gaming history",ViewUi.Text("Filter by game"),_filter,_grid,_message));
            root.Children.Add(ViewUi.Panel("Session averages · oldest to newest",ViewUi.Text("CPU utilization (%) · one point per recorded session"),_cpuTrend,ViewUi.Text("GPU utilization (%) · one point per recorded session"),_gpuTrend));
            root.Children.Add(ViewUi.Panel("Game profiles",ViewUi.Text("Process name (for example, cs2). Profiles also enable detection of other games. Position and size are set in RTSS."),_game,_enabled,ViewUi.Text("Refresh interval (300–10000 ms)"),_refresh,_cpu,_gpu,_ram,_vram,ViewUi.Button("Load profile",LoadProfile),ViewUi.Button("Save profile",SaveProfile)));
            _filter.TextChanged+=(s,e)=>Refresh();Content=root;Refresh();
        }
        public void Refresh()
        {
            var sessions=App.Store.SessionSnapshot().Where(s=>(s.GameName??"").IndexOf(_filter.Text,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(s=>s.StartedUtc).ToList();
            var cpu=new MetricHistory(Math.Max(2,sessions.Count));var gpu=new MetricHistory(Math.Max(2,sessions.Count));
            foreach(var session in sessions){cpu.Push(session.CpuAvgPercent);gpu.Push(session.GpuAvgPercent);}_cpuTrend.Source=cpu;_gpuTrend.Source=gpu;
            _grid.ItemsSource=App.Store.SessionSnapshot().Where(s=>(s.GameName??"").IndexOf(_filter.Text,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(s=>s.StartedUtc).Select(s=>new {
                Game=s.GameName,Date=s.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),Duration=Fmt.Dur(s.DurationSeconds),
                AverageFPS=s.FpsAvailable?s.FpsAvg.ToString("0.0"):"Unavailable",MinimumFPS=s.FpsAvailable?s.FpsMin.ToString("0.0"):"Unavailable",MaximumFPS=s.FpsAvailable?s.FpsMax.ToString("0.0"):"Unavailable",
                Low1=s.FpsAvailable?MetricView.Number(s.Fps1Low,"FPS"):"Unavailable",Low01=s.FpsAvailable?MetricView.Number(s.Fps01Low,"FPS"):"Unavailable",
                CPU=Fmt.Pct(s.CpuAvgPercent),CPUtemperature=Fmt.C(s.CpuTempAvg),GPU=Fmt.Pct(s.GpuAvgPercent),GPUtemperature=Fmt.C(s.GpuTempAvg),VRAM=Fmt.Gb(s.GpuVramAvgGb),RAM=Fmt.Gb(s.RamAvgGb)
            }).ToList();
            _message.Text=App.Store.SessionSnapshot().Count==0?"No recorded sessions yet.":"Select a column header to sort sessions.";
        }
        private void LoadProfile()
        {
            var p=App.Store.GetProfile(_game.Text.Trim());if(p==null){_message.Text="Profile not found.";return;}
            _enabled.IsChecked=p.OverlayEnabled;_refresh.Text=p.OverlayRefreshMs.ToString();_cpu.IsChecked=p.ShowCpu;_gpu.IsChecked=p.ShowGpu;_ram.IsChecked=p.ShowRam;_vram.IsChecked=p.ShowVram;
        }
        private void SaveProfile()
        {
            string name=_game.Text.Trim();
            if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\\','/','.'})>=0||!int.TryParse(_refresh.Text,out int ms)||ms<300||ms>10000){_message.Text="Enter a process name and a valid interval.";return;}
            App.Store.SaveProfile(new GameProfile{GameName=name,OverlayEnabled=_enabled.IsChecked==true,OverlayRefreshMs=ms,ShowCpu=_cpu.IsChecked==true,ShowGpu=_gpu.IsChecked==true,ShowRam=_ram.IsChecked==true,ShowVram=_vram.IsChecked==true});
            _message.Text="Profile saved.";
        }
    }
}

