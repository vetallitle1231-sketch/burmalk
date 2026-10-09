using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Burmalk.Models;
using Burmalk.Views;

namespace Burmalk.Services
{
    public static class OverlayManager
    {
        private static DataStore _store;
        private static SystemMonitor _monitor;
        private static OverlayWindow _window;
        internal static OverlayWindow CurrentWindow => _window;
        private static DispatcherTimer _timer;
        private static HwndSource _source;
        private static IntPtr _handle;
        private static DateTime _previewUntil;
        private static DateTime _nextContent;
        private static IntPtr _manualTarget;
        private static int _manualPid;
        public static string DisplayStatus {get;private set;}="Overlay is off.";
        internal static string DiagnosticPath;
        private static string _lastDiagnostic;
        private static readonly OverlayVisibility Visibility=new OverlayVisibility();
        public static string HotkeyStatus {get;private set;}="Hotkey is initializing…";
        public static void Preview() { _previewUntil=DateTime.UtcNow.AddSeconds(10); Tick(null,EventArgs.Empty); }
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
            _manualTarget=IntPtr.Zero;_manualPid=0;_nextContent=DateTime.MinValue;
            _timer.Interval=TimeSpan.FromMilliseconds(150);
            UnregisterHotKey(_handle,HotkeyId);
            HotkeyStatus="Choose a hotkey from F1 to F12.";
            if(int.TryParse((_store.Settings.OverlayHotkey??"F8").Trim().ToUpperInvariant().TrimStart('F'),out int key)&&key>=1&&key<=12)
            {
                if(RegisterHotKey(_handle,HotkeyId,0x4000,(uint)(0x70+key-1)))HotkeyStatus="F"+key+" is ready · press it while the game is active. The overlay hides when you switch away.";
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
                    bool wasVisible=_window?.IsVisible==true&&OverlayTarget.Pid(foreground)==(_manualPid>0?_manualPid:game?.Pid??0);
                    _manualTarget=foreground;_manualPid=OverlayTarget.Pid(foreground);
                    _store.Settings.OverlayEnabled=Visibility.SetManual(!wasVisible);
                }
                else _store.Settings.OverlayEnabled=Visibility.Toggle(_store.Settings.OverlayEnabled,game?.Active==true,profile?.OverlayEnabled??true,DateTime.UtcNow<_previewUntil);
                _previewUntil=DateTime.MinValue;_store.Save();Tick(null,EventArgs.Empty);handled=true;
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
                bool preview=DateTime.UtcNow<_previewUntil;
                bool requested=Visibility.IsVisible(_store.Settings.OverlayEnabled,game?.Active==true,profile?.OverlayEnabled??true,preview);
                var foreground=OverlayTarget.GetForegroundWindow();
                var target=preview?_handle:_manualTarget!=IntPtr.Zero?_manualTarget:OverlayTarget.ForProcess(game?.Pid??0);
                bool valid=OverlayTarget.TryBounds(target,out var area);
                if(!preview&&_manualTarget!=IntPtr.Zero&&OverlayTarget.Pid(target)!=_manualPid){_manualTarget=IntPtr.Zero;_manualPid=0;valid=false;}
                bool focused=target!=IntPtr.Zero&&OverlayTarget.Pid(foreground)==OverlayTarget.Pid(target);
                bool show=OverlayPlacement.ShouldRender(requested,valid,focused);
                DisplayStatus=!requested?"Overlay is off.":!valid?"Overlay armed · switch to a detected game, or press the hotkey inside your game.":!show?"Overlay hidden while the game is not active.":preview?"Overlay preview · 10 seconds.":"Overlay is following the active game window.";
                if(!show){_window?.Hide();WriteDiagnostic(target,foreground,false,area);return;}
                game??=new GameCurrent{GameName="Burmalk · manual overlay"};
                if(_window==null)_window=new OverlayWindow();
                if(!_window.IsVisible||DateTime.UtcNow>=_nextContent)
                {
                    _window.Update(_store.Settings,profile,game,_monitor.LastSample);
                    _nextContent=DateTime.UtcNow.AddMilliseconds(Math.Clamp(profile?.OverlayRefreshMs??_store.Settings.OverlayRefreshMs,300,10000));
                }
                _window.ShowOver(area,profile?.OverlayPosition??_store.Settings.OverlayPosition);
                WriteDiagnostic(target,foreground,true,area);
            }catch(Exception ex){ErrorLog.Write("Overlay: "+ex);}
        }
        private static void WriteDiagnostic(IntPtr target,IntPtr foreground,bool visible,OverlayTarget.Bounds area)
        {
            if(DiagnosticPath==null)return;
            var overlay=_window==null?IntPtr.Zero:new WindowInteropHelper(_window).Handle;
            OverlayTarget.GetWindowRect(overlay,out var rect);
            var json=System.Text.Json.JsonSerializer.Serialize(new{Visible=visible,TargetPid=OverlayTarget.Pid(target),ForegroundPid=OverlayTarget.Pid(foreground),OverlayPid=Environment.ProcessId,Target=new[]{area.Left,area.Top,area.Right,area.Bottom},Overlay=new[]{rect.Left,rect.Top,rect.Right,rect.Bottom},HotkeyStatus,DisplayStatus});
            if(json!=_lastDiagnostic){System.IO.File.WriteAllText(DiagnosticPath,json);_lastDiagnostic=json;}
        }
        public static void Shutdown()
        {
            _timer?.Stop();UnregisterHotKey(_handle,HotkeyId);_source?.RemoveHook(Hook);_window?.Close();_window=null;
        }
    }
    public class OverlayWindow : Window
    {
        private readonly TextBlock _label=new TextBlock{Foreground=Brushes.White,FontFamily=new FontFamily("Consolas"),Margin=new Thickness(12)};
        private readonly Border _surface;
        private readonly TextBlock _brand=new TextBlock {Text="BURMALK",Foreground=Brushes.White,FontSize=10,FontWeight=FontWeights.Bold,Margin=new Thickness(4,12,8,0),VerticalAlignment=VerticalAlignment.Top};
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr h,int index,int value);
        public OverlayWindow()
        {
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
            SizeToContent=SizeToContent.WidthAndHeight;IsHitTestVisible=false;
            var row=new StackPanel {Orientation=Orientation.Horizontal};row.Children.Add(_label);row.Children.Add(_brand);
            _label.Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=1};
            _brand.Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=1};
            _surface=new Border{Background=Brushes.Transparent,CornerRadius=new CornerRadius(8),Child=row};Content=_surface;
            SourceInitialized+=(s,e)=>{var h=new WindowInteropHelper(this).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x20|0x08000000);};
        }
        public void Update(AppSettings settings,GameProfile profile,GameCurrent game,Sample s)
        {
            Opacity=Math.Clamp(settings.OverlayOpacity,0.2,1);_label.FontSize=Math.Clamp(settings.OverlayFontSize,10,30);
            _surface.Background=settings.OverlayTransparent?Brushes.Transparent:new SolidColorBrush(Color.FromArgb(230,8,15,32));
            var fps = game.FpsAvailable ? game.FpsCurrent.ToString("0.0") : "Unavailable";
            var lines=new List<string>();if(game.FpsAvailable)lines.Add("FPS  "+fps);
            if(profile?.ShowCpu??settings.OverlayShowCpu)lines.Add("CPU  "+Fmt.Pct(s.CpuPercent)+(double.IsFinite(s.CpuTempC)?"  "+Fmt.C(s.CpuTempC):"")+(double.IsFinite(s.CpuGhz)?"  "+MetricView.Number(s.CpuGhz*1000,"MHz"):""));
            if(profile?.ShowGpu??settings.OverlayShowGpu)lines.Add("GPU  "+Fmt.Pct(s.GpuPercent)+"  "+Fmt.C(s.GpuTempC));
            if(profile?.ShowVram??settings.OverlayShowVram)lines.Add("VRAM "+Fmt.Gb(s.GpuVramUsedGb));
            if(profile?.ShowRam??settings.OverlayShowRam)lines.Add("RAM  "+Fmt.Gb(s.RamUsedGb));
            _label.Text=string.Join(Environment.NewLine,lines);Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        }
        internal void ShowOver(OverlayTarget.Bounds area,string position)
        {
            if(!IsVisible)Show();UpdateLayout();
            var handle=new WindowInteropHelper(this).Handle;
            if(!OverlayTarget.GetWindowRect(handle,out var rect))return;
            var point=OverlayPlacement.Corner(area.Left,area.Top,area.Right,area.Bottom,rect.Right-rect.Left,rect.Bottom-rect.Top,position);
            // Screen/device coordinates are used end-to-end, including monitors left of the primary.
            // Refresh topmost placement without activating either the overlay or Burmalk.
            OverlayTarget.SetWindowPos(handle,new IntPtr(-1),point.X,point.Y,0,0,0x0001|0x0010|0x0200);
        }
    }
}

