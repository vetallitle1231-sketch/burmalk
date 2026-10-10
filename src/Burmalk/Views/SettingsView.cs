using System;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Services;

namespace Burmalk.Views
{
    public class SettingsView : UserControl,IRefreshable
    {
        private readonly TextBox _interval,_history,_endpoint,_refresh,_hotkey;
        private readonly CheckBox _overlay,_detect,_cpu,_gpu,_ram,_vram,_sensors;
        private readonly ComboBox _theme;
        private readonly CheckBox _compact;
        private readonly Slider _dim;
        private readonly TextBlock _photo=ViewUi.Text("");
        private readonly Image _photoPreview=new Image {Height=100,MaxWidth=280,Stretch=System.Windows.Media.Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,8)};
        private readonly TextBlock _status=ViewUi.Text("");
        private readonly TextBlock _hotkeyStatus=ViewUi.Text("");
        public SettingsView()
        {
            var s=App.Store.Settings; var root=new StackPanel();
            _theme=new ComboBox {ItemsSource=Appearance.Themes,SelectedItem=s.Theme,MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=220};
            _compact=Check("Compact cards",s.CompactLayout);
            _dim=new Slider {Minimum=0.1,Maximum=0.9,Value=s.BackgroundDim,MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,Width=300};
            _photo.Text=string.IsNullOrEmpty(s.BackgroundImage)?"No custom photo selected":string.IsNullOrEmpty(s.BackgroundImageName)?System.IO.Path.GetFileName(s.BackgroundImage):s.BackgroundImageName;
            UpdatePhotoPreview();
            root.Children.Add(ViewUi.Panel("Appearance",ViewUi.Text("Color theme / wallpaper"),_theme,_compact,ViewUi.Text("Wallpaper dimming"),_dim,ViewUi.Button("Choose photo from PC",ChoosePhoto),_photo,_photoPreview,ViewUi.Button("Remove wallpaper",RemoveWallpaper),ViewUi.Button("Apply appearance",ApplyAppearance)));
            _interval=Input(s.UpdateIntervalMs.ToString()); _history=Input(s.GraphHistorySeconds.ToString());
            _sensors=Check("Enable CPU sensors on startup · Windows permission required",s.CpuSensorsEnabled);
            root.Children.Add(ViewUi.Panel("Monitoring",ViewUi.Text("Update interval (300–10000 ms)"),_interval,ViewUi.Text("Graph history (10–3600 seconds)"),_history));
            root.Children.Add(ViewUi.Panel("CPU sensors / FPS permissions",ViewUi.Text("Click Enable CPU temperature and approve the Windows permission prompt. Burmalk installs or repairs the signed sensor driver when needed. After enabling, future launches request permission automatically. FPS capture also needs this permission."),_sensors,ViewUi.Button("Enable CPU temperature · Windows permission",CpuSensorAccess.Enable)));
            _endpoint=Input(s.AiEndpoint);
            root.Children.Add(ViewUi.Panel("Local AI · Ollama",ViewUi.Text("Server address (HTTP or HTTPS)"),_endpoint,ViewUi.Text("Monitoring uses version and loaded-model endpoints. No inference runs automatically.")));
            _overlay=Check("Show Burmalk overlay through RTSS",s.OverlayEnabled); _detect=Check("Detect recognized games and configured profiles",s.GameDetectionEnabled);
            _refresh=Input(s.OverlayRefreshMs.ToString()); _hotkey=Input(s.OverlayHotkey);
            _cpu=Check("CPU",s.OverlayShowCpu); _gpu=Check("GPU and temperature",s.OverlayShowGpu); _ram=Check("RAM",s.OverlayShowRam); _vram=Check("VRAM",s.OverlayShowVram);
            root.Children.Add(ViewUi.Panel("RTSS",ViewUi.Text("Burmalk sends live figures to RTSS in fullscreen, borderless and windowed games. The game must be supported by RTSS. RTSS controls position, size and color. If the game was open before RTSS, restart the game when convenient."),ViewUi.Button("Start RTSS",RtssOverlay.Start),ViewUi.Button("Get RTSS",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/"){UseShellExecute=true}))));
            root.Children.Add(_hotkeyStatus);Refresh();
            root.Children.Add(ViewUi.Panel("Game overlay",_overlay,ViewUi.Text("Refresh (300–10000 ms)"),_refresh,ViewUi.Text("Global toggle hotkey (F1–F12)"),_hotkey,_cpu,_gpu,_ram,_vram));
            root.Children.Add(ViewUi.Panel("Gaming",_detect,ViewUi.Text("Settings, sessions and profiles are stored in "+App.Store.RootDir)));
            root.Children.Add(ViewUi.Button("Save settings",Save)); root.Children.Add(_status); Content=root;
        }
        private static TextBox Input(string text)=>new TextBox {Text=text,MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=220};
        public void Refresh()=>_hotkeyStatus.Text=OverlayManager.HotkeyStatus+"\n"+OverlayManager.DisplayStatus+"\n"+RtssOverlay.Status;
        private static CheckBox Check(string text,bool value)=>new CheckBox{Content=text,IsChecked=value};
        public void SelectPhoto(string file)
        {
            var imported=Appearance.ImportPhoto(file,App.Store.RootDir);
            App.Store.Settings.BackgroundImage=imported;App.Store.Settings.BackgroundImageName=System.IO.Path.GetFileName(file);_photo.Text=App.Store.Settings.BackgroundImageName;
            _photo.ToolTip=file;_theme.SelectedItem="Custom photo";UpdatePhotoPreview();ApplyAppearance();
        }
        private void UpdatePhotoPreview()=>_photoPreview.Source=Appearance.LoadImage(new Burmalk.Models.AppSettings{Theme="Custom photo",BackgroundImage=App.Store.Settings.BackgroundImage});
        private void RemoveWallpaper(){App.Store.Settings.BackgroundImage="";App.Store.Settings.BackgroundImageName="";_photo.Text="No custom photo selected";_photo.ToolTip=null;_photoPreview.Source=null;_theme.SelectedItem="Dark";ApplyAppearance();}
        private void ChoosePhoto()
        {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif",Title="Choose Burmalk wallpaper"};
            if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
            try { SelectPhoto(dialog.FileName); }
            catch(Exception ex) { _status.Text="Wallpaper unchanged: "+ex.Message;Burmalk.Services.ErrorLog.Write("Wallpaper import: "+ex.Message);MessageBox.Show(Window.GetWindow(this),"Unable to apply the selected picture.\n"+ex.Message,"Burmalk"); }
        }
        private void ApplyAppearance()
        {
            var s=App.Store.Settings;s.Theme=_theme.SelectedItem as string??"Dark";s.CompactLayout=_compact.IsChecked==true;s.BackgroundDim=_dim.Value;
            Appearance.Apply(s);App.Store.Save();_status.Text="Appearance saved.";
        }
        private void Save()
        {
            if (!int.TryParse(_interval.Text,out int interval)||interval<300||interval>10000 || !int.TryParse(_history.Text,out int history)||history<10||history>3600 ||
                !int.TryParse(_refresh.Text,out int refresh)||refresh<300||refresh>10000 ||
                !Uri.TryCreate(_endpoint.Text.Trim(),UriKind.Absolute,out var uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||!string.IsNullOrEmpty(uri.UserInfo) ||
                !System.Text.RegularExpressions.Regex.IsMatch(_hotkey.Text.Trim(),@"^F([1-9]|1[0-2])$"))
            { _status.Text="Check the ranges, endpoint and F1–F12 hotkey."; return; }
            var s=App.Store.Settings; s.UpdateIntervalMs=interval; s.GraphHistorySeconds=history;s.AiEndpoint=uri.ToString().TrimEnd('/');
            s.CpuSensorsEnabled=_sensors.IsChecked==true;
            s.OverlayEnabled=_overlay.IsChecked==true;s.GameDetectionEnabled=_detect.IsChecked==true;s.OverlayRefreshMs=refresh;s.OverlayHotkey=_hotkey.Text.Trim();
            s.OverlayShowCpu=_cpu.IsChecked==true;s.OverlayShowGpu=_gpu.IsChecked==true;s.OverlayShowRam=_ram.IsChecked==true;s.OverlayShowVram=_vram.IsChecked==true;
            ApplyAppearance();
            App.Monitor.SetInterval(interval);App.Ai.SetEndpoint(s.AiEndpoint);OverlayManager.ApplySettings();App.Store.Save();_status.Text="Settings saved.";
        }
    }
    public class AiView : UserControl,IRefreshable
    {
        private readonly TextBlock _info=ViewUi.Text(""),_system=ViewUi.Text("");
        public AiView() { Content=ViewUi.Panel("Local AI monitor",_info,_system,ViewUi.Button("Refresh connection",()=>App.Ai.SetEndpoint(App.Store.Settings.AiEndpoint)));Refresh(); }
        public void Refresh()
        {
            var a=App.Ai.Status;var s=App.Monitor.LastSample;
            _info.Text="Connection: "+a.Status+"\nEndpoint: "+a.Endpoint+"\nLoaded model: "+a.ModelName+"\nModel VRAM: "+(a.ModelName=="Unavailable"?"Unavailable":Fmt.Gb(a.ModelVramGb))+"\nTokens / sec: Unavailable\nGenerated tokens: Unavailable\nRequest latency: Unavailable\nCurrent request: Unavailable";
            _system.Text="\nSystem GPU: "+Fmt.Pct(s.GpuPercent)+"\nSystem VRAM: "+Fmt.Gb(s.GpuVramUsedGb)+"\nSystem RAM: "+Fmt.Gb(s.RamUsedGb);
        }
    }
}

