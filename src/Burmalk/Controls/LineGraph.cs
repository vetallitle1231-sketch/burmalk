using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Burmalk.Models;

namespace Burmalk.Controls
{
    /// <summary>
    /// Code-first sparkline over a MetricHistory ring buffer (no XAML). Draws a normalized
    /// line plus a translucent fill; NaN values break the chart into segments so "no data"
    /// regions stay honest. Views refresh by re-assigning Source (triggers re-render).
    /// </summary>
    public class LineGraph : FrameworkElement
    {
        private MetricHistory _source;
        private double _top = -1;      // -1 => auto range (buffer peak * 1.1)
        private string _suffix = string.Empty;
        public Color StrokeColor { get; set; } = Color.FromRgb(0x12, 0x8A, 0xFF);
        public bool ShowAxes { get; set; }
        public bool DrawAxisLabels { get; set; } = true;
        public bool UseMonitoringWindow { get; set; } = true;

        public LineGraph() { ClipToBounds = true; }

        public MetricHistory Source
        {
            get { return _source; }
            set { _source = value; InvalidateVisual(); }
        }

        public double MaxValue
        {
            get { return _top; }
            set { _top = value; InvalidateVisual(); }
        }

        public string Suffix
        {
            get { return _suffix; }
            set { _suffix = value ?? string.Empty; }
        }

        /// <summary>Formats the current value of a history for a label. NaN => "-".</summary>
        public static string Format(MetricHistory h, string suffix)
        {
            if (h == null) return "Unavailable";
            double v = h.Current;
            if (double.IsNaN(v)) return "Unavailable";
            return v.ToString("0.#", CultureInfo.InvariantCulture) + (suffix ?? "");
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;

            if (_source == null || _source.Count < 2)
            {
                NoData(dc, "no data yet", w, h);
                return;
            }

            double[] all = new double[_source.Capacity];
            long[] times = new long[_source.Capacity];
            int count = _source.Snapshot(all,times);
            int start=0;
            if(UseMonitoringWindow)while(start<count-2 && times[start]<times[count-1]-App.Store.Settings.GraphHistorySeconds*TimeSpan.TicksPerSecond)start++;
            int n = count-start;
            double[] vals = new double[n];
            Array.Copy(all, count - n, vals, 0, n);
            double duration=(times[count-1]-times[start])/(double)TimeSpan.TicksPerSecond;
            double X(int k) => UseMonitoringWindow && duration>0 ? (times[start+k]-times[start])/(double)(times[count-1]-times[start]) : k/(double)(n-1);
            if (n < 2) { NoData(dc, "no data yet", w, h); return; }

            double hi = -double.MaxValue, lo = double.MaxValue;
            bool any = false;
            for (int j = 0; j < n; j++)
            {
                if (double.IsNaN(vals[j])) continue;
                any = true;
                if (vals[j] > hi) hi = vals[j];
                if (vals[j] < lo) lo = vals[j];
            }
            if (!any) { NoData(dc, "Unavailable", w, h); return; }

            double top = _top > 0 ? _top : Math.Max(hi * 1.1, lo + 1);
            double bottom = 0;
            if (top - bottom < 1e-9) top = bottom + 1;

            double pad = ShowAxes ? 42 : 3;
            double gw = w - pad - 8, gh = h - (ShowAxes ? 26 : 6);
            if (ShowAxes && DrawAxisLabels)
            {
                for (int row = 0; row <= 4; row++)
                {
                    double y = 3 + gh * row / 4;
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x24,0x36,0x52)), 0.5), new Point(pad,y), new Point(w-8,y));
                    DrawLabel(dc, (top * (4-row)/4).ToString("0.#") + _suffix, 0, y-6);
                }
                DrawLabel(dc, "−" + TimeSpan.FromSeconds(duration).ToString(@"mm\:ss"), pad, gh+7);
                DrawLabel(dc, "now", w-35, gh+7);
            }
            double yPad = 3;

            var figures = new PathFigureCollection();
            var linePen = new Pen(new SolidColorBrush(StrokeColor), 1.7)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var fillBrush = new SolidColorBrush(Color.FromArgb(0x26, StrokeColor.R, StrokeColor.G, StrokeColor.B));

            // Walk buffer; each maximal non-NaN run becomes one stroke + one fill figure.
            int i = 0;
            while (i < n)
            {
                if (double.IsNaN(vals[i])) { i++; continue; }

                int runStart = i;
                while (i < n && !double.IsNaN(vals[i])) i++;
                int runEnd = i;   // exclusive

                var fig = new PathFigure();
                fig.StartPoint = new Point(
                    pad + gw * X(runStart), yPad + gh);
                fig.IsClosed = true;

                Point prev = new Point();
                bool hasPrev = false;
                for (int k = runStart; k < runEnd; k++)
                {
                    double x = pad + gw * X(k);
                    double y = yPad + gh - Math.Clamp((vals[k] - bottom) / (top - bottom), 0, 1) * gh;
                    var p = new Point(x, y);
                    fig.Segments.Add(new LineSegment(p, true));
                    if (hasPrev) dc.DrawLine(linePen, prev, p);
                    prev = p;
                    hasPrev = true;
                }

                fig.Segments.Add(new LineSegment(
                    new Point(pad + gw * X(runEnd - 1), yPad + gh), true));
                fig.Freeze();
                figures.Add(fig);
            }

            figures.Freeze();
            var fill = new PathGeometry(figures, FillRule.EvenOdd, null);
            fill.Freeze();
            dc.DrawGeometry(fillBrush, null, fill);
        }

        private static void NoData(DrawingContext dc, string text, double w, double h)
        {
            var face = new Typeface("Segoe UI");
            var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, face, 12,
                (Application.Current.TryFindResource("FgMuted") as Brush ?? Brushes.Gray), 1.0);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
        }
        private void DrawLabel(DrawingContext dc, string text, double x, double y)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, (Application.Current.TryFindResource("FgMuted") as Brush ?? Brushes.Gray), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x,y));
        }
    }
}



