using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Burmalk.Models;

namespace Burmalk.Controls
{
    public sealed class RankEmblem : FrameworkElement
    {
        private PcRankResult _rank = new PcRankResult();
        public PcRankResult Rank { get => _rank; set { var next = value ?? new PcRankResult(); bool changed = next.Name != _rank.Name || next.Letter != _rank.Letter || next.Confirmed != _rank.Confirmed; _rank = next; if (changed) { InvalidateVisual(); UpdatePulse(); } } }
        public RankEmblem()
        {
            Width = Height = 140;
            Loaded += (s, e) => UpdatePulse(); Unloaded += (s, e) => BeginAnimation(OpacityProperty, null);
            IsVisibleChanged += (s, e) => UpdatePulse();
        }
        public static Color ColorFor(string name) => name switch
        {
            "BRONZE" => Color.FromRgb(218, 139, 83), "SILVER" => Color.FromRgb(199, 218, 236),
            "GOLD" => Color.FromRgb(245, 201, 91), "DIAMOND" => Color.FromRgb(80, 223, 236),
            "LEGENDARY" => Color.FromRgb(183, 139, 255), _ => Color.FromRgb(79, 128, 157)
        };
        public void UpdatePulse()
        {
            BeginAnimation(OpacityProperty, null); Opacity = 1;
            if (IsLoaded && IsVisible && Rank.Confirmed && App.Store?.Settings.ReducedEffects != true)
                BeginAnimation(OpacityProperty, new DoubleAnimation(.72, 1, TimeSpan.FromSeconds(Rank.Name == "LEGENDARY" ? 1.2 : 2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        protected override void OnRender(DrawingContext dc)
        {
            double scale = Math.Min(ActualWidth, ActualHeight) / 200;
            if (scale <= 0) return;
            dc.PushTransform(new TranslateTransform((ActualWidth - scale * 200) / 2, (ActualHeight - scale * 200) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale)); Draw(dc, Rank); dc.Pop(); dc.Pop();
        }
        public static void Draw(DrawingContext dc, PcRankResult rank)
        {
            Color color = ColorFor(rank.Name); var accent = new SolidColorBrush(color);
            var glow = new RadialGradientBrush(Color.FromArgb(95, color.R, color.G, color.B), Colors.Transparent);
            dc.DrawEllipse(glow, null, new Point(100, 100), 98, 98);
            if (rank.Name == "LEGENDARY")
            {
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 242, 196, 87)), 1.5), new Point(100, 100), 87, 87);
                dc.DrawEllipse(null, new Pen(accent, 2), new Point(100, 100), 77, 77);
            }
            int rays = rank.Name == "BRONZE" ? 6 : rank.Name == "LEGENDARY" ? 24 : 12;
            for (int i = 0; i < rays; i++)
            {
                double a = i * Math.PI * 2 / rays;
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B)), 1.3),
                    new Point(100 + Math.Cos(a) * 79, 100 + Math.Sin(a) * 79), new Point(100 + Math.Cos(a) * 90, 100 + Math.Sin(a) * 90));
            }
            var metal = new LinearGradientBrush(Color.FromRgb(25, 36, 57), Color.FromRgb(10, 16, 31), 90);
            Point[] shield = rank.Name == "DIAMOND" ? new[] { new Point(100, 26), new Point(166, 87), new Point(100, 171), new Point(34, 87) } :
                new[] { new Point(100, 34), new Point(154, 59), new Point(148, 126), new Point(100, 164), new Point(52, 126), new Point(46, 59) };
            var geometry = Polygon(shield); dc.DrawGeometry(metal, new Pen(accent, 3), geometry);
            dc.PushTransform(new ScaleTransform(.83, .83, 100, 100)); dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B)), 1), geometry); dc.Pop();
            if (rank.Name == "SILVER" || rank.Name == "GOLD" || rank.Name == "LEGENDARY")
                for (int i = 0; i < 3; i++)
                {
                    dc.DrawLine(new Pen(accent, 4 - i), new Point(45, 78 + i * 17), new Point(24 + i * 4, 67 + i * 17));
                    dc.DrawLine(new Pen(accent, 4 - i), new Point(155, 78 + i * 17), new Point(176 - i * 4, 67 + i * 17));
                }
            if (rank.Name == "GOLD")
                for (int i = 0; i < 5; i++) { dc.DrawEllipse(accent, null, new Point(37 + i * 3, 100 + i * 8), 3, 5); dc.DrawEllipse(accent, null, new Point(163 - i * 3, 100 + i * 8), 3, 5); }
            if (rank.Name == "DIAMOND") { dc.DrawLine(new Pen(accent, 1), shield[0], shield[2]); dc.DrawLine(new Pen(accent, 1), shield[1], shield[3]); }
            if (rank.Name == "LEGENDARY") dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 201, 91)), null,
                Polygon(new[] { new Point(65, 45), new Point(59, 21), new Point(82, 34), new Point(100, 12), new Point(118, 34), new Point(141, 21), new Point(135, 45) }));
            var text = new FormattedText(rank.Confirmed ? rank.Letter : "?", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 57,
                rank.Name == "LEGENDARY" ? new SolidColorBrush(Color.FromRgb(245, 201, 91)) : accent, 1);
            dc.DrawText(text, new Point(100 - text.Width / 2, 61));
            for (int i = 0; i < (rank.Name == "BRONZE" ? 1 : rank.Name == "SILVER" ? 2 : rank.Name == "GOLD" ? 3 : rank.Name == "DIAMOND" ? 4 : 5); i++)
                dc.DrawEllipse(accent, null, new Point(80 + i * 10, 139), 2, 2);
        }
        private static StreamGeometry Polygon(Point[] points)
        {
            var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(points[0], true, true); for (int i = 1; i < points.Length; i++) c.LineTo(points[i], true, false); } g.Freeze(); return g;
        }
    }
}
