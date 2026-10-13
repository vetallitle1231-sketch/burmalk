using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Threading;
using Burmalk.Services;

namespace Burmalk.Views
{
    /// <summary>Visible, cancellable two-minute WPF Direct3D scene benchmark.</summary>
    internal sealed class GpuStressWindow : Window
    {
        private readonly TextBlock _status = new TextBlock { FontSize = 16, Margin = new Thickness(14) };
        private readonly TextBlock _metrics = new TextBlock { FontSize = 14, Margin = new Thickness(14) };
        private readonly Button _cancel = new Button { Content = "Остановить тест", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(14) };
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly Stopwatch _elapsed = new Stopwatch();
        private readonly AxisAngleRotation3D _rotation = new AxisAngleRotation3D(new Vector3D(0.2, 1, 0.15), 0);
        private long _totalFrames, _windowFrames;
        private DateTime _lastFrameWindow = DateTime.UtcNow;
        private double _fps, _maxCpuTemp = double.NaN, _maxGpuTemp = double.NaN, _maxCpuMhz = double.NaN, _maxGpuMhz = double.NaN;
        private bool _finished;
        private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
        private readonly CancellationToken _cancellation;
        private readonly bool _autoClose;
        public bool Completed { get; private set; }
        public double DurationSeconds { get; private set; }
        public int RenderTier { get; private set; }
        public double MaxCpuTemp => _maxCpuTemp;
        public double MaxGpuTemp => _maxGpuTemp;
        public double MaxCpuMhz => _maxCpuMhz;
        public double MaxGpuMhz => _maxGpuMhz;
        public double AverageFps { get; private set; }
        public long ScorePoints => (long)Math.Round(AverageFps * 100);
        public string SensorSummary => "GPU max " + Fmt.C(_maxGpuTemp) + " / " + MetricView.Number(_maxGpuMhz, "MHz") +
            " · CPU max " + Fmt.C(_maxCpuTemp) + " / " + MetricView.Number(_maxCpuMhz, "MHz");
        public string FinishReason { get; private set; }

