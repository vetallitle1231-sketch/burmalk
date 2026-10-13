using Burmalk.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Models;

namespace Burmalk.Views
{
    /// <summary>Static hardware details filled from the one-shot HardwareInfoService probe.</summary>
    public class HardwareView : UserControl, IRefreshable
    {
        private readonly TextBlock _cpuName, _cpuSpec;
        private readonly TextBlock _gpuName, _gpuDriver, _gpuVram;
        private readonly TextBlock _ramTitle;
        private readonly StackPanel _ramModules;
        private readonly TextBlock _mbName, _mbBios, _mbChipset;
        private readonly TextBlock _sysOs, _sysUptime;

        public HardwareView()
        {
            Margin = new Thickness(4);
            var snap = Snapshot ?? new HardwareSnapshot();

            _cpuName = Text(snap.Cpu.Manufacturer + (snap.Cpu.Model != "Unavailable" && snap.Cpu.Model != snap.Cpu.Manufacturer
                ? "  " + snap.Cpu.Model : ""));
            _cpuSpec = Text("cores " + (snap.Cpu.Cores > 0 ? snap.Cpu.Cores.ToString() : "—")
                 + " / threads " + (snap.Cpu.Threads > 0 ? snap.Cpu.Threads.ToString() : "—")
                 + (snap.Cpu.BaseGhz > 0 ? "   base " + snap.Cpu.BaseGhz.ToString("0.00") + " GHz" : "")
                 + "   " + snap.Cpu.Architecture);

            _gpuName = Text(snap.Gpu.Name);
            _gpuDriver = Text("driver " + snap.Gpu.DriverVersion
                 + (snap.Gpu.DriverDate != "Unavailable" ? "  (" + snap.Gpu.DriverDate + ")" : ""));
            _gpuVram = Text(double.IsNaN(snap.Gpu.VramTotalGb) ? "VRAM —"
                 : "VRAM " + snap.Gpu.VramTotalGb.ToString("0.0") + " GB");

            _ramTitle = Text((snap.Ram.TotalGb > 0 ? snap.Ram.TotalGb + " GB" : "— total")
                 + (snap.Ram.MaxSpeedMhz > 0 ? "   max " + snap.Ram.MaxSpeedMhz + " MT/s" : ""));
            _ramModules = new StackPanel();
            foreach (var m in snap.Ram.Modules)
            {
                _ramModules.Children.Add(Text((m.CapacityGb > 0 ? m.CapacityGb + " GB" : "—")
                    + (m.SpeedMhz > 0 ? " @ " + m.SpeedMhz + " MT/s" : "")
                    + (m.Manufacturer != "Unavailable" ? "  " + m.Manufacturer : "")
                    + (m.Slot != "Unavailable" ? "  (" + m.Slot + ")" : "")));
            }

            _mbName = Text(snap.Motherboard.Manufacturer
                 + (snap.Motherboard.Model != "Unavailable" ? "  " + snap.Motherboard.Model : ""));
            _mbBios = Text("BIOS " + snap.Motherboard.BiosVersion
                 + (snap.Motherboard.BiosDate != "Unavailable" ? "  (" + snap.Motherboard.BiosDate + ")" : ""));
            _mbChipset = Text("chipset " + snap.Motherboard.Chipset);

            _sysOs = Text(snap.System.OsName
                 + (snap.System.OsVersion != "Unavailable" ? " " + snap.System.OsVersion : "")
                 + (snap.System.OsBuild != "Unavailable" ? "  build " + snap.System.OsBuild : ""));
            _sysUptime = Text(Fmt.Dur(snap.System.UptimeSeconds));
            var sysArch = Text(snap.System.ComputerName + "   " + snap.System.Architecture);

            var root = new StackPanel();
            root.Children.Add(Panel("CPU", new[] { _cpuName, _cpuSpec }));
            root.Children.Add(Panel("GPU", new[] { _gpuName, _gpuDriver, _gpuVram }));
            root.Children.Add(Panel("Memory", new UIElement[] { _ramTitle, _ramModules }));
            root.Children.Add(Panel("Motherboard", new[] { _mbName, _mbBios, _mbChipset }));
            root.Children.Add(Panel("System", new[] { _sysOs, _sysUptime, sysArch }));

            Content = root;
        }

