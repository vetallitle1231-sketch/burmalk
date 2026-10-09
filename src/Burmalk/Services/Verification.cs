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
        public static void Interact(UserControl view,string page)
        {
            if(DirectoryPath==null)return;
            if(page=="Benchmark")Click(view,"Run short");
            if(page=="Settings"){Click(view,"Save settings");Click(view,"Preview overlay");}
        }
        private static bool Click(DependencyObject root,string text)
        {
            if(root is Button button && button.Content is string caption && caption.StartsWith(text))
            {button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return true;}
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Click(VisualTreeHelper.GetChild(root,i),text))return true;
            return false;
        }
        public static void Complete()
        {
            if(DirectoryPath==null)return;
            var history=new MetricHistory(3);history.Push(10);history.Push(double.NaN);history.Push(20);history.Push(30);
            var values=new double[3];history.Snapshot(values);
            Check(double.IsNaN(values[0])&&values[1]==20&&values[2]==30,"History must preserve missing intervals and ring order");
            var store=new DataStore(Path.Combine(DirectoryPath,"persistence-test"));store.Load();store.Settings.UpdateIntervalMs=1700;
            store.SaveProfile(new GameProfile{GameName="VerificationProfile",OverlayEnabled=false,OverlayPosition="BottomRight"});
            store.AddSession(new GameSession{GameName="VerificationProfile",StartedUtc=DateTime.UtcNow,GpuTempAvg=double.NaN,CpuTempAvg=App.Monitor.LastSample.CpuTempC});store.Save();
            var read=new DataStore(store.RootDir);read.Load();
            Check(read.Settings.UpdateIntervalMs==1700,"Settings round trip");
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
            var photo=Appearance.ImportPhoto(Path.Combine(DirectoryPath,"Dashboard-final.png"),App.Store.RootDir);
            App.Store.Settings.BackgroundImage=photo;App.Store.Settings.Theme="Custom photo";App.Store.Settings.CompactLayout=true;Appearance.Apply(App.Store.Settings);
            Check(Appearance.LoadImage(App.Store.Settings)!=null,"Imported photo loads");Capture(Application.Current.MainWindow,"Theme-custom-photo-compact");
            App.Store.Save();var appearanceRead=new DataStore(App.Store.RootDir);appearanceRead.Load();
            Check(appearanceRead.Settings.Theme=="Custom photo"&&appearanceRead.Settings.CompactLayout&&File.Exists(appearanceRead.Settings.BackgroundImage),"Appearance survives restart");
            App.Store.Settings.Theme=original;App.Store.Settings.CompactLayout=false;Appearance.Apply(App.Store.Settings);
            if(OverlayManager.CurrentWindow!=null) Capture(OverlayManager.CurrentWindow,"Overlay-preview");
            else Failures.Add("Overlay preview did not open");
            if(OverlayManager.CurrentWindow?.Content is Border overlaySurface && overlaySurface.Background is SolidColorBrush overlayBrush)
                Check(!App.Store.Settings.OverlayTransparent||overlayBrush.Color.A==0,"Overlay has transparent surface");
            var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
            File.WriteAllText(Path.Combine(DirectoryPath,"verification.json"),JsonSerializer.Serialize(new{Pages,Failures,Sample=App.Monitor.LastSample,CpuSensorStatus=App.Monitor.CpuSensors.Status,CpuSensors=App.Monitor.CpuSensors.DescribeSensors(),FrequencySource=App.Monitor.CpuSensors.FrequencySource,Benchmark=Burmalk.Views.BenchmarkView.LastReport,Hardware=App.Monitor.Hardware.Snapshot,AI=App.Ai.Status},options));
        }
    }
}

