using System;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace Burmalk.Services
{
    public sealed class CpuSensors : IDisposable
    {
        private Computer _computer;
        private string _driverStatus="";
        public string Status {get;private set;}="Initializing CPU sensors…";
        public string FrequencySource {get;private set;}="";
        public string TemperatureSource {get;private set;}="";
        public object DescribeSensors()=>_computer?.Hardware.Select(h=>new {Name=h.Name,Type=h.HardwareType.ToString(),Sensors=h.Sensors.Select(s=>new{Name=s.Name,Type=s.SensorType.ToString(),Value=s.Value}).ToArray()}).ToArray();
        [StructLayout(LayoutKind.Sequential)] private struct PowerInfo {public uint Number,MaxMhz,CurrentMhz,MhzLimit,MaxIdleState,CurrentIdleState;}
        [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level,IntPtr input,uint inputSize,[Out] PowerInfo[] output,uint size);
        public void Open()
        {
            try {_driverStatus=CpuSensorAccess.EnsureDriver();_computer=new Computer {IsCpuEnabled=true};_computer.Open();}
            catch(Exception ex) {Status="CPU sensor access: "+ex.Message;ErrorLog.Write(Status);}
        }
        public (double Temperature,double Ghz) Sample()
        {
            double temperature=double.NaN, mhz=double.NaN;
            FrequencySource="";TemperatureSource="";
            try
            {
                foreach(var hardware in _computer?.Hardware??Array.Empty<IHardware>())
                {
                    if(hardware.HardwareType!=HardwareType.Cpu)continue;
                    hardware.Update();foreach(var child in hardware.SubHardware)child.Update();
                    var sensors=hardware.Sensors.Concat(hardware.SubHardware.SelectMany(x=>x.Sensors)).Where(x=>x.Value.HasValue&&float.IsFinite(x.Value.Value)).ToArray();
                    var temps=sensors.Where(x=>x.SensorType==SensorType.Temperature&&x.Value>0&&x.Value<150)
                        .OrderByDescending(x=>x.Name.Contains("Tctl/Tdie")||x.Name.Contains("Package")?3:x.Name.Contains("Tdie")?2:1).ToArray();
                    if(temps.Length>0)
                    {
                        var package=temps.FirstOrDefault(x=>x.Name.Contains("Package")||x.Name.Contains("Tctl/Tdie")||x.Name.Contains("Tdie"));
                        var chosen=package??temps.OrderByDescending(x=>x.Value.Value).First();
                        temperature=chosen.Value.Value;TemperatureSource=package!=null?chosen.Name:"Core max · "+chosen.Name;
                    }
                    // Core clocks only: do not average in bus, uncore, SoC or memory clocks.
                    var clocks=sensors.Where(x=>x.SensorType==SensorType.Clock&&
                        CpuClockNames.IsCore(x.Name)&&x.Value>0&&x.Value<15000).ToArray();
                    var average=clocks.FirstOrDefault(x=>x.Name=="Cores (Average)");
                    if(clocks.Length>0){mhz=average?.Value??clocks.Average(x=>x.Value.Value);FrequencySource="LibreHardwareMonitor · average core clock";}
                }
            }
            catch(Exception ex) {ErrorLog.Write("CPU sensors: "+ex.Message);}
            if(!double.IsFinite(mhz))
            {
                try {var data=new PowerInfo[Environment.ProcessorCount];if(CallNtPowerInformation(11,IntPtr.Zero,0,data,(uint)(Marshal.SizeOf<PowerInfo>()*data.Length))==0){var clocks=data.Where(x=>x.CurrentMhz>0).ToArray();if(clocks.Length>0){mhz=clocks.Average(x=>(double)x.CurrentMhz);FrequencySource="Windows power API · reported clock (may omit boost)";}}}catch{}
            }
            Status=double.IsFinite(temperature)?"CPU temperature: LibreHardwareMonitor":"CPU temperature unavailable · "+_driverStatus;
            return (temperature,mhz/1000.0);
        }
        public void Dispose(){try{_computer?.Close();}catch{}}
    }
}
