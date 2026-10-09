using System.Windows;
using System.Windows.Controls;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>Game session view: live current game FPS + live system stats (IRefreshable — MainWindow drives refresh).</summary>
    public class GamesView : UserControl, IRefreshable
    {
        private readonly TextBlock _name = Big("No active game detected");
        private readonly TextBlock _duration = Big("—");
        private readonly TextBlock _fps = Big("—");
        private readonly TextBlock _gtemp = Big("—");
        private readonly TextBlock _gpu = Big("—");
        private readonly TextBlock _vram = Big("—");
        private readonly TextBlock _cpu = Big("—");
        private readonly TextBlock _ram = Big("—");

        public GamesView()
        {
            Margin = new Thickness(4);
            Content = BuildTree();
            Refresh();
        }

        private static TextBlock Big(string text)
        {
            return new TextBlock { Style = Theme.Style("BigValue"), Text = text };
        }

        private UIElement BuildTree()
        {
            var root = new StackPanel();

            root.Children.Add(Panel("LIVE GAME", new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8),
                Children =
                {
                    Label("GAME", _name, 10),
                    Label("SESSION DURATION", _duration, 10),
                    Label("FPS  (honest availability — never a fabricated number)", _fps, 0)
                }
            }));

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition());

            Cell(grid, "CPU", _cpu, 0, 0);
            Cell(grid, "GPU", _gpu, 1, 0);
            Cell(grid, "GPU TEMP", _gtemp, 0, 1);
            Cell(grid, "VRAM", _vram, 1, 1);
            Cell(grid, "RAM", _ram, 0, 2);

            root.Children.Add(Panel("SYSTEM STATS WHILE GAMING", grid));

            return root;
        }

        private static UIElement Label(string text, TextBlock value, double bottomMargin)
        {
            value.Margin = new Thickness(0, 0, 0, 0);
            return new StackPanel
            {
                Margin = new Thickness(0, 0, 0, bottomMargin),
                Children =
                {
                    new TextBlock { Style = Theme.Style("SubValue"), Text = text, Margin = new Thickness(0, 0, 0, 4) },
                    value
                }
            };
        }

        private static void Cell(Grid g, string caption, TextBlock value, int col, int row)
        {
            value.Margin = new Thickness(0, 0, 0, 10);
            var cell=Label(caption,value,10);
            Grid.SetColumn(cell, col);
            Grid.SetRow(cell, row);
            g.Children.Add(cell);
        }

        private static UIElement Panel(string title, UIElement body)
        {
            return new Border
            {
                Style = Theme.Style("Panel"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Children =
                    {
                        new TextBlock { Style = Theme.Style("Title"), Text = title.ToUpperInvariant(), Margin = new Thickness(0, 0, 0, 8) },
                        body
                    }
                }
            };
        }

        public void Refresh()
        {
            Models.GameCurrent cur = GameService.Current;
            if (cur == null)
            {
                _name.Text = "No active game detected";
                _duration.Text = "Unavailable";
                _fps.Text = "Unavailable";
                var sample=App.Monitor.LastSample;
                _cpu.Text=Fmt.Pct(sample.CpuPercent);_gpu.Text=Fmt.Pct(sample.GpuPercent);_gtemp.Text=Fmt.C(sample.GpuTempC);_vram.Text=Fmt.Gb(sample.GpuVramUsedGb);_ram.Text=Fmt.Gb(sample.RamUsedGb);
                return;
            }

            _name.Text = cur.Active ? (cur.GameName ?? "(undetected process)") : "No active game detected";
            _duration.Text = Fmt.Dur(cur.DurationSeconds);
            _fps.Text = cur.FpsAvailable
                ? cur.FpsAvg.ToString("0.0") + " FPS avg (min " + (double.IsInfinity(cur.FpsMin) ? "—" : cur.FpsMin.ToString("0.0")) + " / max " + (cur.FpsMax > 0 ? cur.FpsMax.ToString("0.0") : "—") + ")"
                : "Unavailable · no safe FPS source";
            _gpu.Text = Fmt.Pct(cur.GpuPercent);
            _gtemp.Text = Fmt.C(cur.GpuTempC);
            _vram.Text = Fmt.Gb(cur.GpuVramUsedGb);
            _cpu.Text = Fmt.Pct(cur.CpuPercent);
            _ram.Text = Fmt.Gb(cur.RamUsedGb);
        }
    }
}

