using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Services;

namespace Burmalk.Views
{
    internal sealed class CpuFanControlPanel : UserControl, IRefreshable
    {
        private readonly TextBlock _status = ViewUi.Text("Проверка поддержки…");
        private readonly TextBlock _rpm = ViewUi.Text("Обороты: недоступно");
        private readonly StackPanel _editorHost = new();
        private readonly Button _apply, _automatic, _configure;
        private TuningSetting _control;
        private BoardFanSensor _sensor;
        private TuningEditor _editor;

        public CpuFanControlPanel()
        {
            var body = new StackPanel();
            body.Children.Add(_status); body.Children.Add(_rpm); body.Children.Add(_editorHost);
            var actions = new WrapPanel();
            _apply = ViewUi.Button("Применить скорость", () => Execute(() =>
            {
                if (_control == null || _editor == null) return;
                App.Tuning.Apply(new Dictionary<TuningSetting, double> { [_control] = _editor.Desired() });
            }));
            _automatic = ViewUi.Button("Авто", () => Execute(() => { if (_control != null) App.Tuning.Automatic(_control); }));
            actions.Children.Add(_apply); actions.Children.Add(_automatic);
            actions.Children.Add(ViewUi.Button("Вернуть предыдущую скорость", () => Execute(() => App.Tuning.Restore("CPU FAN"))));
            body.Children.Add(actions);
            _configure = ViewUi.Button("Настроить кулер CPU", Configure); body.Children.Add(_configure);
            body.Children.Add(ViewUi.Text("40–100% · Авто передаёт управление плате. При обычном выходе возвращается предыдущая настройка."));
            if (!CpuSensorAccess.IsAdministrator) body.Children.Add(ViewUi.Button("Разрешить доступ к оборудованию", CpuSensorAccess.Enable));
            Content = ViewUi.Panel("CPU · СКОРОСТЬ КУЛЕРА", body);
            SetBinding(null, null); _configure.IsEnabled = false;
            Initialize();
        }

        private async void Initialize()
        {
            await App.Tuning.Ready;
            ReloadBinding();
        }

        private void ReloadBinding()
        {
            var saved = App.Store.Settings;
            var resolved = CpuFanBinding.Resolve(App.Tuning.Settings("CPU FAN"), App.Tuning.BoardFans(),
                App.Tuning.BoardId, saved.CpuFanBoardId, saved.CpuFanControlId, saved.CpuFanRpmSensorId);
            SetBinding(resolved.Control, resolved.Sensor);
            _configure.IsEnabled = App.Tuning.BoardId.Length > 0 && App.Tuning.Settings("CPU FAN").Length > 0 && App.Tuning.BoardFans().Length > 0;
            _status.Text = _control != null ? "Кулер CPU готов к управлению." : !CpuSensorAccess.IsAdministrator
                ? "Для управления кулером разреши доступ к оборудованию."
                : _configure.IsEnabled ? "Нужно один раз определить кулер CPU: нажми «Настроить кулер CPU»."
                : "Плата не предоставила управление кулером CPU. Используй настройку вентилятора в BIOS.";
            _configure.Content = _control != null ? "Изменить назначение кулера CPU" : "Настроить кулер CPU";
            Refresh();
        }

        private void SetBinding(TuningSetting control, BoardFanSensor sensor)
        {
            _control = control; _sensor = sensor; _editor = null; _editorHost.Children.Clear();
            if (control != null && sensor != null)
            {
                _editor = new TuningEditor(control, displayName: "Скорость кулера CPU");
                _editorHost.Children.Add(_editor);
            }
            else
            {
                _editorHost.Children.Add(ViewUi.Text("Скорость кулера CPU · %", "Title"));
                _editorHost.Children.Add(new Slider { Minimum = 40, Maximum = 100, IsEnabled = false, Margin = new Thickness(0, 8, 0, 12) });
            }
            _apply.IsEnabled = _automatic.IsEnabled = control != null && sensor != null;
        }

        private void Configure()
        {
            // A board's generic channel order does not identify the physical CPU header.
            // Selection only saves the binding; no speed is written by this dialog.
            var controls = App.Tuning.Settings("CPU FAN"); var sensors = App.Tuning.BoardFans();
            var controlChoice = new ComboBox { ItemsSource = controls, SelectedItem = _control, MinWidth = 300 };
            var rpmChoice = new ComboBox { ItemsSource = sensors, SelectedItem = _sensor, MinWidth = 300 };
            var confirm = new CheckBox { Content = "Я сверил эти каналы с CPU_FAN в BIOS", IsChecked = _control != null, Margin = new Thickness(0, 12, 0, 12) };
            var message = ViewUi.Text("Названия Fan # не обозначают разъём. Выбери только каналы, которые ты сверил с CPU_FAN в BIOS. Назначение сохранится; скорость здесь не меняется.");
            var body = new StackPanel { Margin = new Thickness(20) };
            body.Children.Add(message); body.Children.Add(ViewUi.Text("Канал управления скоростью")); body.Children.Add(controlChoice);
            body.Children.Add(ViewUi.Text("Датчик оборотов")); body.Children.Add(rpmChoice); body.Children.Add(confirm);
            var dialog = new Window { Title = "Burmalk · назначение кулера CPU", Owner = Window.GetWindow(this),
                Width = 640, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
            dialog.SetResourceReference(Window.BackgroundProperty, "ControlBg");
            var save = ViewUi.Button("Сохранить", () =>
            {
                if (confirm.IsChecked != true || controlChoice.SelectedItem is not TuningSetting c || rpmChoice.SelectedItem is not BoardFanSensor r) return;
                try
                {
                    var saved = App.Store.Settings;
                    saved.CpuFanBoardId = App.Tuning.BoardId; saved.CpuFanControlId = c.Id; saved.CpuFanRpmSensorId = r.Id;
                    App.Store.Save(); dialog.DialogResult = true;
                }
                catch (Exception ex) { message.Text = ex.Message; }
            });
            void UpdateSave() => save.IsEnabled = confirm.IsChecked == true && controlChoice.SelectedItem != null && rpmChoice.SelectedItem != null;
            controlChoice.SelectionChanged += (_, _) => { confirm.IsChecked = false; UpdateSave(); };
            rpmChoice.SelectionChanged += (_, _) => { confirm.IsChecked = false; UpdateSave(); };
            confirm.Checked += (_, _) => UpdateSave(); confirm.Unchecked += (_, _) => UpdateSave();
            var actions = new WrapPanel(); actions.Children.Add(save); actions.Children.Add(ViewUi.Button("Закрыть", () => dialog.Close())); body.Children.Add(actions);
            UpdateSave(); if (dialog.ShowDialog() == true) ReloadBinding();
        }

        private void Execute(Action action)
        {
            try { action(); _status.Text = "Настройка применена."; Refresh(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        public void Refresh()
        {
            _editor?.Refresh();
            if (_sensor == null) { _rpm.Text = "Обороты: недоступно"; return; }
            try { double rpm = App.Tuning.Read(_sensor); _rpm.Text = "Обороты: " + (double.IsFinite(rpm) ? rpm.ToString("0") + " RPM" : "недоступно"); }
            catch { _rpm.Text = "Обороты: недоступно"; }
        }
    }
}
