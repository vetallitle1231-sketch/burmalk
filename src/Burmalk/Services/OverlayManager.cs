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
        public static void Preview() { _previewUntil=DateTime.UtcNow.AddSeconds(10); Tick(null,EventArgs.Empty); }
        private const int HotkeyId=4726;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
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
            _timer.Interval=TimeSpan.FromMilliseconds(Math.Clamp(_store.Settings.OverlayRefreshMs,300,10000));
            UnregisterHotKey(_handle,HotkeyId);
            if(int.TryParse((_store.Settings.OverlayHotkey??"F8").TrimStart('F'),out int key)&&key>=1&&key<=12)
                if(!RegisterHotKey(_handle,HotkeyId,0x4000,(uint)(0x70+key-1)))ErrorLog.Write("Overlay hotkey already in use.");
        }
        private static IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)
        {
            if(msg==0x0312&&w.ToInt32()==HotkeyId){_store.Settings.OverlayEnabled=!_store.Settings.OverlayEnabled;_store.Save();handled=true;}
            return IntPtr.Zero;
        }
        private static void Tick(object sender,EventArgs args)
        {
            try
            {
                var game=GameService.Current;var profile=game!=null?_store.GetProfile(game.GameName):null;
                bool show=_store.Settings.OverlayEnabled&&game?.Active==true&&(profile==null||profile.OverlayEnabled);
                if(DateTime.UtcNow<_previewUntil){show=true;game=game??new GameCurrent{GameName="Burmalk · preview"};}
                _timer.Interval=TimeSpan.FromMilliseconds(Math.Clamp(profile?.OverlayRefreshMs??_store.Settings.OverlayRefreshMs,300,10000));
                if(!show){_window?.Hide();return;}
                if(_window==null)_window=new OverlayWindow();
                _window.Update(_store.Settings,profile,game,_monitor.LastSample);_window.Show();
            }catch(Exception ex){ErrorLog.Write("Overlay: "+ex);}
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
            var area=SystemParameters.WorkArea;string pos=profile?.OverlayPosition??settings.OverlayPosition;
            Left=pos.Contains("Right")?area.Right-DesiredSize.Width-12:area.Left+12;
            Top=pos.Contains("Bottom")?area.Bottom-DesiredSize.Height-12:area.Top+12;
        }
    }
}

