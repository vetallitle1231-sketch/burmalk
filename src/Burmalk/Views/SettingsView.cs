using System;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Services;

namespace Burmalk.Views
{
    public class SettingsView : UserControl
    {
        private readonly TextBox _interval,_history,_endpoint,_refresh,_opacity,_hotkey,_size;
        private readonly CheckBox _overlay,_detect,_cpu,_gpu,_ram,_vram;
        private readonly ComboBox _position;
        private readonly ComboBox _theme;
        private readonly CheckBox _compact, _transparent;
        private readonly Slider _dim;
        private readonly TextBlock _photo=ViewUi.Text("");
        private readonly TextBlock _status=ViewUi.Text("");
        public SettingsView()
        {
            var s=App.Store.Settings; var root=new StackPanel();
            _theme=new ComboBox {ItemsSource=Appearance.Themes,SelectedItem=s.Theme,MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=220};
            _compact=Check("Compact cards",s.CompactLayout);
            _dim=new Slider {Minimum=0.1,Maximum=0.9,Value=s.BackgroundDim,MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,Width=300};
            _photo.Text=string.IsNullOrEmpty(s.BackgroundImage)?"No custom photo selected":System.IO.Path.GetFileName(s.BackgroundImage);
            root.Children.Add(ViewUi.Panel("Appearance",ViewUi.Text("Color theme / wallpaper"),_theme,_compact,ViewUi.Text("Wallpaper dimming"),_dim,ViewUi.Button("Choose photo from PC",ChoosePhoto),_photo,ViewUi.Button("Apply appearance",ApplyAppearance)));
            _interval=Input(s.UpdateIntervalMs.ToString()); _history=Input(s.GraphHistorySeconds.ToString());
            root.Children.Add(ViewUi.Panel("Monitoring",ViewUi.Text("Update interval (300–10000 ms)"),_interval,ViewUi.Text("Graph history (10–3600 seconds)"),_history));
            root.Children.Add(ViewUi.Panel("CPU sensors / FPS permissions",ViewUi.Text("For CPU temperature install the signed PawnIO driver and restart Burmalk as administrator. FPS capture also requires administrator rights. Normal monitoring and appearance work without elevation."),ViewUi.Button("CPU sensor setup instructions",()=>MessageBox.Show("1. Close Burmalk.\n2. Open the Support folder beside Burmalk.exe and install PawnIO_setup.exe (signed edition).\n3. Right-click the Burmalk shortcut and choose Run as administrator.\n\nSupport folder: "+System.IO.Path.Combine(AppContext.BaseDirectory,"Support"),"Burmalk · CPU sensors"))));
            _endpoint=Input(s.AiEndpoint);
            root.Children.Add(ViewUi.Panel("Local AI · Ollama",ViewUi.Text("Server address (HTTP or HTTPS)"),_endpoint,ViewUi.Text("Monitoring uses version and loaded-model endpoints. No inference runs automatically.")));
            _overlay=Check("Enable overlay",s.OverlayEnabled); _detect=Check("Detect recognized games and configured profiles",s.GameDetectionEnabled);
            _refresh=Input(s.OverlayRefreshMs.ToString()); _opacity=Input(s.OverlayOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _hotkey=Input(s.OverlayHotkey); _size=Input(s.OverlayFontSize.ToString());
            _position=new ComboBox {ItemsSource=new[]{"TopLeft","TopRight","BottomLeft","BottomRight"},SelectedItem=s.OverlayPosition};
            _cpu=Check("CPU",s.OverlayShowCpu); _gpu=Check("GPU and temperature",s.OverlayShowGpu); _ram=Check("RAM",s.OverlayShowRam); _vram=Check("VRAM",s.OverlayShowVram);
            _transparent=Check("Transparent overlay · numbers + BURMALK",s.OverlayTransparent);
            root.Children.Add(ViewUi.Panel("FPS overlay",_overlay,_transparent,ViewUi.Text("Position"),_position,ViewUi.Text("Refresh (300–10000 ms)"),_refresh,ViewUi.Text("Opacity (0.2–1.0)"),_opacity,ViewUi.Text("Font size (10–30)"),_size,ViewUi.Text("Global toggle hotkey (F1–F12)"),_hotkey,_cpu,_gpu,_ram,_vram));
            root.Children.Add(ViewUi.Panel("Gaming",_detect,ViewUi.Text("Settings, sessions and profiles are stored in "+App.Store.RootDir)));
            root.Children.Add(ViewUi.Button("Save settings",Save)); root.Children.Add(ViewUi.Button("Preview overlay · 10 seconds",OverlayManager.Preview)); root.Children.Add(_status); Content=root;
        }
        private static TextBox Input(string text)=>new TextBox {Text=text,MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=220};
        private static CheckBox Check(string text,bool value)=>new CheckBox{Content=text,IsChecked=value};
        private void ChoosePhoto()
        {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Images|*.png;*.jpg;*.jpeg;*.bmp",Title="Choose Burmalk wallpaper"};
            if(dialog.ShowDialog()!=true)return;
            try { App.Store.Settings.BackgroundImage=Appearance.ImportPhoto(dialog.FileName,App.Store.RootDir);_photo.Text=System.IO.Path.GetFileName(dialog.FileName);_theme.SelectedItem="Custom photo";ApplyAppearance(); }
            catch(Exception ex) { _status.Text=ex.Message; }
        }
        private void ApplyAppearance()
        {
            var s=App.Store.Settings;s.Theme=_theme.SelectedItem as string??"Dark";s.CompactLayout=_compact.IsChecked==true;s.BackgroundDim=_dim.Value;
            Appearance.Apply(s);App.Store.Save();_status.Text="Appearance saved.";
        }
        private void Save()
        {
            if (!int.TryParse(_interval.Text,out int interval)||interval<300||interval>10000 || !int.TryParse(_history.Text,out int history)||history<10||history>3600 ||
                !int.TryParse(_refresh.Text,out int refresh)||refresh<300||refresh>10000 || !int.TryParse(_size.Text,out int size)||size<10||size>30 ||
                !double.TryParse(_opacity.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double opacity)||!double.IsFinite(opacity)||opacity<0.2||opacity>1 ||
                !Uri.TryCreate(_endpoint.Text.Trim(),UriKind.Absolute,out var uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||!string.IsNullOrEmpty(uri.UserInfo) ||
                !System.Text.RegularExpressions.Regex.IsMatch(_hotkey.Text.Trim(),@"^F([1-9]|1[0-2])$"))
            { _status.Text="Check the ranges, endpoint and F1–F12 hotkey."; return; }
            var s=App.Store.Settings; s.UpdateIntervalMs=interval; s.GraphHistorySeconds=history;s.AiEndpoint=uri.ToString().TrimEnd('/');
            s.OverlayEnabled=_overlay.IsChecked==true;s.GameDetectionEnabled=_detect.IsChecked==true;s.OverlayRefreshMs=refresh;s.OverlayOpacity=opacity;s.OverlayFontSize=size;
            s.OverlayPosition=_position.SelectedItem as string??"TopLeft";s.OverlayHotkey=_hotkey.Text.Trim();
            s.OverlayShowCpu=_cpu.IsChecked==true;s.OverlayShowGpu=_gpu.IsChecked==true;s.OverlayShowRam=_ram.IsChecked==true;s.OverlayShowVram=_vram.IsChecked==true;
            s.OverlayTransparent=_transparent.IsChecked==true;ApplyAppearance();
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