        public GpuStressWindow(Window owner, CancellationToken cancellation = default, bool autoClose = false)
        {
            _cancellation = cancellation; _autoClose = autoClose;
            Owner = Verification.DirectoryPath != null ? null : owner;
            Title = "Burmalk · двухминутный 3D-тест GPU";
            Width = 1180; Height = 760; MinWidth = 850; MinHeight = 600;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (Verification.DirectoryPath != null) { WindowStartupLocation = WindowStartupLocation.Manual; Left = 25; Top = 25; ShowActivated = true; }
            Background = new SolidColorBrush(Color.FromRgb(12, 18, 30));

            var scene = CreateScene();
            var visual = new ModelVisual3D { Content = scene };
            var camera = new PerspectiveCamera(new Point3D(0, 0, 13), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 52);
            var viewport = new Viewport3D { Width = 960, Height = 540, Camera = camera, ClipToBounds = true, Children = { visual }, IsHitTestVisible = false };
            var root = new DockPanel();
            var controls = new StackPanel();
            controls.Children.Add(new TextBlock { Text = "BURMALK · НАГРУЗКА GPU", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(14, 14, 14, 0) });
            controls.Children.Add(new TextBlock { Text = "2 минуты · останови тест, если компьютер перегревается. Автостоп: GPU 90 °C, CPU 95 °C.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 4, 14, 0) });
            controls.Children.Add(_status); controls.Children.Add(_metrics); controls.Children.Add(_cancel);
            DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls); root.Children.Add(new Viewbox { Child = viewport, Stretch = Stretch.Uniform }); Content = root;

            _cancel.Click += CancelClick;
            _timer.Tick += (s, e) => Tick();
            Loaded += (s, e) =>
            {
                RenderTier = RenderCapability.Tier >> 16;
                if ((RenderCapability.Tier >> 16) == 0)
                {
                    FinishReason = "Аппаратное 3D-ускорение недоступно; оценка GPU не рассчитана.";
                    _status.Text = FinishReason;
                    _cancel.Content = "Закрыть";
                    _cancel.Click -= CancelClick;
                    _cancel.Click += (s2, e2) => Close();
                    if (_autoClose) Dispatcher.BeginInvoke(new Action(Close));
                    return;
                }
                CompositionTarget.Rendering += OnRendering;
                _elapsed.Start(); _timer.Start();
                _status.Text = "Сцена рендерится · 00:00 / 02:00";
                _metrics.Text = "Собираю кадры и пиковые значения датчиков…";
            };
            Closed += (s, e) => { CompositionTarget.Rendering -= OnRendering; _timer.Stop(); _elapsed.Stop(); if (!_finished) FinishReason = "Окно теста закрыто пользователем"; };
        }

        private void CancelClick(object sender, RoutedEventArgs e) => Finish("Остановлено пользователем");

        private Model3DGroup CreateScene()
        {
            var scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(105, 110, 135)));
            scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -2, -3)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(100, 150, 255), new Vector3D(2, 1, 2)));
            var spin = new RotateTransform3D(_rotation);
            var root = new Model3DGroup { Transform = spin };
            var mesh = Sphere(0.33, 28, 16);
            mesh.Freeze();
            var palette = new[] { Colors.DeepSkyBlue, Colors.MediumPurple, Colors.Turquoise, Colors.Gold, Colors.Coral, Colors.LimeGreen };
            for (int x = 0; x < 8; x++) for (int y = 0; y < 5; y++) for (int z = 0; z < 3; z++)
            {
                var material = new MaterialGroup();
                material.Children.Add(new DiffuseMaterial(new SolidColorBrush(palette[(x + y * 2 + z * 3) % palette.Length])));
                material.Children.Add(new SpecularMaterial(Brushes.White, 48));
                var model = new GeometryModel3D(mesh, material)
                {
                    Transform = new TranslateTransform3D((x - 3.5) * 0.92, (y - 2) * 0.92, (z - 1) * 0.95),
                    BackMaterial = material
                };
                root.Children.Add(model);
            }
            scene.Children.Add(root);
            return scene;
        }

        private static MeshGeometry3D Sphere(double radius, int slices, int stacks)
        {
            var mesh = new MeshGeometry3D();
            for (int stack = 0; stack <= stacks; stack++)
            {
                double phi = Math.PI * stack / stacks;
                for (int slice = 0; slice <= slices; slice++)
                {
                    double theta = 2 * Math.PI * slice / slices;
                    var normal = new Vector3D(Math.Sin(phi) * Math.Cos(theta), Math.Cos(phi), Math.Sin(phi) * Math.Sin(theta));
                    mesh.Positions.Add(new Point3D(radius * normal.X, radius * normal.Y, radius * normal.Z));
                    mesh.Normals.Add(normal);
                    mesh.TextureCoordinates.Add(new Point(slice / (double)slices, stack / (double)stacks));
                }
            }
            for (int stack = 0; stack < stacks; stack++) for (int slice = 0; slice < slices; slice++)
            {
                int a = stack * (slices + 1) + slice, b = a + slices + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
            }
            return mesh;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (e is RenderingEventArgs frame)
            {
                if (frame.RenderingTime == _lastRenderingTime) return;
                _lastRenderingTime = frame.RenderingTime;
            }
            _rotation.Angle = (_elapsed.Elapsed.TotalSeconds * 18) % 360;
            _totalFrames++; _windowFrames++;
        }

        private void Tick()
        {
            if (_cancellation.IsCancellationRequested) { Finish("Остановлено пользователем"); return; }
            if (WindowState == WindowState.Minimized) { Finish("Тест остановлен: свёрнутое окно не даёт сопоставимого GPU-результата."); return; }
            var now = DateTime.UtcNow;
            double seconds = Math.Max(0.1, (now - _lastFrameWindow).TotalSeconds);
            _fps = _windowFrames / seconds; _windowFrames = 0; _lastFrameWindow = now;
            var sample = App.Monitor.LastSample;
            if (DateTime.UtcNow.Ticks - sample.TimestampUtc > TimeSpan.TicksPerSecond * 15) { Finish("Тест остановлен: мониторинг не обновляет датчики."); return; }
            Max(ref _maxCpuTemp, sample.CpuTempC); Max(ref _maxGpuTemp, sample.GpuTempC);
            Max(ref _maxCpuMhz, sample.CpuGhz * 1000); Max(ref _maxGpuMhz, sample.GpuClockMhz);
            double elapsed = _elapsed.Elapsed.TotalSeconds;
            if (Verification.DirectoryPath != null && elapsed >= 10 && elapsed < 11.1) Verification.Capture(this, "GPU-stress-active");
            _status.Text = "Сцена рендерится · " + TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss") + " / 02:00 · " + _fps.ToString("0") + " FPS";
            _metrics.Text = "GPU: макс. " + Fmt.C(_maxGpuTemp) + " · " + MetricView.Number(_maxGpuMhz, "MHz") +
                "\nCPU: макс. " + Fmt.C(_maxCpuTemp) + " · " + MetricView.Number(_maxCpuMhz, "MHz");
            if (double.IsFinite(sample.GpuTempC) && sample.GpuTempC >= 90) { Finish("Автостоп: температура GPU достигла 90 °C"); return; }
            if (double.IsFinite(sample.CpuTempC) && sample.CpuTempC >= 95) { Finish("Автостоп: температура CPU достигла 95 °C"); return; }
            if (elapsed >= 120)
            {
                DurationSeconds = elapsed;
                Completed = true;
                AverageFps = _totalFrames / elapsed;
                FinishReason = "Завершено · средняя частота рендеринга: " + AverageFps.ToString("0.0") + " FPS";
                _finished = true;
                _status.Text = FinishReason;
                _cancel.Content = "Закрыть";
                _cancel.Click -= CancelClick;
                _cancel.Click += (s, e) => Close();
                _timer.Stop(); CompositionTarget.Rendering -= OnRendering;
                if (_autoClose) Close();
            }
        }

        private static void Max(ref double maximum, double value)
        {
            if (double.IsFinite(value) && (!double.IsFinite(maximum) || value > maximum)) maximum = value;
        }

        private void Finish(string reason)
        {
            if (_finished) { Close(); return; }
            _finished = true; FinishReason = reason; _elapsed.Stop(); _timer.Stop(); CompositionTarget.Rendering -= OnRendering;
            _status.Text = reason; _cancel.Content = "Закрыть";
            _cancel.Click -= CancelClick; _cancel.Click += (s, e) => Close();
            Close();
        }
    }
}
