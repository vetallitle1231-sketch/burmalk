using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Burmalk.Services;

namespace Burmalk.Views
{
    internal sealed class TuningEditor : StackPanel
    {
        public TuningSetting Setting { get; }
        public bool Selected => _selected == null || _selected.IsChecked == true;
        private readonly CheckBox _selected;
        private readonly Slider _slider;
        private readonly TextBlock _live = ViewUi.Text("");
        private readonly TextBox _number = new() { Width = 85, Margin = new Thickness(10, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        public TuningEditor(TuningSetting setting, bool optional = false, string displayName = null)
        {
            Setting = setting; Margin = new Thickness(0, 10, 0, 10);
            Children.Add(ViewUi.Text(displayName ?? setting.Name, "Title")); Children.Add(_live);
            var row = new DockPanel(); DockPanel.SetDock(_number, Dock.Right); row.Children.Add(_number);
            _slider = new Slider { Minimum = setting.Minimum, Maximum = setting.Maximum, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 8, 8, 8) };
            double value; try { value = App.Tuning.Read(setting); } catch { value = setting.Minimum; }
            _slider.Value = double.IsFinite(value) ? Math.Clamp(value, setting.Minimum, setting.Maximum) : setting.Minimum;
            _number.Text = _slider.Value.ToString("0");
            _slider.ValueChanged += (_, _) => _number.Text = _slider.Value.ToString("0");
            row.Children.Add(_slider); Children.Add(row);
            if (optional)
            {
                _selected = new CheckBox { Content = "Изменить эту настройку" };
                _slider.IsEnabled = false; _number.IsEnabled = false;
                _selected.Checked += (_, _) => { _slider.IsEnabled = true; _number.IsEnabled = true; };
                _selected.Unchecked += (_, _) => { _slider.IsEnabled = false; _number.IsEnabled = false; };
                Children.Insert(2, _selected);
            }
            Children.Add(ViewUi.Text("Диапазон: " + setting.Minimum + " — " + setting.Maximum + " " + setting.Unit));
            Refresh();
        }
        public double Desired()
        {
            if (!double.TryParse(_number.Text, out double value) || value != Math.Round(value)) throw new InvalidOperationException("Введи целое число для «" + Setting.Name + "».");
            return value;
        }
        public void Refresh()
        {
            try { double v = App.Tuning.Read(Setting); _live.Text = double.IsFinite(v) ? "Сейчас: " + v.ToString("0.##") + " " + Setting.Unit : "Текущая скорость недоступна"; }
            catch (Exception ex) { _live.Text = "Недоступно: " + ex.Message; }
        }
    }

