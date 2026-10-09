using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Burmalk.Models;
using PresentMonFps;

namespace Burmalk.Services
{
    public static class GameService
    {
        private static readonly object Gate=new object();
        private static Timer _timer;
        private static bool _running;
        private static int _scanning;
        private static int _fpsSampling;
        private static GameCurrent _current;
        private static readonly HashSet<string> Known=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {"cs2","dota2","valorant-win64-shipping","fortniteclient-win64-shipping","r5apex","rocketleague","gta5","eldenring","cyberpunk2077","witcher3","overwatch","minecraft","starfield","baldursgate3","bg3","bg3_dx11"};
        private static readonly double[] Sums=new double[6];
        private static readonly int[] Counts=new int[6];
        public static GameCurrent Current { get { lock(Gate) { return _current==null?null: new GameCurrent {Active=_current.Active,GameName=_current.GameName,Executable=_current.Executable,Pid=_current.Pid,StartedUtc=_current.StartedUtc,DurationSeconds=_current.DurationSeconds,FpsAvailable=_current.FpsAvailable,FpsCurrent=_current.FpsCurrent,FpsAvg=_current.FpsAvg,FpsMin=_current.FpsMin,FpsMax=_current.FpsMax,CpuPercent=_current.CpuPercent,CpuTempC=_current.CpuTempC,GpuPercent=_current.GpuPercent,GpuTempC=_current.GpuTempC,GpuVramUsedGb=_current.GpuVramUsedGb,RamUsedGb=_current.RamUsedGb}; } } }
        public static void Start() { lock(Gate){if(_running)return;_running=true;_timer=new Timer(Scan,null,0,1000);} }
        public static void Stop() { lock(Gate){_running=false;_timer?.Dispose();_timer=null;FinalizeSession();} }
        public static void EndSession() { lock(Gate){FinalizeSession();} }
        private static void Scan(object state)
        {
            if(Interlocked.CompareExchange(ref _scanning,1,0)!=0)return;
            try
            {
                int pid=0;string name=null,exe=null;
                bool enabled=App.Store.Settings.GameDetectionEnabled;
                var names=new HashSet<string>(Known,StringComparer.OrdinalIgnoreCase);
                foreach(var profile in App.Store.AllProfiles)names.Add(profile.GameName);
                if(enabled) foreach(var p in Process.GetProcesses())
                {
                    using(p) try
                    {
                        if(!names.Contains(p.ProcessName))continue;
                        lock(Gate) if(pid!=0 && (_current==null || p.Id!=_current.Pid))continue;
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        pid=p.Id;name=p.ProcessName;try{exe=p.MainModule?.FileName;}catch{}
                    }catch{}
                }
                lock(Gate)
                {
                    if(!_running)return;
                    if(_current!=null && _current.Pid!=pid)FinalizeSession();
                    if(pid==0)return;
                    if(_current==null){Array.Clear(Sums);Array.Clear(Counts);_current=new GameCurrent{Active=true,GameName=name,Executable=exe,Pid=pid,StartedUtc=DateTime.UtcNow};}
                    if (_current.Pid == pid) _ = SampleFpsAsync(pid);
                    var s=App.Monitor.LastSample;
                    _current.DurationSeconds=(DateTime.UtcNow-_current.StartedUtc).TotalSeconds;
                    _current.CpuPercent=s.CpuPercent;_current.CpuTempC=s.CpuTempC;_current.GpuPercent=s.GpuPercent;_current.GpuTempC=s.GpuTempC;_current.GpuVramUsedGb=s.GpuVramUsedGb;_current.RamUsedGb=s.RamUsedGb;
                    double[] values={s.CpuPercent,s.GpuPercent,s.GpuTempC,s.GpuVramUsedGb,s.RamUsedGb,s.CpuTempC};
                    for(int i=0;i<6;i++)if(double.IsFinite(values[i])){Sums[i]+=values[i];Counts[i]++;}
                }
            }catch(Exception ex){ErrorLog.Write("GameService: "+ex.Message);}finally{Interlocked.Exchange(ref _scanning,0);}
        }
        private static async Task SampleFpsAsync(int pid)
        {
            if (Interlocked.CompareExchange(ref _fpsSampling, 1, 0) != 0 || !FpsInspector.IsAvailable) return;
            try
            {
                var result = await FpsInspector.StartOnceAsync(new FpsRequest((uint)pid));
                if (result == null || result.IsCanceled || !double.IsFinite(result.Fps) || result.Fps <= 0) return;
                lock (Gate)
                {
                    if (_current == null || _current.Pid != pid) return;
                    _current.FpsSamples++;
                    _current.FpsCurrent = result.Fps;
                    _current.FpsAvg += (result.Fps - _current.FpsAvg) / _current.FpsSamples;
                    _current.FpsMin = Math.Min(_current.FpsMin, result.Fps);
                    _current.FpsMax = Math.Max(_current.FpsMax, result.Fps);
                    _current.FpsAvailable = _current.FpsSamples >= 3;
                }
            }
            catch (Exception ex) { ErrorLog.Write("FPS: " + ex.Message); }
            finally { Interlocked.Exchange(ref _fpsSampling, 0); }
        }
        private static double Average(int i)=>Counts[i]>0?Sums[i]/Counts[i]:double.NaN;
        private static void FinalizeSession()
        {
            if(_current==null)return;
            App.Store.AddSession(new GameSession {GameName=_current.GameName,StartedUtc=_current.StartedUtc,DurationSeconds=(DateTime.UtcNow-_current.StartedUtc).TotalSeconds,FpsAvailable=_current.FpsAvailable,FpsAvg=_current.FpsAvailable?_current.FpsAvg:double.NaN,FpsMin=_current.FpsAvailable?_current.FpsMin:double.NaN,FpsMax=_current.FpsAvailable?_current.FpsMax:double.NaN,Fps1Low=double.NaN,Fps01Low=double.NaN,CpuAvgPercent=Average(0),CpuTempAvg=Average(5),GpuAvgPercent=Average(1),GpuTempAvg=Average(2),GpuVramAvgGb=Average(3),RamAvgGb=Average(4)});
            _current=null;
        }
    }
}
