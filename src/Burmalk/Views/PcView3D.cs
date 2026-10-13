using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Burmalk.Services;

namespace Burmalk.Views
{
    public sealed class PcView3D : UserControl, IRefreshable
    {
        private readonly Viewport3D _viewport = new Viewport3D { ClipToBounds = true };
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 43, NearPlaneDistance = .1, FarPlaneDistance = 100 };
        private readonly Dictionary<GeometryModel3D, string> _parts = new Dictionary<GeometryModel3D, string>();
        private readonly Dictionary<GeometryModel3D, Material> _original = new Dictionary<GeometryModel3D, Material>();
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        private readonly TextBlock _partTitle = ViewUi.Text("", "BigValue"), _facts = ViewUi.Text(""), _live = ViewUi.Text("");
        private readonly DispatcherTimer _cameraTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly CheckBox _spin = new CheckBox { Content = "Автовращение", Margin = new Thickness(8) };
        private string _selected = "gpu";
        private double _yaw = 32, _pitch = 15, _distance = 14, _targetYaw = 32, _targetPitch = 15, _targetDistance = 14;
        private Point? _down;
        private Point _previous;
        private bool _dragging;
        private string _sceneKey;
        private readonly bool _compact;
        internal string SelectedPart => _selected;
        internal bool AnimationRunning => _cameraTimer.IsEnabled;
        internal double CameraDistance => _targetDistance;
        public PcView3D(bool compact = false)
        {
            _compact = compact; if (compact) { _distance = _targetDistance = App.Store.Settings.PcWidgetDistance; _yaw = _targetYaw = App.Store.Settings.PcWidgetYaw; _pitch = _targetPitch = App.Store.Settings.PcWidgetPitch; }
            var root = new StackPanel(); root.Children.Add(BrandVisuals.Badge("BURMALK  /  3D PC VIEW"));
            root.Children.Add(new TextBlock { Text = "Загляни внутрь своего ПК.", FontSize = 32, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 10) });
            root.Children.Add(ViewUi.Text("Универсальный корпус, настоящие характеристики. Перетаскивай мышью для вращения; колесо — масштаб. Выбери деталь мышью или кнопкой."));
            var tools = new WrapPanel { Margin = new Thickness(0, 14, 0, 10) };
            foreach (var part in new[] { ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("board", "Плата"), ("disks", "Накопители"), ("cooling", "Охлаждение") })
            {
                string id = part.Item1; var button = ViewUi.Button(part.Item2, () => SelectPart(id)); _buttons[id] = button; tools.Children.Add(button);
            }
            root.Children.Add(tools);
            var main = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stage = new Grid { Height = compact ? 158 : 500, Focusable = true, Background = new RadialGradientBrush(Color.FromRgb(31, 30, 62), Color.FromRgb(10, 17, 32)), ClipToBounds = true };
            _viewport.Camera = _camera; stage.Children.Add(_viewport);
            var hint = ViewUi.Text("360°  /  КОЛЕСО: МАСШТАБ  /  СТРЕЛКИ: ПОВОРОТ"); hint.FontSize = 10; hint.Margin = new Thickness(18); hint.VerticalAlignment = VerticalAlignment.Bottom; hint.IsHitTestVisible = false; stage.Children.Add(hint);
            var sceneBorder = new Border { Child = stage, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 0, 16, 0) };
            sceneBorder.SetResourceReference(Border.BorderBrushProperty, "BorderColor"); main.Children.Add(sceneBorder);
            var details = ViewUi.Panel("ВЫБРАННЫЙ КОМПОНЕНТ", _partTitle, _facts, _live); Grid.SetColumn(details, 1); main.Children.Add(details); root.Children.Add(main);
            var controls = new WrapPanel(); controls.Children.Add(ViewUi.Button("Сбросить камеру", ResetCamera));
            controls.Children.Add(ViewUi.Button("Приблизить +", () => Zoom(-1))); controls.Children.Add(ViewUi.Button("Отдалить −", () => Zoom(1))); controls.Children.Add(_spin); root.Children.Add(controls);
            root.Children.Add(ViewUi.Text("Модель иллюстративная: форма и расположение деталей не являются точным сканом сборки. Нет данных — датчик не поддерживается или недоступен."));
            var chassisChoice = new ComboBox { ItemsSource = new[] { "Авто: по данным корпуса", "Настольный ПК", "Ноутбук" }, SelectedIndex = App.Store.Settings.PcModelChassis == "Laptop" ? 2 : App.Store.Settings.PcModelChassis == "Desktop" ? 1 : 0, MinWidth = 210, Margin = new Thickness(8) };
            var coolingChoice = new ComboBox { ItemsSource = new[] { "Тип охлаждения не указан", "Воздушный кулер", "СЖО / водяное" }, SelectedIndex = App.Store.Settings.PcModelCooling == "Aio" ? 2 : App.Store.Settings.PcModelCooling == "Air" ? 1 : 0, MinWidth = 210, Margin = new Thickness(8) };
            var choices = new WrapPanel(); choices.Children.Add(chassisChoice); choices.Children.Add(coolingChoice); root.Children.Insert(4, choices);
            chassisChoice.SelectionChanged += (s, e) => { App.Store.Settings.PcModelChassis = chassisChoice.SelectedIndex == 2 ? "Laptop" : chassisChoice.SelectedIndex == 1 ? "Desktop" : "Auto"; App.Store.Save(); Refresh(); };
            coolingChoice.SelectionChanged += (s, e) => { App.Store.Settings.PcModelCooling = coolingChoice.SelectedIndex == 2 ? "Aio" : coolingChoice.SelectedIndex == 1 ? "Air" : "Auto"; App.Store.Save(); Refresh(); };
            if (compact) { main.Children.Remove(sceneBorder); sceneBorder.Child = null; Content = stage; hint.Visibility = Visibility.Collapsed; }
            else Content = root;
            BuildScene(); SetCamera(); if (!compact) SelectPart("gpu");
            stage.MouseLeftButtonDown += (s, e) => { stage.Focus(); _down = _previous = e.GetPosition(stage); _dragging = false; stage.CaptureMouse(); e.Handled = true; };
            stage.MouseMove += (s, e) =>
            {
                if (_down == null || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(stage); if ((p - _down.Value).Length > 4) _dragging = true;
                _targetYaw += (p.X - _previous.X) * .5; _targetPitch = Math.Clamp(_targetPitch + (p.Y - _previous.Y) * .35, -65, 65); _previous = p; Animate();
            };
            stage.MouseLeftButtonUp += (s, e) => { if (_down != null && !_dragging) Pick(e.GetPosition(_viewport)); _down = null; stage.ReleaseMouseCapture(); SaveWidgetCamera(); e.Handled = true; };
            stage.LostMouseCapture += (s, e) => _down = null;
            stage.MouseWheel += (s, e) => { Zoom(e.Delta > 0 ? -1 : 1); SaveWidgetCamera(); e.Handled = true; };
            stage.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Left) _targetYaw -= 15; else if (e.Key == Key.Right) _targetYaw += 15;
                else if (e.Key == Key.Up) _targetPitch = Math.Clamp(_targetPitch - 10, -65, 65); else if (e.Key == Key.Down) _targetPitch = Math.Clamp(_targetPitch + 10, -65, 65);
                else if (e.Key == Key.Add || e.Key == Key.OemPlus) Zoom(-1); else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) Zoom(1); else return;
                Animate(); e.Handled = true;
            };
            _spin.Click += (s, e) => Animate();
            _cameraTimer.Tick += (s, e) =>
            {
                if (!IsVisible) { _cameraTimer.Stop(); return; }
                if (_spin.IsChecked == true && !App.Store.Settings.ReducedEffects) _targetYaw += .35;
                _yaw += (_targetYaw - _yaw) * .18; _pitch += (_targetPitch - _pitch) * .18; _distance += (_targetDistance - _distance) * .18; SetCamera();
                if (_spin.IsChecked != true && Math.Abs(_targetYaw - _yaw) + Math.Abs(_targetPitch - _pitch) + Math.Abs(_targetDistance - _distance) < .02) _cameraTimer.Stop();
            };
            Unloaded += (s, e) => _cameraTimer.Stop(); IsVisibleChanged += (s, e) => { if (!IsVisible) _cameraTimer.Stop(); else if (_spin.IsChecked == true) Animate(); };
        }
        private void Animate() { if (App.Store.Settings.ReducedEffects) { _yaw = _targetYaw; _pitch = _targetPitch; _distance = _targetDistance; SetCamera(); } else if (IsVisible) _cameraTimer.Start(); }
        private void SaveWidgetCamera()
        { if (!_compact) return; App.Store.Settings.PcWidgetYaw = _targetYaw; App.Store.Settings.PcWidgetPitch = _targetPitch; App.Store.Settings.PcWidgetDistance = _targetDistance; App.Store.Save(); }
        internal string ModelVariant => _sceneKey;
        private void SetCamera()
        {
            double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
            var position = new Point3D(Math.Sin(yaw) * Math.Cos(pitch) * _distance, Math.Sin(pitch) * _distance, Math.Cos(yaw) * Math.Cos(pitch) * _distance);
            _camera.Position = position; _camera.LookDirection = new Vector3D(-position.X, -position.Y, -position.Z); _camera.UpDirection = new Vector3D(0, 1, 0);
        }
        internal void ResetCamera() { _targetYaw = 32; _targetPitch = 15; _targetDistance = _compact ? 18 : 14; _spin.IsChecked = false; Animate(); }
        internal void Zoom(double change) { _targetDistance = Math.Clamp(_targetDistance + change, 7, 22); Animate(); }
        private void Pick(Point point)
        {
            VisualTreeHelper.HitTest(_viewport, null, result =>
            {
                if (result is RayMeshGeometry3DHitTestResult hit && hit.ModelHit is GeometryModel3D geometry && _parts.TryGetValue(geometry, out var id))
                { SelectPart(id); return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(point));
        }
        internal void SelectPart(string id)
        {
            if (!_buttons.ContainsKey(id)) return; _selected = id;
            foreach (var pair in _original)
            {
                if (_parts.TryGetValue(pair.Key, out var part) && part == id)
                {
                    var group = new MaterialGroup(); group.Children.Add(pair.Value); group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(95, 48, 165, 189)))); pair.Key.Material = group;
                }
                else pair.Key.Material = pair.Value;
            }
            foreach (var pair in _buttons) { pair.Value.SetResourceReference(Button.BackgroundProperty, pair.Key == id ? "Accent" : "ControlBg"); pair.Value.SetResourceReference(Button.ForegroundProperty, pair.Key == id ? "AccentInk" : "FgMain"); }
            Refresh();
        }
        public void Refresh()
        {
            var h = App.Monitor.Hardware.Snapshot; var s = App.Monitor.LastSample;
            string key = (App.Store.Settings.PcModelChassis == "Auto" ? h.System.IsLaptop ? "Laptop" : "Desktop" : App.Store.Settings.PcModelChassis) + "/" + App.Store.Settings.PcModelCooling;
            if (_sceneKey != key) { BuildScene(); if (!_compact) SelectPart(_selected); }
            bool fresh = DateTime.UtcNow.Ticks - s.TimestampUtc < TimeSpan.TicksPerSecond * 15;
            string N(double value, string unit) => PassportService.Number(fresh ? value : double.NaN, unit);
            _partTitle.Text = _selected switch { "cpu" => "Процессор", "gpu" => "Видеокарта", "ram" => "Память", "board" => "Материнская плата", "disks" => "Накопители", _ => "Охлаждение" };
            _facts.Text = _selected switch
            {
                "cpu" => PassportService.Text(h.Cpu.Model) + "\n" + (h.Cpu.Cores > 0 ? h.Cpu.Cores + " ядер · " + h.Cpu.Threads + " потоков" : "Ядра и потоки: Нет данных"),
                "gpu" => PassportService.Text(PassportService.GpuName()) + "\nДрайвер: " + PassportService.Text(h.Gpu.DriverVersion),
                "ram" => PassportService.Number(h.Ram.InstalledGb, "GiB") + " · " + PassportService.Text(h.Ram.MemoryType) + "\n" + h.Ram.Modules.Count + " модулей · " + PassportService.Number(h.Ram.MaxSpeedMhz, "MT/s"),
                "board" => PassportService.Text(h.Motherboard.Manufacturer) + "\n" + PassportService.Text(h.Motherboard.Model) + "\nBIOS: " + PassportService.Text(h.Motherboard.BiosVersion),
                "disks" => string.Join("\n\n", App.Monitor.Storage.PhysicalDrives.Select(d => PassportService.Text(d.Model) + "\n" + PassportService.Text(d.Kind) + " · " + PassportService.Number(d.CapacityGb, "GiB"))),
                _ => _sceneKey?.StartsWith("Laptop", StringComparison.Ordinal) == true ? "Встроенная система охлаждения ноутбука показана условно. Внешний башенный кулер или СЖО на этой модели не устанавливаются." : App.Store.Settings.PcModelCooling == "Aio" ? "Модель СЖО: помпа, трубки и верхний радиатор. Тип выбран пользователем." : App.Store.Settings.PcModelCooling == "Air" ? "Башенный воздушный кулер. Тип выбран пользователем." : "Windows не сообщила тип охлаждения. Воздушный кулер показан условно; выбери свой тип выше."
            };
            if (_facts.Text.Length == 0) _facts.Text = "Нет данных";
            _live.Text = "\nДАТЧИКИ · LIVE\n" + (_selected switch
            {
                "cpu" => "Температура: " + N(s.CpuTempC, "°C") + "\nНагрузка: " + N(s.CpuPercent, "%") + "\nЧастота: " + N(s.CpuGhz * 1000, "MHz"),
                "gpu" => "Температура: " + N(s.GpuTempC, "°C") + "\nНагрузка: " + N(s.GpuPercent, "%") + "\nЯдро: " + N(s.GpuClockMhz, "MHz") + "\nПамять: " + N(s.GpuMemClockMhz, "MHz") + "\nVRAM: " + N(s.GpuVramUsedGb, "GiB") + " / " + N(s.GpuVramTotalGb, "GiB"),
                "ram" => "Занято: " + N(s.RamUsedGb, "GiB") + "\nНагрузка: " + N(s.RamPercent, "%"),
                "disks" => "Чтение: " + N(s.DiskReadKBps, "KiB/s") + "\nЗапись: " + N(s.DiskWriteKBps, "KiB/s"),
                "cooling" => "CPU: " + N(s.CpuTempC, "°C") + "\nGPU: " + N(s.GpuTempC, "°C"),
                _ => "Для платы отдельные датчики не сопоставлены. Нет данных."
            });
        }
        private void BuildScene()
        {
            _viewport.Children.Clear(); _parts.Clear(); _original.Clear();
            bool laptop = App.Store.Settings.PcModelChassis == "Laptop" || (App.Store.Settings.PcModelChassis == "Auto" && App.Monitor.Hardware.Snapshot.System.IsLaptop);
            _sceneKey = (laptop ? "Laptop" : "Desktop") + "/" + App.Store.Settings.PcModelCooling;
            var scene = new Model3DGroup(); scene.Children.Add(new AmbientLight(Color.FromRgb(67, 78, 101)));
            scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-3, -4, -6)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(129, 89, 230), new Vector3D(4, 2, 1)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(72, 205, 212), new Vector3D(-3, 1, 3)));
            void Box(string id, double x, double y, double z, double w, double h, double d, Color color) => Add(scene, id, BoxMesh(x, y, z, w, h, d), color);
            Color chassis = Color.FromRgb(35, 48, 67), teal = Color.FromRgb(58, 210, 202), purple = Color.FromRgb(144, 113, 220);
            if (laptop) { BuildLaptop(scene); _viewport.Children.Add(new ModelVisual3D { Content = scene }); return; }
            Box(null, 0, -3, 0, 4.2, .18, 3.1, chassis); Box(null, 0, 3, 0, 4.2, .18, 3.1, chassis); Box(null, 0, 0, -1.55, 4.2, 6, .12, chassis);
            foreach (double x in new[] { -2.06, 2.06 }) foreach (double z in new[] { -1.5, 1.5 }) Box(null, x, 0, z, .09, 6, .09, chassis);
            foreach (double x in new[] { -2.06, 2.06 }) Box(null, x, 0, 1.57, .025, 5.85, .04, teal);
            Box("board", -.32, .38, -1.36, 3.35, 4.45, .15, Color.FromRgb(18, 45, 52));
            Box("cpu", -.85, 1.15, -1.05, .78, .78, .24, Color.FromRgb(151, 167, 190));
            if (App.Store.Settings.PcModelCooling == "Aio")
            {
                Add(scene, "cooling", Cylinder(new Point3D(-.85, 1.15, -.82), .42, .22), chassis);
                Ring(scene, "cooling", new Point3D(-.85, 1.15, -.69), .35, .045, teal);
                Box("cooling", -.4, 2.65, -.15, 2.5, .22, 2.1, chassis);
                for (int i = 0; i < 20; i++) Box("cooling", -1.55 + i * .12, 2.78, -.15, .025, .08, 1.85, Color.FromRgb(75, 86, 100));
                for (int side = 0; side < 2; side++)
                {
                    Point3D prev = new Point3D(-.67 + side * .2, 1.25, -.63);
                    for (int i = 1; i <= 32; i++)
                    {
                        double t = i / 32d;
                        var next = new Point3D(-.67 + side * .2 + Math.Sin(t * Math.PI) * .55, 1.25 + t * 1.35, -.63 + Math.Sin(t * Math.PI) * .65);
                        Add(scene, "cooling", Tube(prev, next, .075), Color.FromRgb(35, 43, 56)); prev = next;
                    }
                }
            }
            else
            {
                for (int i = 0; i < 16; i++) Box("cooling", -.85, .63 + i * .075, -.5, 1.05, .032, .85, Color.FromRgb(148, 157, 171));
                for (int i = 0; i < 4; i++) Add(scene, "cooling", Tube(new Point3D(-1.15 + i * .2, .45, -.55), new Point3D(-1.15 + i * .2, 1.83, -.55), .04), Color.FromRgb(188, 138, 83));
                Box("cooling", -.85, 1.2, .015, 1.08, 1.25, .12, chassis);
                Ring(scene, "cooling", new Point3D(-.85, 1.2, .095), .47, .05, teal);
            }
            for (int i = 0; i < 7; i++) Box("board", -.95 + i * .24, 2.15, -1.19, .14, .14, .12, Color.FromRgb(93, 108, 131));
            for (int i = 0; i < 3; i++) Box("board", -.9, -.05 - i * .32, -1.18, 1.65, .09, .12, chassis);
            for (int i = 0; i < 6; i++) Box("board", 1.32, -.45 - i * .17, -1.17, .12, .09, .17, Color.FromRgb(90, 102, 119));
            for (int i = 0; i < 4; i++) { Box("ram", .25 + i * .31, 1, -.88, .16, 1.8, .35, Color.FromRgb(64, 49, 98)); Box("ram", .25 + i * .31, 1.95, -.65, .16, .06, .25, purple); }
            Box("gpu", -.35, -.59, .1, 3.2, .52, 1.96, Color.FromRgb(35, 54, 68)); Box("gpu", -.35, -.3, .65, 3.05, .05, .6, teal);
            for (int i = 0; i < 3; i++) Ring(scene, "gpu", new Point3D(-1.37 + i * 1.02, -.57, 1.11), .35, .04, Color.FromRgb(87, 178, 192));
            Box(null, -.6, -2.37, -.1, 2.8, .9, 2.75, chassis);
            for (int i = 0; i < Math.Clamp(App.Monitor.Storage.PhysicalDrives.Count, 1, 4); i++) { Box("disks", 1.23, -1.25 - i * .44, -.4, .88, .32, 1.85, Color.FromRgb(50, 65, 83)); Box("disks", 1.23, -1.17 - i * .44, .56, .5, .035, .05, purple); }
            for (int i = 0; i < 3; i++) Box("cooling", 1.48, 2.13 - i * 1.36, 1.25, 1.08, 1.08, .14, chassis);
            for (int i = 0; i < 3; i++) Ring(scene, "cooling", new Point3D(1.48, 2.13 - i * 1.36, 1.36), .47, .06, i % 2 == 0 ? teal : purple);
            foreach (double x in new[] { -1.55, 1.55 }) foreach (double z in new[] { -1.05, 1.05 }) Box(null, x, -3.15, z, .5, .32, .65, chassis);
            for (int i = 0; i < 22; i++) Box(null, -.7 + i * .12, -2.12, 1.33, .06, .37, .035, Color.FromRgb(67, 78, 94));
            Box(null, 0, -3.36, 0, 5.9, .08, 4.5, Color.FromRgb(15, 24, 37));
            _viewport.Children.Add(new ModelVisual3D { Content = scene });
        }
        private void Add(Model3DGroup scene, string id, MeshGeometry3D mesh, Color color)
        {
            mesh.Freeze(); var material = new MaterialGroup(); var brush = new SolidColorBrush(color); brush.Freeze(); material.Children.Add(new DiffuseMaterial(brush)); material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(100, 122, 140)), 38)); material.Freeze();
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material }; _original[model] = material; if (id != null) _parts[model] = id; scene.Children.Add(model);
        }
        private void Ring(Model3DGroup scene, string id, Point3D center, double radius, double thickness, Color color)
        {
            var mesh = new MeshGeometry3D();
            for (int i = 0; i < 32; i++)
            {
                double a = i * Math.PI * 2 / 32, b = (i + 1) * Math.PI * 2 / 32; int start = mesh.Positions.Count;
                foreach (var p in new[] { (a, radius), (a, radius - thickness), (b, radius - thickness), (b, radius) }) { mesh.Positions.Add(new Point3D(center.X + Math.Cos(p.Item1) * p.Item2, center.Y + Math.Sin(p.Item1) * p.Item2, center.Z)); mesh.Normals.Add(new Vector3D(0, 0, 1)); }
                foreach (int k in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(start + k);
            }
            Add(scene, id, mesh, color);

            Add(scene, id, Cylinder(new Point3D(center.X, center.Y, center.Z + .02), radius * .21, .08), Color.FromRgb(99, 115, 136));
            for (int blade = 0; blade < 9; blade++)
            {
                var fan = new MeshGeometry3D();
                for (int step = 0; step <= 10; step++)
                {
                    double t = step / 10d, r = radius * (.19 + t * .68), a = blade * Math.PI * 2 / 9 + t * .53;
                    foreach (double offset in new[] { 0d, .29 * t + .12 })
                    { fan.Positions.Add(new Point3D(center.X + Math.Cos(a + offset) * r, center.Y + Math.Sin(a + offset) * r, center.Z + .009)); fan.Normals.Add(new Vector3D(0, 0, 1)); }
                    if (step > 0) foreach (int k in new[] { 0, 1, 3, 0, 3, 2 }) fan.TriangleIndices.Add((step - 1) * 2 + k);
                }
                Add(scene, id, fan, Color.FromRgb(85, 98, 120));
            }
        }
        private void BuildLaptop(Model3DGroup scene)
        {
            Color metal = Color.FromRgb(63, 75, 99), accent = Color.FromRgb(75, 221, 214);
            void B(string id, double x, double y, double z, double w, double h, double d, Color c) => Add(scene, id, BoxMesh(x, y, z, w, h, d), c);
            B("board", 0, -.95, .4, 6.3, .24, 3.8, metal);
            B("cpu", 0, -.8, -.6, 6.15, .035, 1.4, Color.FromRgb(20, 28, 42));
            for (int row = 0; row < 5; row++) for (int col = 0; col < 17; col++)
            { B("cpu", -2.82 + col * .35, -.77, -.94 + row * .28, .28, .025, .21, Color.FromRgb(50, 63, 82)); }
            B("ram", 0, -.8, 1.2, 1.8, .018, .9, Color.FromRgb(40, 52, 74));
            B("board", 0, .65, -1.54, 6.3, 3.48, .18, metal);
            B("gpu", 0, .68, -1.43, 5.96, 3.1, .025, Color.FromRgb(8, 17, 34));
            for (int i = 0; i < 14; i++)
                B("gpu", -2.72 + i * .42, .02 + Math.Sin(i * .53) * .34, -1.408, .22, .55 + Math.Cos(i * .4) * .4, .012, i % 3 == 0 ? Color.FromRgb(134, 93, 233) : accent);
            B("gpu", 0, 1.62, -1.4, 2.4, .055, .014, accent);
            B("gpu", 0, 1.35, -1.4, 1.4, .026, .014, Color.FromRgb(153, 173, 199));
            for (int i = 0; i < 20; i++) B("cooling", -2.6 + i * .27, -.86, -1.35, .12, .05, .08, Color.FromRgb(12, 22, 38));
            B("disks", -2.65, -.95, .3, .5, .06, .13, Color.FromRgb(20, 28, 42));
        }
        private static MeshGeometry3D Cylinder(Point3D center, double radius, double depth) => Tube(new Point3D(center.X, center.Y, center.Z - depth / 2), new Point3D(center.X, center.Y, center.Z + depth / 2), radius);
        private static MeshGeometry3D Tube(Point3D from, Point3D to, double radius)
        {
            var mesh = new MeshGeometry3D(); var axis = to - from; axis.Normalize();
            var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < .9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)); u.Normalize(); var v = Vector3D.CrossProduct(axis, u);
            for (int i = 0; i <= 24; i++)
            {
                double angle = i * Math.PI * 2 / 24; var normal = u * Math.Cos(angle) + v * Math.Sin(angle);
                mesh.Positions.Add(from + normal * radius); mesh.Positions.Add(to + normal * radius); mesh.Normals.Add(normal); mesh.Normals.Add(normal);
                if (i > 0) foreach (int k in new[] { 0, 1, 3, 0, 3, 2 }) mesh.TriangleIndices.Add((i - 1) * 2 + k);
            }
            foreach (bool top in new[] { false, true })
            {
                Point3D center = top ? to : from; Vector3D normal = top ? axis : -axis; int start = mesh.Positions.Count;
                mesh.Positions.Add(center); mesh.Normals.Add(normal);
                for (int i = 0; i <= 24; i++) { double a = i * Math.PI * 2 / 24; mesh.Positions.Add(center + (u * Math.Cos(a) + v * Math.Sin(a)) * radius); mesh.Normals.Add(normal); }
                for (int i = 0; i < 24; i++) { mesh.TriangleIndices.Add(start); mesh.TriangleIndices.Add(start + i + (top ? 1 : 2)); mesh.TriangleIndices.Add(start + i + (top ? 2 : 1)); }
            }
            return mesh;
        }

        private static MeshGeometry3D BoxMesh(double x, double y, double z, double w, double h, double d)
        {
            var mesh = new MeshGeometry3D(); var p = new[] { new Point3D(x-w/2,y-h/2,z-d/2), new Point3D(x+w/2,y-h/2,z-d/2), new Point3D(x+w/2,y+h/2,z-d/2), new Point3D(x-w/2,y+h/2,z-d/2), new Point3D(x-w/2,y-h/2,z+d/2), new Point3D(x+w/2,y-h/2,z+d/2), new Point3D(x+w/2,y+h/2,z+d/2), new Point3D(x-w/2,y+h/2,z+d/2) };
            int[][] faces = { new[] {4,5,6,7}, new[] {1,0,3,2}, new[] {0,4,7,3}, new[] {5,1,2,6}, new[] {3,7,6,2}, new[] {0,1,5,4} };
            Vector3D[] normals = { new Vector3D(0,0,1), new Vector3D(0,0,-1), new Vector3D(-1,0,0), new Vector3D(1,0,0), new Vector3D(0,1,0), new Vector3D(0,-1,0) };
            for (int i = 0; i < faces.Length; i++) { int start = mesh.Positions.Count; foreach (int k in faces[i]) { mesh.Positions.Add(p[k]); mesh.Normals.Add(normals[i]); } foreach (int k in new[] {0,1,2,0,2,3}) mesh.TriangleIndices.Add(start+k); }
            return mesh;
        }
    }
}
