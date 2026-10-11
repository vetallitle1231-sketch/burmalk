using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
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
        private readonly TextBlock _ctemp = Big("—");
        private readonly TextBlock _ram = Big("—");
        private readonly WrapPanel _library = new WrapPanel();
        private readonly TextBox _search = new TextBox { MinWidth = 240, ToolTip = "Поиск по названию игры", Margin = new Thickness(0, 0, 8, 8) };
        private List<LibraryGame> _games = new List<LibraryGame>();
        private readonly TextBlock _libraryStatus = ViewUi.Text("Сканирование Steam и ярлыков игр…");
        private bool _scanning;

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

            var libraryActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            libraryActions.Children.Add(ViewUi.Button("Обновить список игр", ScanLibrary));
            libraryActions.Children.Add(ViewUi.Button("Добавить игру (.exe)", AddGame));
            var libraryBody = new StackPanel();
            libraryBody.Children.Add(_libraryStatus); libraryBody.Children.Add(libraryActions);
            libraryBody.Children.Add(ViewUi.Text("Поиск игры")); libraryBody.Children.Add(_search);
            _search.TextChanged += (_, _) => RenderLibrary();
            libraryBody.Children.Add(new ScrollViewer { Content = _library, Height = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            root.Children.Add(Panel("БИБЛИОТЕКА ИГР", libraryBody));

            var session = new StackPanel();
            session.Children.Add(Panel("LIVE GAME", new StackPanel
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
            Cell(grid, "CPU TEMP", _ctemp, 1, 2);

            session.Children.Add(Panel("SYSTEM STATS WHILE GAMING", grid));
            root.Children.Add(new Expander { Header = "Текущая игра · показатели системы", Content = session, Margin = new Thickness(0, 8, 0, 8) });
            ScanLibrary();

            return root;
        }

        private async void ScanLibrary()
        {
            if (_scanning) return;
            _scanning = true;
            _libraryStatus.Text = "Ищу игры в Steam и ярлыки меню «Пуск»…";
            try
            {
                _games = await GameLibraryService.ScanAsync(App.Store.Settings.GameLibraryPaths);
                RenderLibrary();
            }
            catch (Exception ex) { _libraryStatus.Text = "Не удалось просканировать игры: " + ex.Message; ErrorLog.Write("Game library: " + ex); }
            finally { _scanning = false; }
        }

        private void RenderLibrary()
        {
            _library.Children.Clear();
            var games = _games.Where(g => g.Name.Contains(_search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            foreach (var game in games)
            {
                var tile = new StackPanel();
                tile.Children.Add(new Image { Source = GameIconService.Get(game), Width = 64, Height = 64, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
                tile.Children.Add(new TextBlock { Text = game.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, Height = 48, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, ToolTip = game.Name });
                var buttons = new WrapPanel();
                buttons.Children.Add(ViewUi.Button("▶ Запустить", () =>
                {
                    try { GameLibraryService.Launch(game); }
                    catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Burmalk · игры"); }
                }));
                if ((App.Store.Settings.GameLibraryPaths ?? Array.Empty<string>()).Contains(game.Executable, StringComparer.OrdinalIgnoreCase))
                    buttons.Children.Add(ViewUi.Button("Убрать", () =>
                    {
                        App.Store.Settings.GameLibraryPaths = App.Store.Settings.GameLibraryPaths.Where(p => !string.Equals(p, game.Executable, StringComparison.OrdinalIgnoreCase)).ToArray();
                        App.Store.Save(); ScanLibrary();
                    }));
                tile.Children.Add(buttons);
                var card = new Border { Child = tile, Width = 258, MinHeight = 210, Padding = new Thickness(16), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), ToolTip = game.Executable };
                card.SetResourceReference(Border.BackgroundProperty, "ControlBg"); card.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
                _library.Children.Add(card);
            }
            _libraryStatus.Text = _games.Count == 0 ? "Игры не найдены. Добавь файл .exe вручную." : "Найдено: " + _games.Count + " · показано: " + games.Length;
        }

        private void AddGame()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Исполняемый файл игры|*.exe", Title = "Добавить игру в библиотеку" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var paths = (App.Store.Settings.GameLibraryPaths ?? System.Array.Empty<string>()).ToList();
            if (!paths.Contains(dialog.FileName, System.StringComparer.OrdinalIgnoreCase)) paths.Add(dialog.FileName);
            App.Store.Settings.GameLibraryPaths = paths.ToArray();
            App.Store.Save();
            ScanLibrary();
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
                _cpu.Text=Fmt.Pct(sample.CpuPercent);_ctemp.Text=Fmt.C(sample.CpuTempC);_gpu.Text=Fmt.Pct(sample.GpuPercent);_gtemp.Text=Fmt.C(sample.GpuTempC);_vram.Text=Fmt.Gb(sample.GpuVramUsedGb);_ram.Text=Fmt.Gb(sample.RamUsedGb);
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
            _ctemp.Text = Fmt.C(cur.CpuTempC);
            _ram.Text = Fmt.Gb(cur.RamUsedGb);
        }
    }
}

