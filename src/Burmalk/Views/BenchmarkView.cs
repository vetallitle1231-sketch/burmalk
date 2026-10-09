using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Services;

namespace Burmalk.Views
{
    public class BenchmarkView : UserControl,IRefreshable
    {
        private readonly TextBlock _cpu=ViewUi.Text("Not run","BigValue"),_memory=ViewUi.Text("Not run","BigValue"),_write=ViewUi.Text("Not run","BigValue"),_read=ViewUi.Text("Not run","BigValue"),_details=ViewUi.Text(""),_gpu=ViewUi.Text("");
        private readonly Button _start;
        private readonly TextBlock _cpuSensors=ViewUi.Text("");
        public static BenchmarkReport LastReport {get;private set;}
        public bool IsRunning {get;private set;}
        public BenchmarkView()
        {
            _start=ViewUi.Button("Run short CPU / memory / disk benchmark",Run);
            var cards=new System.Windows.Controls.Primitives.UniformGrid {Columns=2};
            cards.Children.Add(ViewUi.Panel("CPU · single-thread integer operations / sec",_cpu,_cpuSensors));
            cards.Children.Add(ViewUi.Panel("Memory · copy throughput",_memory));
            cards.Children.Add(ViewUi.Panel("Disk · write-through, 32 MiB",_write));
            cards.Children.Add(ViewUi.Panel("Disk · buffered read-back, 32 MiB",_read));
            var root=new StackPanel();root.Children.Add(ViewUi.Panel("Manual benchmark",ViewUi.Text("Runs only when requested. CPU: 1.5 seconds, memory: 0.5 seconds, disk: temporary 32 MiB file deleted after validation. Buffered read can include Windows cache and is not raw disk speed. Background activity affects results."),_start,_details));root.Children.Add(cards);root.Children.Add(ViewUi.Panel("GPU · live readings, no synthetic GPU benchmark",_gpu));Content=root;
        }
        public void Refresh(){var s=App.Monitor.LastSample;_cpuSensors.Text="CPU temperature: "+Fmt.C(s.CpuTempC)+" · Clock: "+MetricView.Number(s.CpuGhz*1000,"MHz");}
        private async void Run()
        {
            IsRunning=true;_start.IsEnabled=false;_details.Text="Running…";
            try
            {
                var report=await Task.Run(BenchmarkRunner.Run);LastReport=report;
                _cpu.Text=report.CpuMillionOps.ToString("0.0")+" million / sec";_memory.Text=report.MemoryGiBs.ToString("0.00")+" GiB/s";
                _write.Text=report.DiskWriteMiBs.ToString("0.0")+" MiB/s";_read.Text=report.DiskReadMiBs.ToString("0.0")+" MiB/s";
                _details.Text="Completed in "+report.Seconds.ToString("0.0")+" sec · temporary drive "+report.DiskPath+" · read-back verified · "+DateTime.Now.ToString("HH:mm:ss");
                var s=App.Monitor.LastSample;_gpu.Text=App.Monitor.Gpu.Last.Name+"\nLoad: "+Fmt.Pct(s.GpuPercent)+" · Temperature: "+Fmt.C(s.GpuTempC)+" · Clock: "+MetricView.Number(s.GpuClockMhz,"MHz")+"\nVRAM: "+Fmt.Gb(s.GpuVramUsedGb)+" / "+Fmt.Gb(s.GpuVramTotalGb);
            }
            catch(Exception ex){_details.Text="Benchmark failed: "+ex.Message;if(Verification.DirectoryPath!=null)Verification.Failures.Add("Benchmark: "+ex.Message);}
            finally{IsRunning=false;_start.IsEnabled=true;}
        }
    }
}
