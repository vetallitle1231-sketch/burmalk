using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Burmalk.Controls;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    public sealed class PcRankView : UserControl, IRefreshable
    {
        private readonly RankEmblem _emblem = new RankEmblem { Width = 210, Height = 210 };
        private readonly TextBlock _name = ViewUi.Text("Получи свой PC RANK", "BigValue");
        private readonly TextBlock _score = ViewUi.Text("GPU 50% · CPU 35% · RAM 15%");
        private readonly TextBlock _status = ViewUi.Text("Тест ещё не запускался.");
        private readonly TextBlock _results = ViewUi.Text("");
        private readonly Button _start, _cancel;
        private readonly CheckBox _consent = new CheckBox { Content = "Готов к нагрузке: закрыл игры и сохранил работу", Margin = new Thickness(8) };
        private readonly CheckBox _reduced = new CheckBox { Content = "Уменьшить визуальные эффекты", Margin = new Thickness(8) };
        private readonly Grid _root = new Grid();
        private readonly Grid _reveal = new Grid { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(247, 6, 10, 23)) };
        private CancellationTokenSource _cts;
        private PerformanceRecord _shown;
        public bool Running => _cts != null;
        public PcRankView()
        {
            var root = new StackPanel(); root.Children.Add(BrandVisuals.Badge("BURMALK  /  PC RANK"));
            root.Children.Add(new TextBlock { Text = "Узнай ранг своей сборки.", FontSize = 32, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 20) });
            var hero = new DockPanel(); DockPanel.SetDock(_emblem, Dock.Left); hero.Children.Add(_emblem);
            var description = new StackPanel { Margin = new Thickness(28, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            description.Children.Add(_name); description.Children.Add(_score); description.Children.Add(_results); hero.Children.Add(description);
            root.Children.Add(ViewUi.Panel("MY PC RANK", hero));
            var levels = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5 };
            string[] names = { "BRONZE", "SILVER", "GOLD", "DIAMOND", "LEGENDARY" }, letters = { "D", "C", "B", "A", "S" };
            for (int i = 0; i < names.Length; i++)
            {
                var item = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 8, 4, 15) };
                item.Children.Add(new RankEmblem { Rank = new PcRankResult { Confirmed = true, Name = names[i], Letter = letters[i] }, Width = 85, Height = 85 });
                item.Children.Add(new TextBlock { Text = names[i] + " / " + letters[i], Foreground = new SolidColorBrush(RankEmblem.ColorFor(names[i])), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10, FontWeight = FontWeights.SemiBold }); levels.Children.Add(item);
            }
            root.Children.Add(levels);
            _start = ViewUi.Button("ПРОВЕРИТЬ МОЩНОСТЬ ПК", () => _ = RunAsync(true));
            _start.SetResourceReference(BackgroundProperty, "Accent"); _start.SetResourceReference(ForegroundProperty, "AccentInk");
            _cancel = ViewUi.Button("Отменить тест", () => _cts?.Cancel()); _cancel.IsEnabled = false;
            _reduced.IsChecked = App.Store.Settings.ReducedEffects;
            _reduced.Click += (s, e) => { App.Store.Settings.ReducedEffects = _reduced.IsChecked == true; App.Store.Save(); _emblem.UpdatePulse(); foreach (var child in levels.Children) if (child is StackPanel stack) foreach (var emblem in stack.Children) if (emblem is RankEmblem mark) mark.UpdatePulse(); };
            root.Children.Add(ViewUi.Panel("ОКОЛО 3 МИНУТ", ViewUi.Text("GPU: 2 минуты 3D-сцены. CPU: одно ядро, 60 секунд. RAM: полная запись и чтение фиксированных 128 МиБ. Тест создаёт нагрузку; может нагреть ПК и замедлить другие приложения. Отмена и автостоп по доступным температурам не присваивают ранг."), _consent, _start, _cancel, _status, _reduced));
            var method = new Expander { Header = "Как считается ранг", Margin = new Thickness(8, 14, 8, 20), Content = ViewUi.Text("Это относительный индекс тестов Burmalk, а не место среди всех ПК. Каждый результат делится на измеренный эталон этой методики и умножается на 1000. Затем: GPU × 50% + CPU × 35% + RAM × 15%.\nЦентры рангов: BRONZE — ¼ эталона; SILVER — ½; GOLD — 1; DIAMOND — 2; LEGENDARY — 4. Границы проходят посередине между соседними уровнями по геометрической шкале, чтобы небольшой разброс теста не менял ранг эталона.\nGPU-тест WPF зависит от CPU, драйвера, частоты экрана и DPI, поэтому не равен FurMark или универсальной оценке игровой мощности. CPU-тест измеряет одно ядро; RAM-тест не диагностирует всю память.\nРанг подтверждается только после полного прохода всех трёх тестов. Методика: " + RankMethod.Version) };
            root.Children.Add(method); _root.Children.Add(root); _root.Children.Add(_reveal); Content = _root;
            Refresh(); Unloaded += (s, e) => DismissReveal();
        }
        internal async Task<PerformanceRecord> RunAsync(bool userInitiated)
        {
            if (Running || PerformanceTest.Busy) { _status.Text = "Другой тест уже работает."; return null; }
            if (userInitiated && _consent.IsChecked != true) { _status.Text = "Подтверди готовность к нагрузке галочкой выше."; return null; }
            if (!App.Monitor.Hardware.ProbeCompleted) { _status.Text = "Подожди завершения сбора характеристик."; return null; }
            _cts = new CancellationTokenSource(); _start.IsEnabled = false; _cancel.IsEnabled = true; _consent.IsEnabled = false;
            try
            {
                var result = await PerformanceTest.RunAsync(Window.GetWindow(this), new Progress<string>(p => _status.Text = p), _cts.Token);
                // Keep the last confirmed result if the new run was cancelled or failed.
                if (RankMethod.Complete(result)) { App.Store.SavePerformance(result); _shown = null; Refresh(); if (userInitiated) Reveal(PassportService.CurrentRank()); }
                _status.Text = result.Status + (RankMethod.Complete(result) ? " · результат сохранён" : " · прежний подтверждённый ранг сохранён");
                return result;
            }
            catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; ErrorLog.Write("PC rank view: " + ex); return null; }
            finally { _cts.Dispose(); _cts = null; _start.IsEnabled = true; _cancel.IsEnabled = false; _consent.IsEnabled = true; }
        }
        public void Refresh()
        {
            var r = App.Store.Performance; var rank = PassportService.CurrentRank(); _emblem.Rank = rank;
            _name.Text = rank.Confirmed ? rank.Name + " / RANK " + rank.Letter : "Получи свой PC RANK";
            _score.Text = rank.Confirmed ? Math.Floor(rank.Total).ToString("0") + " БАЛЛОВ BURMALK  ·  " + r.TestedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : rank.Reason;
            _results.Text = rank.Confirmed ? "GPU " + rank.Gpu.ToString("0") + " · CPU " + rank.Cpu.ToString("0") + " · RAM " + rank.Ram.ToString("0") + " нормализованных баллов\n" + r.CpuModel + "\n" + r.GpuModel : "";
            if (!ReferenceEquals(_shown, r)) { _shown = r; if (!Running && r != null) _status.Text = r.Status; }
            if (!Running) _start.IsEnabled = !PerformanceTest.Busy;
        }
        internal void Reveal(PcRankResult rank)
        {
            if (!rank.Confirmed) return; DismissReveal(); _reveal.Children.Clear(); _reveal.Visibility = Visibility.Visible;
            int intensity = rank.Name == "LEGENDARY" ? 36 : rank.Name == "GOLD" || rank.Name == "DIAMOND" ? 22 : 12;
            var sparkField = new Canvas { IsHitTestVisible = false, ClipToBounds = true }; _reveal.Children.Add(sparkField);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
            var emblem = new RankEmblem { Width = 260, Height = 260, Rank = rank, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
            stack.Children.Add(emblem); stack.Children.Add(new TextBlock { Text = "YOUR PC RANK", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(RankEmblem.ColorFor(rank.Name)), Margin = new Thickness(0, 20, 0, 12) });
            stack.Children.Add(new TextBlock { Text = rank.Name + " / RANK " + rank.Letter, FontSize = 38, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = Math.Floor(rank.Total).ToString("0") + " БАЛЛОВ BURMALK", FontSize = 23, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 20) });
            stack.Children.Add(ViewUi.Button("Пропустить / Продолжить", DismissReveal)); _reveal.Children.Add(stack);
            if (App.Store.Settings.ReducedEffects) return;
            _reveal.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.4)));
            var scale = (ScaleTransform)emblem.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.25, 1, TimeSpan.FromSeconds(1.4)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .2 } });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.25, 1, TimeSpan.FromSeconds(1.4)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .2 } });
            double width = Math.Max(720, ActualWidth), height = Math.Max(580, ActualHeight);
            for (int i = 0; i < intensity; i++)
            {
                double a = Math.PI * 2 * i / intensity;
                var spark = new Ellipse { Width = 3 + i % 3, Height = 3 + i % 3, Fill = new SolidColorBrush(rank.Name == "LEGENDARY" && i % 2 == 0 ? RankEmblem.ColorFor("GOLD") : RankEmblem.ColorFor(rank.Name)) };
                double x = width / 2 + Math.Cos(a) * 75, y = height / 2 - 80 + Math.Sin(a) * 75;
                Canvas.SetLeft(spark, x); Canvas.SetTop(spark, y); sparkField.Children.Add(spark);
                spark.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + Math.Cos(a) * 180, TimeSpan.FromSeconds(2.8)));
                spark.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, y + Math.Sin(a) * 180, TimeSpan.FromSeconds(2.8)));
                spark.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(2.8)));
            }
        }
        internal void DismissReveal() { _reveal.BeginAnimation(OpacityProperty, null); _reveal.Children.Clear(); _reveal.Visibility = Visibility.Collapsed; }
        internal void Cancel() => _cts?.Cancel();
    }
}
