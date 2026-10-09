using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Burmalk.Controls;
using Burmalk.Models;

namespace Burmalk.Views
{
    internal static class ViewUi
    {
        public static TextBlock Text(string text, string style = "SubValue") => new TextBlock { Text = text, Style = Theme.Style(style) };
        public static Border Panel(string title, params UIElement[] children)
        {
            var stack = new StackPanel(); stack.Children.Add(Text(title, "Title"));
            foreach (var child in children) stack.Children.Add(child);
            return new Border { Style = Theme.Style("Panel"), Child = stack };
        }
        public static Button Button(string text, Action action)
        {
            var b = new Button { Content = text }; b.Click += (s,e) => action(); return b;
        }
        public static Color Blue = Color.FromRgb(0x12,0x8A,0xFF);
        public static Color Green = Color.FromRgb(0,0xC8,0xA4);
        public static Color Purple = Color.FromRgb(0xAA,0x4B,0xFF);
        public static Color Gold = Color.FromRgb(0xFF,0xB7,0x35);
    }

    public class MetricView : UserControl, IRefreshable
    {
        private readonly StackPanel _root = new StackPanel();
        private readonly TextBlock _info = ViewUi.Text("Loading hardware…");
        private readonly List<LineGraph> _graphs = new List<LineGraph>();
        private readonly List<Action<Sample>> _updates = new List<Action<Sample>>();
        private readonly string _kind;
        public MetricView(string kind)
        {
            _kind = kind; Content = _root;
            _root.Children.Add(ViewUi.Panel(kind + " · live monitoring", _info));
            var h = App.Monitor.Hub;
            if (kind == "CPU")
            {
                Add("Utilization", h.CpuPercent, s=>Fmt.Pct(s.CpuPercent), "%", 100, ViewUi.Blue);
                Add("Temperature", h.CpuTempC, s=>Fmt.C(s.CpuTempC), "°C", 100, ViewUi.Gold);
                Add("Current frequency · MHz", h.CpuGhz, s=>Number(s.CpuGhz*1000,"MHz"), "GHz", -1, ViewUi.Purple);
            }
            else if (kind == "GPU")
            {
                Add("Utilization", h.GpuPercent, s=>Fmt.Pct(s.GpuPercent), "%",100,ViewUi.Green);
                Add("Temperature",h.GpuTempC,s=>Fmt.C(s.GpuTempC),"°C",100,ViewUi.Gold);
                Add("VRAM",h.GpuVramGb,s=>Fmt.Gb(s.GpuVramUsedGb)+" / "+Fmt.Gb(s.GpuVramTotalGb),"GiB",-1,ViewUi.Purple);
                Add("Graphics clock",h.GpuClockMhz,s=>Number(s.GpuClockMhz,"MHz"),"MHz",-1,ViewUi.Green);
                Add("Power",h.GpuPowerW,s=>Fmt.W(s.GpuPowerW),"W",-1,ViewUi.Gold);
            }
            else if (kind == "RAM") Add("Used memory",h.RamUsedGb,s=>Fmt.Gb(s.RamUsedGb),"GiB",-1,ViewUi.Purple);
            else if (kind == "Disk activity")
            {
                Add("Aggregate activity",h.DiskActivePercent,s=>Fmt.Pct(s.DiskActivePercent),"%",100,ViewUi.Gold);
                Add("Read",h.DiskReadKBps,s=>Fmt.Rate(s.DiskReadKBps),"KiB/s",-1,ViewUi.Green);
                Add("Write",h.DiskWriteKBps,s=>Fmt.Rate(s.DiskWriteKBps),"KiB/s",-1,ViewUi.Gold);
            }
            Refresh();
        }
        public static string Number(double n, string unit) => double.IsFinite(n) ? n.ToString("0.##")+" "+unit : "Unavailable";
        private void Add(string title,MetricHistory history,Func<Sample,string> format,string unit,double max,Color color)
        {
            var value=ViewUi.Text("Unavailable","BigValue");
            var graph=new LineGraph {Source=history,Height=145,ShowAxes=true,Suffix=unit,MaxValue=max,StrokeColor=color};
            _graphs.Add(graph); _updates.Add(s=>value.Text=format(s));
            _root.Children.Add(ViewUi.Panel(title,value,graph));
        }
        public void Refresh()
        {
            var m=App.Monitor; var s=m.LastSample; var hw=m.Hardware.Snapshot;
            if (_kind=="CPU") _info.Text=hw.Cpu.Model+" · "+hw.Cpu.Cores+" cores · "+hw.Cpu.Threads+" threads · "+hw.Cpu.Architecture+"\n"+m.CpuSensors.Status+"\nFrequency: "+m.CpuSensors.FrequencySource;
            else if (_kind=="GPU") _info.Text=m.Gpu.Last.Name+" · driver "+m.Gpu.Last.DriverVersion+" · memory clock "+Number(s.GpuMemClockMhz,"MHz");
            else if (_kind=="RAM") _info.Text="Total "+Fmt.Gb(hw.Ram.TotalGb)+" · Available "+Fmt.Gb(hw.Ram.TotalGb>0 ? hw.Ram.TotalGb-s.RamUsedGb : double.NaN)+" · "+hw.Ram.Modules.Count+" modules · "+(hw.Ram.MaxSpeedMhz>0 ? hw.Ram.MaxSpeedMhz+" MT/s" : "Unavailable");
            else _info.Text="All physical disks · read and write counters · recent history";
            foreach(var update in _updates) update(s);
            foreach(var graph in _graphs) graph.InvalidateVisual();
        }
    }

    public class HealthView : UserControl,IRefreshable
    {
        private readonly TextBlock _text=ViewUi.Text("");
        public HealthView() { Content=ViewUi.Panel("System health",_text); Refresh(); }
        public void Refresh()
        {
            var s=App.Monitor.LastSample; var total=App.Monitor.Hardware.Snapshot.Ram.TotalGb;
            _text.Text="CPU temperature: "+Fmt.C(s.CpuTempC)+"\nGPU temperature: "+Fmt.C(s.GpuTempC)+"\nRAM utilization: "+Fmt.Pct(total>0?s.RamUsedGb/total*100:double.NaN)+"\nNetwork: "+(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()?"Connected":"Disconnected")+"\nDrive SMART: Unavailable\n\n";
            foreach (var d in App.Monitor.Storage.RefreshSpace()) _text.Text+=d.Letter+" · "+d.FreeGb+" GB free · "+(d.UsedPercent>90?"Low free space":"Space available")+"\n";
            _text.Text+="\nUnavailable sensors are not included in a health score.";
        }
    }
}


