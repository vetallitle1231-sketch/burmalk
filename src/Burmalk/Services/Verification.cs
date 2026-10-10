using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Burmalk.Models;

namespace Burmalk.Services
{
    public static class Verification
    {
        public static string DirectoryPath;
        public static bool RequireCpuTemperature;
        public static string PhotoPath;
        public static readonly List<string> Pages=new List<string>();
        public static readonly List<string> Failures=new List<string>();
        public static void Capture(Window window,string page)
        {
            if(DirectoryPath==null)return;
            window.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(DirectoryPath,page.Replace(" ","-")+".png")))encoder.Save(file);
            Pages.Add(page);
        }
        private static void Check(bool condition,string name) { if(!condition)Failures.Add(name); }
        private static string PhotoFixture(string folder,Color color)
        {
            Directory.CreateDirectory(folder);string file=Path.Combine(folder,"photo.png");
            byte[] pixels=new byte[8*8*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=color.B;pixels[i+1]=color.G;pixels[i+2]=color.R;pixels[i+3]=255;}
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,pixels,32)));
            using(var stream=File.Create(file))encoder.Save(stream);return file;
        }
        private static bool HasColor(BitmapSource image,Color color)
        {
            if(image==null)return false;var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixel=new byte[4];
            converted.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);return pixel[0]==color.B&&pixel[1]==color.G&&pixel[2]==color.R;
        }
        public static void Interact(UserControl view,string page)
        {
            if(DirectoryPath==null)return;
            if(page=="Benchmark")Click(view,"Run short");
            if(page=="Settings")Click(view,"Save settings");
        }
        private static bool Click(DependencyObject root,string text)
        {
            if(root is Button button && button.Content is string caption && caption.StartsWith(text))
            {button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return true;}
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Click(VisualTreeHelper.GetChild(root,i),text))return true;
            return false;
        }
        private static T Find<T>(DependencyObject root) where T : DependencyObject
        {
            if(root is T found)return found;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=Find<T>(VisualTreeHelper.GetChild(root,i));
                if(child!=null)return child;
            }
            return null;
        }
        public static void Complete()
        {
            if(DirectoryPath==null)return;
            var history=new MetricHistory(3);history.Push(10);history.Push(double.NaN);history.Push(20);history.Push(30);
            var values=new double[3];history.Snapshot(values);
            Check(double.IsNaN(values[0])&&values[1]==20&&values[2]==30,"History must preserve missing intervals and ring order");
            var store=new DataStore(Path.Combine(DirectoryPath,"persistence-test"));store.Load();store.Settings.UpdateIntervalMs=1700;
            store.SaveProfile(new GameProfile{GameName="VerificationProfile",OverlayEnabled=false});
            store.AddSession(new GameSession{GameName="VerificationProfile",StartedUtc=DateTime.UtcNow,GpuTempAvg=double.NaN,CpuTempAvg=App.Monitor.LastSample.CpuTempC});store.Save();
            var read=new DataStore(store.RootDir);read.Load();
            Check(read.Settings.UpdateIntervalMs==1700,"Settings round trip");
            Check(NowPlayingService.SourceName("Spotify.exe")=="Spotify"&&
                  NowPlayingService.SourceName("Chrome._crx_eikjhbkpemcmdeeeamdpkgabmk")=="SoundCloud"&&
                  NowPlayingService.SourceName("Chrome.exe")==null,"Music sources are identified without claiming unrelated browser audio");
            var dashboard=Find<Burmalk.Views.DashboardView>(Application.Current.MainWindow);
            Check(dashboard!=null&&dashboard.CurrentOrder.Length==Burmalk.Views.DashboardView.DefaultOrder.Length,"Dashboard widgets are present");
            if(dashboard!=null)
            {
                dashboard.MoveCard("music","cpu");
                var layoutRead=new DataStore(App.Store.RootDir);layoutRead.Load();
                Check(dashboard.CurrentOrder[0]=="music"&&layoutRead.Settings.DashboardLayout[0]=="music","Moved dashboard widget survives a settings reload");
                dashboard.MoveCard("music","system");
            }
            Check(read.GetProfile("verificationprofile")?.OverlayEnabled==false,"Profile round trip / case-insensitive lookup");
            Check(read.Sessions.Count>0&&double.IsNaN(read.Sessions.Last().GpuTempAvg),"Unavailable session metric round trip");
            Check(read.Sessions.Last().CpuTempAvg.Equals(App.Monitor.LastSample.CpuTempC),"CPU temperature session round trip");
            Check(double.IsNaN(GpuSampler.ParseDouble("N/A"))&&GpuSampler.ParseDouble("42.5")==42.5,"GPU parsing");
            Check(App.Monitor.Hub.CpuPercent.Count>2,"Live sampling advances history");
            var processes=ProcessSnapshot.Capture();
            Check(processes.Count>0&&processes.Any(p=>p.MemoryMb>0),"Process snapshot contains real memory measurements");
            var analysis=StorageAnalyzer.Scan(Path.Combine(DirectoryPath,"persistence-test"),System.Threading.CancellationToken.None);
            Check(analysis.Files>=3&&analysis.Largest.Count>0,"Storage analyzer reads actual saved files");
            Check(File.Exists(Path.Combine(App.Store.RootDir,"settings.json")),"Settings save button creates a real file");
            Check(Burmalk.Views.BenchmarkView.LastReport!=null,"Benchmark completed");
            Check(double.IsFinite(App.Monitor.LastSample.CpuGhz)&&App.Monitor.LastSample.CpuGhz>0,"Real CPU frequency available");
            if(RequireCpuTemperature)Check(double.IsFinite(App.Monitor.LastSample.CpuTempC)&&App.Monitor.LastSample.CpuTempC>0&&App.Monitor.LastSample.CpuTempC<150,"Real CPU temperature available");
            var original=App.Store.Settings.Theme;
            foreach(var theme in Appearance.Themes.Where(t=>t!="Custom photo"))
            {
                App.Store.Settings.Theme=theme;Appearance.Apply(App.Store.Settings);
                Check(Application.Current.TryFindResource("Accent") is SolidColorBrush,"Theme resources: "+theme);
                if(theme=="Anime")Check(Appearance.LoadImage(App.Store.Settings)!=null,"Anime background loads");
                Capture(Application.Current.MainWindow,"Theme-"+theme);
            }
            var picker=new Burmalk.Views.SettingsView();
            picker.SelectPhoto(PhotoFixture(Path.Combine(DirectoryPath,"first-photo"),Colors.Red));
            string first=App.Store.Settings.BackgroundImage;
            picker.SelectPhoto(PhotoFixture(Path.Combine(DirectoryPath,"second-photo"),Colors.Blue));
            Check(App.Store.Settings.BackgroundImage!=first,"Different photos with the same filename must replace the wallpaper");
            var windowLayers=Application.Current.MainWindow.Content as Grid;
            Check(HasColor(windowLayers?.Children.OfType<Image>().FirstOrDefault()?.Source as BitmapSource,Colors.Blue),"Window displays the second selected photo, without stale image content");
            Check(File.Exists(first),"Replacing wallpaper preserves the previous imported image");
            Check((double)Application.Current.FindResource("PanelOpacity")==1,"Wallpaper does not reduce text/graph opacity");
            string second=App.Store.Settings.BackgroundImage;
            try{picker.SelectPhoto(Path.Combine(DirectoryPath,"missing-image.png"));Check(false,"Missing photo must report an error");}catch(FileNotFoundException){}
            Check(App.Store.Settings.BackgroundImage==second,"Failed photo import preserves the current wallpaper");
            if(!string.IsNullOrEmpty(PhotoPath))picker.SelectPhoto(PhotoPath);
            App.Store.Settings.CompactLayout=true;Appearance.Apply(App.Store.Settings);
            Check(Appearance.LoadImage(App.Store.Settings)!=null,"Imported photo loads");Capture(Application.Current.MainWindow,"Theme-custom-photo-compact");
            App.Store.Save();var appearanceRead=new DataStore(App.Store.RootDir);appearanceRead.Load();
            Check(appearanceRead.Settings.Theme=="Custom photo"&&appearanceRead.Settings.CompactLayout&&File.Exists(appearanceRead.Settings.BackgroundImage),"Appearance survives restart");
            App.Store.Settings.Theme=original;App.Store.Settings.CompactLayout=false;Appearance.Apply(App.Store.Settings);
            Check(!string.IsNullOrWhiteSpace(OverlayManager.HotkeyStatus),"Overlay hotkey status is available");
            var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
            File.WriteAllText(Path.Combine(DirectoryPath,"verification.json"),JsonSerializer.Serialize(new{Pages,Failures,Sample=App.Monitor.LastSample,CpuSensorStatus=App.Monitor.CpuSensors.Status,CpuSensors=App.Monitor.CpuSensors.DescribeSensors(),FrequencySource=App.Monitor.CpuSensors.FrequencySource,Benchmark=Burmalk.Views.BenchmarkView.LastReport,Hardware=App.Monitor.Hardware.Snapshot,AI=App.Ai.Status},options));
        }
    }
}

