using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Burmalk.Models;

namespace Burmalk.Services
{
    public static class Appearance
    {
        public static readonly string[] Themes = { "Dark", "Violet", "Emerald", "Rose", "Light", "Anime", "Custom photo" };
        public static event Action Changed;
        public static void Apply(AppSettings settings)
        {
            var light = settings.Theme == "Light";
            string bg = light ? "#EEF2F8" : "#080F20", panel = light ? "#FFFFFF" : "#101D35";
            string accent = "#0065EF";
            if (settings.Theme == "Violet" || settings.Theme == "Anime") { bg="#140E25";panel="#251A3B";accent="#9957EA"; }
            if (settings.Theme == "Emerald") { bg="#071C19";panel="#102D28";accent="#00896F"; }
            if (settings.Theme == "Rose") { bg="#210F19";panel="#361B29";accent="#C43F7E"; }
            bool wallpaper=settings.Theme=="Anime"||settings.Theme=="Custom photo";
            Set("BgDark",bg); Set("PanelBg",wallpaper?"#F5"+panel.Substring(1):panel); Set("Accent",accent);
            Set("ChromeBg",bg);
            Set("FgMain",light?"#14233B":"#EDF3FF"); Set("FgMuted",light?"#455B76":"#BCCBE3");
            Set("ControlBg",light?"#E0E8F4":panel); Set("BorderColor",light?"#B5C5DA":"#3A4863");
            Set("RowAlt",light?"#F1F5FA":bg);
            Application.Current.Resources["PanelPadding"] = new Thickness(settings.CompactLayout ? 10 : 18);
            Application.Current.Resources["PanelRadius"] = new CornerRadius(settings.CompactLayout ? 4 : 12);
            // Only the panel background is translucent; text and graphs stay opaque.
            Application.Current.Resources["PanelOpacity"] = 1.0;
            Changed?.Invoke();
        }
        private static void Set(string key,string color) => Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        public static BitmapImage LoadImage(AppSettings settings)
        {
            try
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=1920;
                if(settings.Theme=="Anime") {image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;image.UriSource=new Uri("pack://application:,,,/Assets/Anime.png");}
                else if(settings.Theme=="Custom photo" && File.Exists(settings.BackgroundImage))
                {
                    using(var stream=File.OpenRead(settings.BackgroundImage)) { image.StreamSource=stream;image.EndInit();image.Freeze();return image; }
                }
                else return null;
                image.EndInit();image.Freeze();return image;
            }
            catch(Exception ex) { ErrorLog.Write("Background image: "+ex.Message); return null; }
        }
        public static string ImportPhoto(string source,string root)
        {
            if(new FileInfo(source).Length>40*1024*1024) throw new InvalidOperationException("Choose an image smaller than 40 MB.");
            var decoded=LoadImage(new AppSettings {Theme="Custom photo",BackgroundImage=source});
            if(decoded==null)throw new InvalidOperationException("This image cannot be opened.");
            string folder=Path.Combine(Path.GetFullPath(root),"backgrounds");Directory.CreateDirectory(folder);
            // A new image gets a new file. Never overwrite an image that is being displayed.
            var target=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
            try
            {
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(decoded));
                using(var output=File.Create(target))encoder.Save(output);
            }
            catch{if(File.Exists(target))File.Delete(target);throw;}
            return target;
        }
    }
}
