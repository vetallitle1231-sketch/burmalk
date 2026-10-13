using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Burmalk.Services;

namespace Burmalk.Views
{
    public class BenchmarkView : UserControl, IRefreshable
    {
        private readonly TextBlock _cpu = ViewUi.Text("—", "BigValue");
        private readonly TextBlock _memory = ViewUi.Text("—", "BigValue");
        private readonly TextBlock _peaks = ViewUi.Text("Пиковые значения появятся во время теста.");
        private readonly TextBlock _details = ViewUi.Text("");
        private readonly TextBlock _gpu = ViewUi.Text("Тест ещё не запускался.");
        private readonly Button _startSystem;
        private readonly Button _cancelSystem;
        private readonly Button _startGpu;
        private readonly DispatcherTimer _sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private CancellationTokenSource _cancellation;
        private bool _running;
        private double _maxCpuTemp = double.NaN, _maxGpuTemp = double.NaN, _maxCpuMhz = double.NaN, _maxGpuMhz = double.NaN;
        public static BenchmarkReport LastReport { get; private set; }
        public bool IsRunning => _running;

        public BenchmarkView()
        {
            _startSystem = ViewUi.Button("Начать тест CPU и памяти · 1 минута", RunSystem);
            _cancelSystem = ViewUi.Button("Остановить тест", CancelSystem);
            _cancelSystem.IsEnabled = false;
            var systemGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            systemGrid.Children.Add(ViewUi.Panel("CPU · производительность всех потоков", _cpu));
            systemGrid.Children.Add(ViewUi.Panel("RAM · проверка 128 МиБ", _memory));
            var root = new StackPanel();
            root.Children.Add(ViewUi.Panel("ПРОВЕРКА CPU И RAM", ViewUi.Text("Тест занимает около минуты и использует все логические потоки CPU. Перед запуском закрой игры. Проверяется запись и чтение 128 МиБ оперативной памяти; это проверка работы, а не диагностика всех модулей RAM."), _startSystem, _cancelSystem, _details, systemGrid, _peaks));
            _startGpu = ViewUi.Button("Начать GPU 3D-тест · 2 минуты", RunGpu);
            root.Children.Add(ViewUi.Panel("БАЛЛЫ ВИДЕОКАРТЫ", ViewUi.Text("Аппаратная OpenGL-сцена 1280 × 720, 96 шагов ray marching, без ограничения частотой монитора. Итог: средний FPS сцены × 100 баллов; сравнивать можно только результаты той же методики Burmalk v2. Тест можно остановить в любой момент. Автостоп срабатывает при GPU 90 °C или CPU 95 °C, если датчик температуры доступен."), _startGpu, ViewUi.Text("Во время теста появится отдельное окно с нагрузкой и показаниями датчиков."), _gpu));
            Content = root;
            _sampleTimer.Tick += (s, e) => CapturePeaks();
            Refresh();
        }

        public void Refresh()
        {
            var saved = App.Store.Benchmarks;
            if (!_running && saved != null && saved.MethodVersion == RankMethod.Version && RankMethod.MatchesHardware(saved, App.Monitor.Hardware.Snapshot, PassportService.GpuName()))
            {
                if (saved.CpuCompleted) _cpu.Text = saved.CpuMillionOps.ToString("0.00") + " млн операций / с";
                if (saved.RamCompleted) { _memory.Text = "Хорошо · 128 МиБ · " + saved.RamMegabytesPerSecond.ToString("0") + " MB/s"; _memory.Foreground = Brushes.LimeGreen; }
                if (saved.GpuCompleted) _gpu.Text = Math.Round(saved.GpuFps * 100) + " баллов · " + saved.GpuFps.ToString("0.0") + " FPS в среднем";
            }
            var sample = App.Monitor.LastSample;
            _peaks.Text = "Максимум во время последнего теста · CPU " + Fmt.C(_maxCpuTemp) + " / " + MetricView.Number(_maxCpuMhz, "MHz") +
                " · GPU " + Fmt.C(_maxGpuTemp) + " / " + MetricView.Number(_maxGpuMhz, "MHz");
            _peaks.ToolTip = "Сейчас · CPU " + Fmt.C(sample.CpuTempC) + " / " + MetricView.Number(sample.CpuGhz * 1000, "MHz") +
                " · GPU " + Fmt.C(sample.GpuTempC) + " / " + MetricView.Number(sample.GpuClockMhz, "MHz");
        }

        private static void Max(ref double current, double value)
        {
            if (double.IsFinite(value) && (!double.IsFinite(current) || value > current)) current = value;
        }

        private void CapturePeaks()
        {
            var sample = App.Monitor.LastSample;
            Max(ref _maxCpuTemp, sample.CpuTempC); Max(ref _maxGpuTemp, sample.GpuTempC);
            Max(ref _maxCpuMhz, sample.CpuGhz * 1000); Max(ref _maxGpuMhz, sample.GpuClockMhz);
            Refresh();
        }

