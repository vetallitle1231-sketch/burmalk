using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Burmalk.Models;

namespace Burmalk.Services
{
    public static class OverlayManager
    {
        private static DataStore _store;
        internal static bool Initialized => _store != null;
        private static SystemMonitor _monitor;
        private static DispatcherTimer _timer;
        private static HwndSource _source;
        private static IntPtr _handle;
        private static DateTime _nextContent;
        private static IntPtr _manualTarget;
        private static int _manualPid;
        private static bool _rtssVisible;
        private static readonly OverlayText Text=new OverlayText();
        public static string DisplayStatus {get;private set;}="Overlay is off.";
        internal static string DiagnosticPath;
        private static string _lastDiagnostic;
        private static readonly OverlayVisibility Visibility=new OverlayVisibility();
        public static string HotkeyStatus {get;private set;}="Hotkey is initializing…";
        private const int HotkeyId=4726;
        [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h,int id);
        public static void Initialize(App app,DataStore store,SystemMonitor monitor)
        {
            _store=store;_monitor=monitor;
            _handle=new WindowInteropHelper(app.MainWindow).Handle;
            _source=HwndSource.FromHwnd(_handle);_source?.AddHook(Hook);
            _timer=new DispatcherTimer();_timer.Tick+=Tick;ApplySettings();_timer.Start();
        }
        public static void ApplySettings()
        {
            if(_store==null)return;
            Visibility.Reset();
            if(_rtssVisible)RtssOverlay.Clear();_rtssVisible=false;
            RtssOverlay.Start();
            _manualTarget=IntPtr.Zero;_manualPid=0;_nextContent=DateTime.MinValue;
            _timer.Interval=TimeSpan.FromMilliseconds(150);
            UnregisterHotKey(_handle,HotkeyId);
            HotkeyStatus="Choose a hotkey from F1 to F12.";
            if(int.TryParse((_store.Settings.OverlayHotkey??"F8").Trim().ToUpperInvariant().TrimStart('F'),out int key)&&key>=1&&key<=12)
            {
                if(RegisterHotKey(_handle,HotkeyId,0x4000,(uint)(0x70+key-1)))HotkeyStatus="F"+key+" is ready · RTSS shows the overlay in the active game.";
                else{int error=Marshal.GetLastWin32Error();HotkeyStatus=error==1409?"F"+key+" is used by another app. Choose a different hotkey.":"Hotkey registration failed (Windows error "+error+").";ErrorLog.Write(HotkeyStatus);}
            }
        }
        private static IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)
        {
            if(msg==0x0312&&w.ToInt32()==HotkeyId)
            {
                var game=GameService.Current;var profile=game!=null?_store.GetProfile(game.GameName):null;
                var foreground=OverlayTarget.GetForegroundWindow();
                if(OverlayTarget.IsExternalTarget(foreground))
                {
                    bool wasVisible=_rtssVisible&&OverlayTarget.Pid(foreground)==(_manualPid>0?_manualPid:game?.Pid??0);
                    _manualTarget=foreground;_manualPid=OverlayTarget.Pid(foreground);
                    _store.Settings.OverlayEnabled=Visibility.SetManual(!wasVisible);
                }
                else _store.Settings.OverlayEnabled=Visibility.Toggle(_store.Settings.OverlayEnabled,game?.Active==true,profile?.OverlayEnabled??true,false);
                _store.Save();Tick(null,EventArgs.Empty);handled=true;
            }
            return IntPtr.Zero;
        }
        private static void Tick(object sender,EventArgs args)
        {
            try
            {
                var game=GameService.Current;
                if(_manualPid>0&&game?.Pid!=_manualPid)game=new GameCurrent{Pid=_manualPid,GameName="Manual target"};
                var profile=game!=null?_store.GetProfile(game.GameName):null;
                bool requested=Visibility.IsVisible(_store.Settings.OverlayEnabled,game?.Active==true,profile?.OverlayEnabled??true,false);
                var foreground=OverlayTarget.GetForegroundWindow();
                var target=_manualTarget!=IntPtr.Zero?_manualTarget:OverlayTarget.ForProcess(game?.Pid??0);
                bool valid=OverlayTarget.TryBounds(target,out var area);
                if(_manualTarget!=IntPtr.Zero&&OverlayTarget.Pid(target)!=_manualPid){_manualTarget=IntPtr.Zero;_manualPid=0;valid=false;}
                bool focused=target!=IntPtr.Zero&&OverlayTarget.Pid(foreground)==OverlayTarget.Pid(target);
                bool show=OverlayPlacement.ShouldRender(requested,valid,focused);
                DisplayStatus=!requested?"RTSS overlay is off.":!valid?"RTSS overlay armed · open a game or press F8 inside it.":!show?"RTSS overlay hidden while the game is not active.":"RTSS overlay follows the active game.";
                if(!show){if(_rtssVisible)RtssOverlay.Clear();_rtssVisible=false;WriteDiagnostic(target,foreground,false,area);return;}
                game??=new GameCurrent{GameName="Burmalk · manual overlay"};
                if(DateTime.UtcNow>=_nextContent||!_rtssVisible)
                {
                    var frame=RtssOverlay.ReadFrames(OverlayTarget.Pid(target));
                    _rtssVisible=RtssOverlay.Publish(Text.Build(_store.Settings,profile,game,_monitor.LastSample,frame,DateTime.UtcNow),OverlayTarget.Pid(target));
                    _nextContent=DateTime.UtcNow.AddMilliseconds(Math.Clamp(profile?.OverlayRefreshMs??_store.Settings.OverlayRefreshMs,300,10000));
                }
                DisplayStatus=RtssOverlay.Status;WriteDiagnostic(target,foreground,_rtssVisible,area);
            }catch(Exception ex){ErrorLog.Write("Overlay: "+ex);}
        }
        private static void WriteDiagnostic(IntPtr target,IntPtr foreground,bool visible,OverlayTarget.Bounds area)
        {
            if(DiagnosticPath==null)return;
            var json=System.Text.Json.JsonSerializer.Serialize(new{Visible=visible,TargetPid=OverlayTarget.Pid(target),ForegroundPid=OverlayTarget.Pid(foreground),OverlayPid=Environment.ProcessId,Target=new[]{area.Left,area.Top,area.Right,area.Bottom},HotkeyStatus,DisplayStatus});
            if(json!=_lastDiagnostic){System.IO.File.WriteAllText(DiagnosticPath,json);_lastDiagnostic=json;}
        }
        public static void Shutdown()
        {
            if(_rtssVisible)RtssOverlay.Clear();_rtssVisible=false;
            _timer?.Stop();UnregisterHotKey(_handle,HotkeyId);_source?.RemoveHook(Hook);
        }
    }
}
