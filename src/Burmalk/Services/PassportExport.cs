using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Burmalk.Controls;
using Burmalk.Models;

namespace Burmalk.Services
{
    internal static class PassportExport
    {
        private static Brush Ink = new SolidColorBrush(Color.FromRgb(239, 243, 255));
        private static Brush Muted = new SolidColorBrush(Color.FromRgb(169, 186, 210));
        private static Brush Accent(string style) => new SolidColorBrush(style == "Aurora" ? Color.FromRgb(185, 151, 251) : style == "Graphite" ? Color.FromRgb(219, 228, 241) : Color.FromRgb(82, 222, 214));
        private static double Text(DrawingContext dc, string text, double x, double y, double width, double size, Brush brush, bool bold = false)
        {
            var f = new FormattedText(text ?? "Нет данных", CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, 1) { MaxTextWidth = Math.Max(1, width) };
            dc?.DrawText(f, new Point(x, y)); return f.Height;
        }
        private static RenderTargetBitmap Bitmap(DrawingVisual visual, int width, int height)
        {
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bmp.Render(visual); bmp.Freeze(); return bmp;
        }
        public static BitmapSource Card(PcPassport p, bool portrait)
        {
            int w = portrait ? 1080 : 1600, h = portrait ? 1920 : 1120;
            int columns = portrait ? 1 : 2;
            double cellWidth = (w - 128 - (columns - 1) * 24) / columns;
            var hardware = p.Sections.Take(6).ToArray();
            double measured = 287;
            for (int row = 0; row < (hardware.Length + columns - 1) / columns; row++)
            {
                double height = 0;
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col; if (index >= hardware.Length) break;
                    var section = hardware[index];
                    double used = 35 + Text(null, section.Icon + "  " + section.Title, 0, 0, cellWidth - 44, portrait ? 17 : 15, Ink, true);
                    foreach (var line in section.Lines) used += Text(null, line, 0, 0, cellWidth - 44, portrait ? 23 : 21, Ink) + 8;
                    height = Math.Max(height, used + 17);
                }
                measured += height + 19;
            }
            measured += 33 + p.Sections.Last().Lines.Sum(line => Text(null, line, 0, 0, w - 128, portrait ? 21 : 18, Ink) + 7);
            h = Math.Max(h, (int)Math.Ceiling(measured + 110));
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
            {
                var bg = new LinearGradientBrush(Color.FromRgb(9, 15, 31), p.Style == "Aurora" ? Color.FromRgb(32, 19, 57) : p.Style == "Graphite" ? Color.FromRgb(21, 28, 39) : Color.FromRgb(12, 31, 45), 35);
                dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
                dc.DrawRoundedRectangle(null, new Pen(Accent(p.Style), 2), new Rect(28, 28, w - 56, h - 56), 20, 20);
                Text(dc, "BURMALK", 64, 58, w - 128, 37, Ink, true);
                Text(dc, "PC PASSPORT  /  " + p.CreatedLocal.ToString("dd.MM.yyyy"), 65, 110, w - 128, 17, Accent(p.Style));
                Text(dc, p.OwnerLabel, 65, 161, w - 390, 27, Ink, true);
                dc.PushTransform(new TranslateTransform(w - 277, 56)); dc.PushTransform(new ScaleTransform(.9, .9)); RankEmblem.Draw(dc, p.Rank); dc.Pop(); dc.Pop();
                Text(dc, p.Rank.Confirmed ? p.Rank.Name + " / RANK " + p.Rank.Letter + " · " + Math.Floor(p.Rank.Total).ToString("0") : "PC RANK · Не протестировано", 65, 208, w - 350, 21, Accent(p.Style), true);
                double top = 287;
                for (int row = 0; row < (hardware.Length + columns - 1) / columns; row++)
                {
                    double rowHeight = 0;
                    for (int col = 0; col < columns; col++)
                    {
                        int index = row * columns + col; if (index >= hardware.Length) break;
                        var section = hardware[index]; double x = 64 + col * (cellWidth + 24), y = top;
                        double used = 22 + Text(dc, section.Icon + "  " + section.Title, x + 22, y + 18, cellWidth - 44, portrait ? 17 : 15, Accent(p.Style), true) + 13;
                        foreach (string line in section.Lines)
                            used += Text(dc, line, x + 22, y + used, cellWidth - 44, portrait ? 23 : 21, Ink) + 8;
                        rowHeight = Math.Max(rowHeight, used + 17);
                    }
                    for (int col = 0; col < columns; col++)
                    {
                        int index = row * columns + col; if (index >= hardware.Length) break;
                        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(47, 69, 89)), 1), new Rect(64 + col * (cellWidth + 24), top, cellWidth, rowHeight), 12, 12);
                    }
                    top += rowHeight + 19;
                }
                var perf = p.Sections.Last();
                Text(dc, "BENCHMARK / " + p.Method, 65, top + 2, w - 128, 15, Accent(p.Style), true);
                double yPerf = top + 33;
                foreach (var line in perf.Lines) yPerf += Text(dc, line, 65, yPerf, w - 128, portrait ? 21 : 18, Ink) + 7;
                Text(dc, "Пульс твоего ПК  ·  burmalk.online", 65, h - 65, w - 128, 15, Muted);
            }
            return Bitmap(visual, w, h);
        }
        public static void Png(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }
        public static void Pdf(PcPassport p, string path)
        {
            var pages = new List<byte[]>(); const int width = 1240, height = 1754;
            DrawingVisual visual = null; DrawingContext dc = null; double y = 0; int page = 0;
            void EndPage()
            {
                if (dc == null) return;
                Text(dc, "BURMALK / PC PASSPORT  ·  " + ++page, 70, height - 75, width - 140, 17, Muted);
                dc.Close(); var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(Bitmap(visual, width, height)));
                using (var bytes = new MemoryStream()) { encoder.Save(bytes); pages.Add(bytes.ToArray()); } dc = null;
            }
            void NewPage()
            {
                EndPage(); visual = new DrawingVisual(); dc = visual.RenderOpen();
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 17, 30)), null, new Rect(0, 0, width, height));
                Text(dc, "BURMALK  /  PC PASSPORT", 70, 60, width - 140, 35, Ink, true);
                Text(dc, p.CreatedLocal.ToString("dd.MM.yyyy HH:mm") + " · " + p.OwnerLabel, 70, 117, width - 140, 20, Muted);
                y = 175;
            }
            NewPage();
            dc.PushTransform(new TranslateTransform(width - 230, 153)); dc.PushTransform(new ScaleTransform(.7, .7)); RankEmblem.Draw(dc, p.Rank); dc.Pop(); dc.Pop();
            Text(dc, p.Rank.Confirmed ? p.Rank.Name + " / RANK " + p.Rank.Letter + " · " + Math.Floor(p.Rank.Total).ToString("0") + " баллов" : "PC RANK · Не протестировано", 70, 194, width - 340, 29, Accent(p.Style), true);
            Text(dc, "Методика: " + p.Method + " · относительный индекс тестов Burmalk", 70, 249, width - 340, 17, Muted); y = 328;
            foreach (var section in p.Sections)
            {
                if (y > height - 280) NewPage();
                dc.DrawLine(new Pen(Accent(p.Style), 1), new Point(70, y), new Point(width - 70, y));
                y += 18 + Text(dc, section.Icon + "  " + section.Title, 70, y + 15, width - 140, 22, Accent(p.Style), true) + 13;
                foreach (var line in section.Lines)
                {
                    var measure = new FormattedText(line, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 22, Ink, 1) { MaxTextWidth = width - 140 };
                    if (y + measure.Height > height - 140) NewPage();
                    y += Text(dc, line, 70, y, width - 140, 22, Ink) + 10;
                }
                y += 23;
            }
            if (y > height - 260) NewPage();
            Text(dc, "Паспорт содержит доступные характеристики и результаты. Не заменяет диагностику. Объёмы GB и GiB используют разные единицы. Скорость накопителя появляется после измерения; результаты зависят от нагрузки и кэша. Результаты других методик не смешиваются. Серийные номера и уникальные идентификаторы не экспортируются.", 70, y, width - 140, 17, Muted);
            EndPage(); WritePdf(pages, width, height, path);
        }
        // Self-contained PDF 1.4 writer: Unicode is rendered with WPF fonts before JPEG page embedding.
        // No printer driver, browser, network, user path, serial number or external executable needed.
        private static void WritePdf(List<byte[]> pages, int w, int h, string path)
        {
            using var file = File.Create(path); var offsets = new List<long> { 0 };
            void Ascii(string text) { var bytes = Encoding.ASCII.GetBytes(text); file.Write(bytes, 0, bytes.Length); }
            void Object(int n, string body) { offsets.Add(file.Position); Ascii(n + " 0 obj\n" + body + "\nendobj\n"); }
            Ascii("%PDF-1.4\n");
            Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            Object(2, "<< /Type /Pages /Count " + pages.Count + " /Kids [" + string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => (3 + i * 3) + " 0 R")) + "] >>");
            for (int i = 0; i < pages.Count; i++)
            {
                int n = 3 + i * 3;
                Object(n, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im " + (n + 1) + " 0 R >> >> /Contents " + (n + 2) + " 0 R >>");
                offsets.Add(file.Position); Ascii((n + 1) + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + w + " /Height " + h + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + pages[i].Length + " >>\nstream\n");
                file.Write(pages[i], 0, pages[i].Length); Ascii("\nendstream\nendobj\n");
                string content = "q 595 0 0 842 0 0 cm /Im Do Q\n";
                Object(n + 2, "<< /Length " + content.Length + " >>\nstream\n" + content + "endstream");
            }
            long xref = file.Position; Ascii("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
            foreach (long offset in offsets.Skip(1)) Ascii(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            Ascii("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        }
    }
}