        private async void RunSystem()
        {
            if (_running || !PerformanceTest.TryEnter()) { _details.Text = "Другой бенчмарк уже работает."; return; }
            _running = true; _startSystem.IsEnabled = false; _cancelSystem.IsEnabled = true;
            _cancellation = new CancellationTokenSource();
            _maxCpuTemp = _maxGpuTemp = _maxCpuMhz = _maxGpuMhz = double.NaN;
            CapturePeaks(); _sampleTimer.Start(); _details.Text = "Подготовка…";
            var progress = new Progress<string>(message => _details.Text = message);
            try
            {
                var report = await BenchmarkRunner.RunSystemAsync(progress, _cancellation.Token, Safe);
                LastReport = report;
                if (report.MemoryPassed)
                {
                    var record = ComponentRecord(); record.CpuWorkers = report.CpuWorkers; record.CpuMillionOps = report.CpuMillionOpsPerSecond; record.CpuSeconds = report.CpuSeconds; record.CpuCompleted = report.CpuSeconds >= 59;
                    record.RamMegabytesPerSecond = report.RamMegabytesPerSecond; record.RamCompleted = report.RamMegabytesPerSecond > 0; record.MemoryPassed = report.MemoryPassed; record.MemoryBytesVerified = report.MemoryBytesVerified;
                    App.Store.SaveComponentBenchmark(record, false);
                }
                if (report.MemoryPassed)
                {
                    _cpu.Text = report.CpuMillionOpsPerSecond.ToString("0.00") + " млн операций / с";
                    _memory.Text = "Хорошо · проверено " + Fmt.Gb(report.MemoryBytesVerified / (1024.0 * 1024.0 * 1024.0));
                    _memory.Foreground = Brushes.LimeGreen;
                    _details.Text = "Тест CPU и RAM завершён за " + report.Seconds.ToString("0") + " с.";
                }
                else
                {
                    _memory.Text = "Плохо · проверка памяти не прошла";
                    _memory.Foreground = Brushes.IndianRed;
                    _details.Text = "Проверка чтения не совпала с записанными данными.";
                }
            }
            catch (OperationCanceledException) { _details.Text = "Тест остановлен пользователем."; }
            catch (Exception ex) { _details.Text = "Ошибка теста: " + ex.Message; ErrorLog.Write("System benchmark: " + ex); }
            finally
            {
                _sampleTimer.Stop(); _running = false; _startSystem.IsEnabled = true; _cancelSystem.IsEnabled = false;
                _cancellation.Dispose(); _cancellation = null; Refresh();
                PerformanceTest.Exit();
            }
        }

        private void CancelSystem() => _cancellation?.Cancel();

        private static bool Safe()
        {
            var s = App.Monitor.LastSample;
            return DateTime.UtcNow.Ticks - s.TimestampUtc < TimeSpan.TicksPerSecond * 15 && (!double.IsFinite(s.CpuTempC) || s.CpuTempC < 95) && (!double.IsFinite(s.GpuTempC) || s.GpuTempC < 90);
        }
        private async void RunGpu()
        {
            if (_running || !PerformanceTest.TryEnter()) { _gpu.Text = "Другой бенчмарк уже работает."; return; }
            _running = true; _startGpu.IsEnabled = false;
            _cancellation = new CancellationTokenSource();
            _maxCpuTemp = _maxGpuTemp = _maxCpuMhz = _maxGpuMhz = double.NaN; _sampleTimer.Start();
            try
            {
                var result = await NativeGpuWindow.RunAsync(Window.GetWindow(this), _cancellation.Token, Safe);
                if (result.Completed)
                {
                    var record = ComponentRecord(); record.GpuFps = result.Fps; record.GpuSeconds = result.Seconds; record.GpuCompleted = true; record.RenderTier = 2;
                    record.GpuEngine = NativeGpuBenchmark.Engine; record.GpuRenderer = result.Renderer;
                    if (!NativeGpuBenchmark.MatchesRenderer(result.Renderer, record.GpuModel)) throw new InvalidOperationException("Тест использовал другую GPU: " + result.Renderer);
                    App.Store.SaveComponentBenchmark(record, true);
                }
                _gpu.Text = Math.Round(result.Fps * 100) + " баллов · " + result.Fps.ToString("0.0") + " FPS · " + result.Renderer;
            }
            catch (OperationCanceledException) { _gpu.Text = "GPU-тест остановлен"; }
            catch (Exception ex) { _gpu.Text = "GPU: " + ex.Message; ErrorLog.Write("GPU benchmark: " + ex); }
            finally { _sampleTimer.Stop(); _running = false; _startGpu.IsEnabled = true; _cancellation.Dispose(); _cancellation = null; PerformanceTest.Exit(); Refresh(); }
        }
        private static Burmalk.Models.PerformanceRecord ComponentRecord() => new Burmalk.Models.PerformanceRecord
        {
            MethodVersion = RankMethod.Version, TestedUtc = DateTime.UtcNow, CpuModel = App.Monitor.Hardware.Snapshot.Cpu.Model,
            GpuModel = PassportService.GpuName(), RamGb = App.Monitor.Hardware.Snapshot.Ram.TotalGb
        };
    }
}
