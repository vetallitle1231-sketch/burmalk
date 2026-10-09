using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.Threading.Tasks;
using Burmalk.Services;
using Burmalk.Controls;
using Burmalk.Models;

namespace Burmalk.Views
{
    public class DashboardView : UserControl, IRefreshable
    {
        private readonly List<Action<Sample>> _updates = new List<Action<Sample>>();
        private readonly List<LineGraph> _graphs = new List<LineGraph>();
        private readonly TextBlock _health = ViewUi.Text("");
        private readonly TextBlock _hardware = ViewUi.Text("");
        private readonly TextBlock _clock = ViewUi.Text("", "BigValue");
        private readonly DataGrid _processes = new DataGrid {AutoGenerateColumns=true,CanUserAddRows=false,IsReadOnly=true,Height=220,HeadersVisibility=DataGridHeadersVisibility.Column};
        private DateTime _lastProcesses=DateTime.MinValue;
        private bool _loadingProcesses;
        public DashboardView()
        {
            var root = new StackPanel();
            root.Children.Add(ViewUi.Panel("Welcome to Burmalk", ViewUi.Text("Your PC, in real time. Recent history and live system measurements.")));
            var cards = new Grid();
            for(int i=0;i<4;i++) cards.ColumnDefinitions.Add(new ColumnDefinition());
            AddCard(cards,0,"CPU",App.Monitor.Hub.CpuPercent,ViewUi.Blue,s=>Fmt.Pct(s.CpuPercent),s=>"Temperature  "+Fmt.C(s.CpuTempC)+"\nFrequency  "+MetricView.Number(s.CpuGhz,"GHz"),100);
            AddCard(cards,1,"GPU",App.Monitor.Hub.GpuPercent,ViewUi.Green,s=>Fmt.Pct(s.GpuPercent),s=>"Temperature  "+Fmt.C(s.GpuTempC)+"\nVRAM  "+Fmt.Gb(s.GpuVramUsedGb),100);
            AddCard(cards,2,"RAM",App.Monitor.Hub.RamUsedGb,ViewUi.Purple,s=>Fmt.Gb(s.RamUsedGb),s=>"Available  "+Fmt.Gb(App.Monitor.Hardware.Snapshot.Ram.TotalGb>0?App.Monitor.Hardware.Snapshot.Ram.TotalGb-s.RamUsedGb:double.NaN),-1);
            AddCard(cards,3,"DISK",App.Monitor.Hub.DiskReadKBps,ViewUi.Gold,s=>Fmt.Rate(s.DiskReadKBps),s=>"Read · all disks\nWrite  "+Fmt.Rate(s.DiskWriteKBps),-1);
            root.Children.Add(cards);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(2,GridUnitType.Star)});
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel();
            var combined=new Grid {Height=185};
            var histories=new[]{App.Monitor.Hub.CpuPercent,App.Monitor.Hub.GpuPercent,App.Monitor.Hub.RamPercent};
            var colors=new[]{ViewUi.Blue,ViewUi.Green,ViewUi.Purple};
            for(int i=0;i<3;i++){var g=new LineGraph{Source=histories[i],ShowAxes=true,DrawAxisLabels=i==0,MaxValue=100,Suffix="%",StrokeColor=colors[i]};_graphs.Add(g);combined.Children.Add(g);}
            left.Children.Add(ViewUi.Panel("System utilization",ViewUi.Text("● CPU · blue     ● GPU · green     ● RAM · purple"),combined));
            left.Children.Add(ViewUi.Panel("Process activity · largest memory users",_processes));
            AddChart(left,"Disk · read",App.Monitor.Hub.DiskReadKBps,ViewUi.Green,-1,"KiB/s");
            AddChart(left,"Disk · write",App.Monitor.Hub.DiskWriteKBps,ViewUi.Gold,-1,"KiB/s");
            var right = new StackPanel();
            right.Children.Add(ViewUi.Panel("System overview",_clock,_hardware));
            right.Children.Add(ViewUi.Panel("Temperatures & health",_health));
            AddChart(right,"Network · download",App.Monitor.Hub.NetDownKBps,ViewUi.Blue,-1,"KiB/s");
            AddChart(right,"Network · upload",App.Monitor.Hub.NetUpKBps,ViewUi.Purple,-1,"KiB/s");
            Grid.SetColumn(right,1); columns.Children.Add(left); columns.Children.Add(right); root.Children.Add(columns);
            Content=root; Refresh();
        }
        private void AddCard(Grid grid,int col,string title,MetricHistory h,Color color,Func<Sample,string> value,Func<Sample,string> sub,double max)
        {
            var v=ViewUi.Text("Unavailable","BigValue"); var detail=ViewUi.Text("");
            var graph=new LineGraph {Source=h,Height=58,StrokeColor=color,MaxValue=max};
            var panel=ViewUi.Panel(title,v,detail,graph); Grid.SetColumn(panel,col); grid.Children.Add(panel);
            _graphs.Add(graph); _updates.Add(s=>{v.Text=value(s);detail.Text=sub(s);});
        }
        private void AddChart(StackPanel stack,string title,MetricHistory h,Color color,double max,string unit)
        {
            var graph=new LineGraph {Source=h,Height=125,ShowAxes=true,StrokeColor=color,MaxValue=max,Suffix=unit};
            _graphs.Add(graph); stack.Children.Add(ViewUi.Panel(title,graph));
        }
        public void Refresh()
        {
            var s=App.Monitor.LastSample;
            foreach(var u in _updates) u(s);
            foreach(var g in _graphs) g.InvalidateVisual();
            _clock.Text=DateTime.Now.ToString("HH:mm");
            var hw=App.Monitor.Hardware.Snapshot;
            _hardware.Text=DateTime.Now.ToString("dd.MM.yyyy")+"\n\n"+hw.Cpu.Model+"\n"+App.Monitor.Gpu.Last.Name+"\n"+Fmt.Gb(hw.Ram.TotalGb)+" RAM\n\n"+hw.System.OsName+"\nUptime "+Fmt.Dur(Environment.TickCount64/1000.0);
            _health.Text="CPU  "+Fmt.C(s.CpuTempC)+"\nGPU  "+Fmt.C(s.GpuTempC)+"\nMotherboard  Unavailable\nDrive SMART  Unavailable\n\nNetwork  "+(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()?"Connected":"Disconnected");
            if(IsLoaded&&!_loadingProcesses&&(DateTime.UtcNow-_lastProcesses).TotalSeconds>5)RefreshProcesses();
        }
        private async void RefreshProcesses()
        {
            _loadingProcesses=true;
            try{var rows=await Task.Run(ProcessSnapshot.Capture);_processes.ItemsSource=rows.OrderByDescending(p=>p.MemoryMb).Take(5).Select(p=>new{Process=p.Name,PID=p.Pid,CPU=p.CpuDisplay,Memory=Fmt.MB(p.MemoryMb)}).ToList();_lastProcesses=DateTime.UtcNow;}
            finally{_loadingProcesses=false;}
        }
    }
}


