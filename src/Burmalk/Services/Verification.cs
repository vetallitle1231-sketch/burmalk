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
        public static bool RunPerformance;
        public static bool RunGpuOnly;
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
            // Long stress tests are opt-in and must never start during the UI smoke test.
            if(page=="Settings")Click(view,"Save settings");
            if (page == "Dashboard" && view is Burmalk.Views.DashboardView rankedDashboard && App.Store.Performance != null && PassportService.CurrentRank().Confirmed)
            {
                if (Array.IndexOf(rankedDashboard.CurrentOrder, "rank") > Array.IndexOf(rankedDashboard.CurrentOrder, "gpu")) rankedDashboard.MoveCard("rank", "gpu"); rankedDashboard.Refresh();
                Check(ContainsText(view, PassportService.CurrentRank().Name), "Dashboard rank widget displays the persisted real rank");
                Check(!ContainsText(view, "System.Windows.Controls.DockPanel"), "Header rank button renders rich content instead of a type name");
            }
            if (page == "3D PC VIEW" && view is Burmalk.Views.PcView3D pc3d)
            {
                foreach (string part in new[] { "cpu", "gpu", "ram", "board", "disks", "cooling" })
                { pc3d.SelectPart(part); Check(pc3d.SelectedPart == part, "3D component selection: " + part); }
                double before = pc3d.CameraDistance; pc3d.Zoom(-1); Check(pc3d.CameraDistance < before, "3D zoom changes camera distance");
                for (int i = 0; i < 50; i++) pc3d.Zoom(-1);
                Check(pc3d.CameraDistance == 7, "3D camera cannot pass into the chassis"); pc3d.ResetCamera(); pc3d.SelectPart("gpu");
            }
            if (page == "PC PASSPORT" && view is Burmalk.Views.PassportView passport)
            {
                passport.Generate();
                var p = PassportService.Create(true, "Pulse");
                Check(p.OwnerLabel == "ТВОЙ ПК" && p.Sections.Count == 7, "Passport defaults to private, complete hardware categories");
                string serialized = JsonSerializer.Serialize(p, new JsonSerializerOptions { IncludeFields = true });
                Check(!serialized.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase), "Public passport excludes the machine name");
                foreach (string style in new[] { "Pulse", "Aurora", "Graphite" })
                {
                    p.Style = style;
                    var card = PassportExport.Card(p, false); Check(card.PixelWidth == 1600 && card.PixelHeight == 1120, "Passport PNG dimensions: " + style);
                    PassportExport.Png(card, Path.Combine(DirectoryPath, "Passport-" + style + ".png"));
                }
                PassportExport.Png(PassportExport.Card(p, true), Path.Combine(DirectoryPath, "Passport-social.png"));
                PassportExport.Pdf(p, Path.Combine(DirectoryPath, "Passport.pdf"));
                Check(new FileInfo(Path.Combine(DirectoryPath, "Passport.pdf")).Length > 5000, "Passport PDF contains rendered hardware pages");
            }
            if (page == "PC RANK" && view is Burmalk.Views.PcRankView rank)
            {
                Check(!PerformanceTest.Busy, "PC RANK never starts load just by opening its tab");
                bool reduced = App.Store.Settings.ReducedEffects; App.Store.Settings.ReducedEffects = true;
                string[] names = { "BRONZE", "SILVER", "GOLD", "DIAMOND", "LEGENDARY" }, letters = { "D", "C", "B", "A", "S" };
                for (int i = 0; i < names.Length; i++) { rank.Reveal(new PcRankResult { Confirmed = true, Name = names[i], Letter = letters[i], Total = 1000 }); Capture(Application.Current.MainWindow, "Rank-presentation-fixture-" + names[i]); rank.DismissReveal(); }
                App.Store.Settings.ReducedEffects = reduced; rank.Refresh();
                if (App.Store.Performance != null && PassportService.CurrentRank().Confirmed) rank.Reveal(PassportService.CurrentRank());
            }
            if (page == "System Health" && view is Burmalk.Views.HealthView health)
            {
                // Presentation fixtures use synthetic readings and never change hardware.
                foreach (var temperature in new[] { 45, 72, 82, 92 })
                {
                    var fixture = new Sample { TimestampUtc = DateTime.UtcNow.Ticks, CpuTempC = temperature, GpuTempC = 40, RamPercent = 35, GpuVramUsedGb = 3, GpuVramTotalGb = 12 };
                    var report = HealthAssessment.Evaluate(fixture, new[] { new StorageDrive { Letter = "C:", TotalGb = 100, FreeGb = 50, UsedPercent = 50 } }, DateTime.UtcNow.Ticks);
                    health.Render(report); Capture(Application.Current.MainWindow, "Health-" + report.Level);
                }
                health.Refresh();
            }
            if (page == "CPU")
            {
                var fan = Find<Burmalk.Views.CpuFanControlPanel>(view);
                Check(fan != null && Count<Slider>(fan) == 1 && Count<ComboBox>(fan) == 0,
                    "CPU cooling exposes one speed slider and no motherboard channel lists on the main page");
                if (!CpuSensorAccess.IsAdministrator && fan != null)
                    Check(Find<Slider>(fan)?.IsEnabled == false, "Unknown CPU fan cannot be controlled through a guessed channel");
            }
            if (page == "GPU")
            {
                string expected = "Частота ядра GPU: " + Burmalk.Views.MetricView.Number(App.Monitor.LastSample.GpuClockMhz, "MHz");
                Check(ContainsText(view, expected), "GPU header shows graphics clock rather than the memory clock");
            }
            if (page == "OVERCLOCK" && view is Burmalk.Views.OverclockView overclock)
            {
                Check(!overclock.Accepted, "Overclock tab starts behind the consent gate");
                Capture(Application.Current.MainWindow, "OVERCLOCK-warning");
                Click(view, "Я согласен");
                Check(!overclock.Accepted, "Overclock cannot open without a checked consent box");
                var consent = Find<CheckBox>(view);
                if (consent != null) consent.IsChecked = true;
                Click(view, "Я согласен");
                Check(overclock.Accepted, "Consent opens overclock settings without applying hardware changes");
                Capture(Application.Current.MainWindow, "OVERCLOCK-controls");
            }
        }
        private static bool Click(DependencyObject root,string text)
        {
            if(root is Button button && button.Content is string caption && caption.StartsWith(text))
            {button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return true;}
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Click(VisualTreeHelper.GetChild(root,i),text))return true;
            return false;
        }
        private static bool ContainsText(DependencyObject root, string text)
        {
            if (root is TextBlock label && label.Text.Contains(text, StringComparison.Ordinal)) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (ContainsText(VisualTreeHelper.GetChild(root, i), text)) return true;
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
        private static int Count<T>(DependencyObject root) where T : DependencyObject
        {
            int count = root is T ? 1 : 0;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) count += Count<T>(VisualTreeHelper.GetChild(root, i));
            return count;
        }
        public static void Complete()
        {
            if(DirectoryPath==null)return;
            var history=new MetricHistory(3);history.Push(10);history.Push(double.NaN);history.Push(20);history.Push(30);
            var values=new double[3];history.Snapshot(values);
            Check(double.IsNaN(values[0])&&values[1]==20&&values[2]==30,"History must preserve missing intervals and ring order");
            var store=new DataStore(Path.Combine(DirectoryPath,"persistence-test"));store.Load();store.Settings.UpdateIntervalMs=1700;
            store.Settings.DashboardVisible=new[]{"cpu","music"};store.Settings.StartWithWindows=true;store.Settings.GameLibraryPaths=new[]{System.IO.Path.Combine(DirectoryPath,"sample-game.exe")};
            store.Settings.CpuFanBoardId = "board-fixture"; store.Settings.CpuFanControlId = "pwm2"; store.Settings.CpuFanRpmSensorId = "rpm4";
            store.SaveProfile(new GameProfile{GameName="VerificationProfile",OverlayEnabled=false});
            store.AddSession(new GameSession{GameName="VerificationProfile",StartedUtc=DateTime.UtcNow,GpuTempAvg=double.NaN,CpuTempAvg=App.Monitor.LastSample.CpuTempC});store.Save();
            var read=new DataStore(store.RootDir);read.Load();
            var rankFixture = new PerformanceRecord { MethodVersion = RankMethod.Version, TestedUtc = DateTime.UtcNow, CpuModel = "fixture CPU", GpuModel = "fixture GPU", RamGb = 32,
                CpuMillionOps = RankMethod.ReferenceCpu, GpuFps = RankMethod.ReferenceGpu, RamMegabytesPerSecond = RankMethod.ReferenceRam, CpuCompleted = true, GpuCompleted = true, RamCompleted = true, MemoryPassed = true,
                CpuSeconds = 60, GpuSeconds = 120, MemoryBytesVerified = 128L * 1024 * 1024, RenderTier = 2, Status = "Fixture complete" };
            store.SavePerformance(rankFixture); store.Settings.ReducedEffects = true; store.Settings.PassportHidePersonal = true; store.Settings.PassportStyle = "Aurora"; store.Save();
            var persistedRank = new DataStore(store.RootDir); persistedRank.Load();
            Check(RankMethod.Evaluate(persistedRank.Performance).Name == "GOLD", "Confirmed rank survives reopening persisted data");
            Check(persistedRank.Settings.ReducedEffects && persistedRank.Settings.PassportStyle == "Aurora" && persistedRank.Settings.PassportHidePersonal, "Passport privacy, style and reduced effects survive restart");
            var partial = new PerformanceRecord { MethodVersion = RankMethod.Version, CpuModel = "fixture CPU", GpuModel = "fixture GPU", CpuCompleted = true, CpuMillionOps = 2 * RankMethod.ReferenceCpu };
            persistedRank.SaveComponentBenchmark(partial, false);
            Check(RankMethod.Evaluate(persistedRank.Performance).Name == "GOLD", "A separate partial benchmark never replaces the full-suite rank");
            Check(read.Settings.UpdateIntervalMs==1700,"Settings round trip");
            Check(read.Settings.CpuFanBoardId == "board-fixture" && read.Settings.CpuFanControlId == "pwm2" && read.Settings.CpuFanRpmSensorId == "rpm4", "CPU fan PWM and RPM identities survive settings reload without applying a fan speed");
            Check(read.Settings.DashboardVisible.Length==2&&read.Settings.DashboardVisible[1]=="music"&&read.Settings.StartWithWindows&&read.Settings.GameLibraryPaths.Length==1,"Experimental dashboard, startup, and game-list settings round trip");
            string gameRoot=Path.Combine(DirectoryPath,"game-library-fixture","Counter-Strike Global Offensive");
            string gameBin=Path.Combine(gameRoot,"game","bin","win64");Directory.CreateDirectory(gameBin);
            File.WriteAllBytes(Path.Combine(gameBin,"cs2.exe"),new byte[1]);File.WriteAllBytes(Path.Combine(gameBin,"console2.exe"),new byte[2048]);
            Check(Path.GetFileName(GameLibraryService.FindGameExecutable(gameRoot,"Counter-Strike Global Offensive"))=="cs2.exe","Steam game library selects the Counter-Strike game executable, not the console helper");
            Check(GameIconService.Get(new LibraryGame { Executable = Path.Combine(gameBin, "cs2.exe") }) != null, "A game without embedded artwork receives a visible fallback icon");
            Check(GameIconService.Get(new LibraryGame { Executable = Environment.ProcessPath }) is BitmapSource, "Executable game icons are extracted as real artwork");
            Check(GameLibraryService.IsToolEntry("ShareX") && !GameLibraryService.IsToolEntry("Dota 2"), "Steam utilities are not presented as detected games");
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
                dashboard.MoveCardTo("music", 137, 319);
                var freeLayoutRead = new DataStore(App.Store.RootDir); freeLayoutRead.Load();
                Check(freeLayoutRead.Settings.DashboardPositions.TryGetValue("music", out var freePosition) && freePosition.X == 137 && freePosition.Y == 319,
                    "Free dashboard coordinates survive a settings reload without grid snapping");
                dashboard.MoveCardTo("music", -10, -20);
                Check(App.Store.Settings.DashboardPositions["music"].X == 0 && App.Store.Settings.DashboardPositions["music"].Y == 0,
                    "Dashboard cards cannot be dragged out of reach above or to the left");
                dashboard.MoveCardTo("music", 332, 504);
            }
            Check(read.GetProfile("verificationprofile")?.OverlayEnabled==false,"Profile round trip / case-insensitive lookup");
            Check(read.Sessions.Count>0&&double.IsNaN(read.Sessions.Last().GpuTempAvg),"Unavailable session metric round trip");
            Check(read.Sessions.Last().CpuTempAvg.Equals(App.Monitor.LastSample.CpuTempC),"CPU temperature session round trip");
            Check(double.IsNaN(GpuSampler.ParseDouble("N/A"))&&GpuSampler.ParseDouble("42.5")==42.5,"GPU parsing");
            Check(App.Monitor.Hub.CpuPercent.Count>2,"Live sampling advances history");
            if (App.Store.Performance != null && RankMethod.Complete(App.Store.Performance))
            {
                var reopened = new DataStore(App.Store.RootDir); reopened.Load();
                Check(RankMethod.Evaluate(reopened.Performance).Name == PassportService.CurrentRank().Name, "Real rank and passport agree after reloading data");
            }
            var processes=ProcessSnapshot.Capture();
            Check(processes.Count>0&&processes.Any(p=>p.MemoryMb>0),"Process snapshot contains real memory measurements");
            var analysis=StorageAnalyzer.Scan(Path.Combine(DirectoryPath,"persistence-test"),System.Threading.CancellationToken.None);
            Check(analysis.Files>=3&&analysis.Largest.Count>0,"Storage analyzer reads actual saved files");
            Check(File.Exists(Path.Combine(App.Store.RootDir,"settings.json")),"Settings save button creates a real file");
            Check(App.SelfTestMode.StartsWith("navigate",StringComparison.Ordinal)||Burmalk.Views.BenchmarkView.LastReport!=null,"Benchmark remains opt-in during UI verification");
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

        public static void VerifyPerformance(PerformanceRecord result)
        {
            var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            File.WriteAllText(Path.Combine(DirectoryPath, "performance-run.json"), JsonSerializer.Serialize(result, options));
            Check(RankMethod.Complete(result), "Full GPU 120s / CPU 60s / RAM 128 MiB run completed");
            if (result == null || !RankMethod.Complete(result)) return;
            var read = new DataStore(App.Store.RootDir); read.Load();
            Check(read.Performance != null && read.Performance.GpuFps == result.GpuFps && read.Performance.CpuMillionOps == result.CpuMillionOps,
                "Real performance measurements survive reopening DataStore");
            var passport = PassportService.Create(true, "Pulse");
            PassportExport.Png(PassportExport.Card(passport, false), Path.Combine(DirectoryPath, "Passport-tested.png"));
            PassportExport.Pdf(passport, Path.Combine(DirectoryPath, "Passport-tested.pdf"));
        }
    }
}