        private static HardwareSnapshot Snapshot
        {
            get
            {
                try
                {
                    if (App.Monitor != null && App.Monitor.Hardware != null && App.Monitor.Hardware.ProbeCompleted)
                        return App.Monitor.Hardware.Snapshot;
                }
                catch { }
                return new HardwareSnapshot();
            }
        }

        private static TextBlock Text(string value)
        {
            return new TextBlock { Style = Theme.Style("SubValue"), Text = value };
        }

        private static Border Panel(string title, UIElement[] lines)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Style = Theme.Style("Title"), Text = title.ToUpperInvariant(), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var line in lines)
                stack.Children.Add(line);
            return new Border { Style = Theme.Style("Panel"), Child = stack };
        }

        public void Refresh()
        {
            var snap=Snapshot;var live=App.Monitor.LastSample;
            _cpuName.Text=snap.Cpu.Manufacturer+" · "+snap.Cpu.Model;
            _cpuSpec.Text=snap.Cpu.Cores+" cores · "+snap.Cpu.Threads+" threads · "+snap.Cpu.Architecture+"\nMaximum reported frequency: "+MetricView.Number(snap.Cpu.BaseGhz,"GHz")+" · Current: "+MetricView.Number(live.CpuGhz,"GHz")+"\nUtilization: "+Fmt.Pct(live.CpuPercent)+" · Temperature: "+Fmt.C(live.CpuTempC);
            var gpu=App.Monitor.Gpu.Last;
            _gpuName.Text=gpu.Name!="Unavailable"?gpu.Name:snap.Gpu.Name;
            _gpuDriver.Text="Driver "+(gpu.DriverVersion!="Unavailable"?gpu.DriverVersion:snap.Gpu.DriverVersion);
            _gpuVram.Text="VRAM установлено "+PassportService.Number(snap.Gpu.VramTotalGb,"GiB")+" · занято "+Fmt.Gb(live.GpuVramUsedGb)+" / "+Fmt.Gb(live.GpuVramTotalGb)+"\nUtilization "+Fmt.Pct(live.GpuPercent)+" · Temperature "+Fmt.C(live.GpuTempC)+"\nGraphics clock "+MetricView.Number(live.GpuClockMhz,"MHz")+" · Power "+Fmt.W(live.GpuPowerW);
            _ramTitle.Text=PassportService.Number(snap.Ram.InstalledGb,"GiB")+" установлено · Windows "+PassportService.Number(snap.Ram.TotalGb,"GiB")+" · Used "+Fmt.Gb(live.RamUsedGb)+" · Available "+Fmt.Gb(snap.Ram.TotalGb>0?snap.Ram.TotalGb-live.RamUsedGb:double.NaN);
            _ramModules.Children.Clear();foreach(var module in snap.Ram.Modules)_ramModules.Children.Add(Text(module.Slot+" · "+module.CapacityGb+" GB · "+module.SpeedMhz+" MT/s · "+module.Manufacturer));
            _mbName.Text=snap.Motherboard.Manufacturer+" · "+snap.Motherboard.Model;
            _mbBios.Text="BIOS "+snap.Motherboard.BiosVersion+" · "+snap.Motherboard.BiosDate;
            _mbChipset.Text="Chipset "+snap.Motherboard.Chipset;
            _sysOs.Text=snap.System.OsName+" · "+snap.System.OsVersion+" · build "+snap.System.OsBuild+"\n"+snap.System.ComputerName+" · "+snap.System.Architecture+"\nDirectX: Unavailable";
            try
            {
                if (App.Monitor != null && App.Monitor.Hardware != null && App.Monitor.Hardware.ProbeCompleted)
                    _sysUptime.Text = "Uptime "+Fmt.Dur(System.Environment.TickCount64/1000.0);
            }
            catch { }
        }
    }
}