    internal sealed class FanControlPanel : UserControl, IRefreshable
    {
        private readonly TextBlock _status = ViewUi.Text("Проверка поддержки…");
        private readonly ComboBox _choice = new() { MinWidth = 250, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly StackPanel _editorHost = new();
        private readonly Button _apply, _automatic;
        private readonly CpuFanControlPanel _cpuPanel;
        private TuningEditor _editor;
        public FanControlPanel(string kind)
        {
            if (kind == "CPU") { _cpuPanel = new CpuFanControlPanel(); Content = _cpuPanel; return; }
            var body = new StackPanel(); body.Children.Add(_status); body.Children.Add(_choice); body.Children.Add(_editorHost);
            _choice.SelectionChanged += (_, _) =>
            {
                _editorHost.Children.Clear(); _editor = null;
                if (_choice.SelectedItem is TuningSetting setting) { _editor = new TuningEditor(setting); _editorHost.Children.Add(_editor); }
                UpdateActions();
            };
            var actions = new WrapPanel();
            _apply = ViewUi.Button("Применить скорость", () => Execute(() =>
            {
                if (_editor != null) App.Tuning.Apply(new Dictionary<TuningSetting, double> { [_editor.Setting] = _editor.Desired() });
            }));
            _automatic = ViewUi.Button("Авто · управление прошивкой", () => Execute(() => { if (_editor != null) App.Tuning.Automatic(_editor.Setting); }));
            actions.Children.Add(_apply); actions.Children.Add(_automatic);
            actions.Children.Add(ViewUi.Button("Вернуть предыдущие настройки", () => Execute(() => App.Tuning.Restore("GPU FAN"))));
            body.Children.Add(actions);
            body.Children.Add(ViewUi.Text("Ручная скорость: от 40% до 100% в пределах драйвера. При обычном выходе восстанавливаются предыдущие настройки."));
            if (!CpuSensorAccess.IsAdministrator) body.Children.Add(ViewUi.Button("Разрешить доступ к оборудованию", CpuSensorAccess.Enable));
            Content = ViewUi.Panel("GPU · ВЕНТИЛЯТОРЫ", body); UpdateActions(); Initialize();
        }
        private async void Initialize()
        {
            await App.Tuning.Ready;
            _status.Text = App.Tuning.GpuStatus;
            var controls = App.Tuning.Settings("GPU FAN"); _choice.ItemsSource = controls;
            if (controls.Length > 0) _choice.SelectedIndex = 0;
            else { _choice.Visibility = Visibility.Collapsed; _status.Text += "\nПоддерживаемый контроллер не обнаружен."; }
            UpdateActions(); Refresh();
        }
        private void UpdateActions() { if (_apply != null) _apply.IsEnabled = _editor != null; if (_automatic != null) _automatic.IsEnabled = _editor != null; }
        private void Execute(Action action)
        {
            try { action(); _status.Text = "Настройка применена."; Refresh(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        public void Refresh() { if (_cpuPanel != null) _cpuPanel.Refresh(); else _editor?.Refresh(); }
    }
    internal sealed class OverclockView : UserControl, IRefreshable
    {
        private readonly StackPanel _root = new();
        private readonly StackPanel _controls = new();
        private readonly Border _warning;
        private readonly CheckBox _consent = new() { Content = "Я понимаю риск повреждения комплектующих и принимаю ответственность", Margin = new Thickness(0, 16, 0, 8) };
        private readonly TextBlock _live = ViewUi.Text("");
        private readonly TextBlock _status = ViewUi.Text("");
        private readonly List<TuningEditor> _editors = new();
        public bool Accepted { get; private set; }
        public OverclockView()
        {
            var text = ViewUi.Text("За повреждения ваших комплектующих никто не отвечает. Вы уверены?\nРазгон может вызвать перегрев, сбои и потерю данных. Галочка открывает настройки; значения применяются отдельной кнопкой.", "Title");
            text.TextWrapping = TextWrapping.Wrap; text.Foreground = Brushes.IndianRed;
            var enter = ViewUi.Button("Я согласен · открыть OVERCLOCK", () => { if (_consent.IsChecked != true) return; Accepted = true; _warning.Visibility = Visibility.Collapsed; _controls.Visibility = Visibility.Visible; });
            enter.IsEnabled = false;
            _consent.Checked += (_, _) => enter.IsEnabled = true; _consent.Unchecked += (_, _) => enter.IsEnabled = false;
            _warning = ViewUi.Panel("OVERCLOCK · ВНИМАНИЕ", text, _consent, enter);
            _warning.BorderBrush = Brushes.IndianRed; _warning.BorderThickness = new Thickness(2);
            _root.Children.Add(_warning); _root.Children.Add(_controls); Content = _root;
            _status.TextWrapping = TextWrapping.Wrap;
            _controls.Children.Add(ViewUi.Panel("OVERCLOCK · МОНИТОРИНГ", _live, _status));
            if (!CpuSensorAccess.IsAdministrator) _controls.Children.Add(ViewUi.Button("Разрешить доступ к оборудованию", CpuSensorAccess.Enable));
            _controls.Children.Add(ViewUi.Text("При обычном выходе Burmalk возвращает исходные значения. При CPU ≥90°C / GPU ≥85°C или потере температуры изменения отменяются. Автоприменения при запуске нет."));
            Loaded += (_, _) => ResetGate(); Unloaded += (_, _) => ResetGate();
            ResetGate(); Initialize();
        }
        private void ResetGate() { Accepted = false; _consent.IsChecked = false; _controls.Visibility = Visibility.Collapsed; _warning.Visibility = Visibility.Visible; }
        private async void Initialize()
        {
            await App.Tuning.Ready;
            AddGroup("GPU", "BOOST GPU", App.Tuning.GpuStatus);
            AddGroup("CPU", "BOOST CPU · WINDOWS", App.Tuning.CpuStatus);
        }
        private void AddGroup(string group, string title, string description)
        {
            var body = new StackPanel(); var info = ViewUi.Text(description); info.TextWrapping = TextWrapping.Wrap; body.Children.Add(info);
            var editors = App.Tuning.Settings(group).Select(s => new TuningEditor(s, true)).ToArray();
            foreach (var editor in editors) { _editors.Add(editor); body.Children.Add(editor); }
            if (editors.Length == 0) body.Children.Add(ViewUi.Text("Поддерживаемые настройки не обнаружены."));
            var actions = new WrapPanel();
            var apply = ViewUi.Button("Применить " + title, () => Execute(() =>
            {
                if (!Accepted) throw new InvalidOperationException("Сначала прими предупреждение.");
                var selected = editors.Where(e => e.Selected).ToArray();
                if (selected.Length == 0) throw new InvalidOperationException("Отметь настройки, которые хочешь изменить.");
                App.Tuning.Apply(selected.ToDictionary(e => e.Setting, e => e.Desired()));
            }));
            apply.IsEnabled = editors.Length > 0; actions.Children.Add(apply);
            actions.Children.Add(ViewUi.Button("Вернуть значения до Burmalk", () => Execute(() => App.Tuning.Restore(group))));
            body.Children.Add(actions); _controls.Children.Add(ViewUi.Panel(title, body));
        }
        private void Execute(Action action) { try { action(); _status.Text = "Настройки применены и прочитаны обратно."; Refresh(); } catch (Exception ex) { _status.Text = ex.Message; } }
        public void Refresh()
        {
            var s = App.Monitor.LastSample;
            _live.Text = "GPU  " + Fmt.C(s.GpuTempC) + " · " + MetricView.Number(s.GpuClockMhz, "MHz") + " · " + Fmt.W(s.GpuPowerW) + "\nCPU  " + Fmt.C(s.CpuTempC) + " · " + MetricView.Number(s.CpuGhz * 1000, "MHz");
            if (Accepted) foreach (var editor in _editors) editor.Refresh();
            if (App.Tuning.LastProtection.Length > 0) _status.Text = App.Tuning.LastProtection;
        }
    }
}
