using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Burmalk.Services
{
    internal static class GameIconService
    {
        private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint icons);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        public static ImageSource Get(LibraryGame game)
        {
            string key = game.IconPath ?? game.Executable;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            ImageSource source = null;
            if (!string.IsNullOrEmpty(game.IconPath))
            {
                try
                {
                    using var stream = File.OpenRead(game.IconPath);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 128; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); source = bitmap;
                }
                catch { }
            }
            if (source == null)
            {
                var large = new IntPtr[1]; var small = new IntPtr[1];
                try
                {
                    if (ExtractIconEx(game.Executable, 0, large, small, 1) > 0)
                    {
                        IntPtr icon = large[0] != IntPtr.Zero ? large[0] : small[0];
                        if (icon != IntPtr.Zero) { var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); source = bitmap; }
                    }
                }
                catch { }
                finally { if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); if (small[0] != IntPtr.Zero && small[0] != large[0]) DestroyIcon(small[0]); }
            }
            source ??= Fallback(); Cache[key] = source; return source;
        }
        private static ImageSource Fallback()
        {
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(30, 58, 95)), null, new Rect(0, 0, 64, 64), 14, 14);
                var white = new Pen(Brushes.White, 4);
                dc.DrawRoundedRectangle(null, white, new Rect(10, 20, 44, 27), 8, 8);
                dc.DrawLine(white, new Point(18, 33), new Point(30, 33)); dc.DrawLine(white, new Point(24, 27), new Point(24, 39));
                dc.DrawEllipse(Brushes.LightSkyBlue, null, new Point(42, 30), 3, 3); dc.DrawEllipse(Brushes.LightSkyBlue, null, new Point(47, 38), 3, 3);
            }
            drawing.Freeze(); var source = new DrawingImage(drawing); source.Freeze(); return source;
        }
    }
}
