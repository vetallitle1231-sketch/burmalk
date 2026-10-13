using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views
{
    public sealed class PassportView : UserControl, IRefreshable
    {
        private readonly Image _preview = new Image { Stretch = Stretch.Uniform, MaxHeight = 680, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly StackPanel _details = new StackPanel();
        private readonly TextBlock _status = ViewUi.Text("Создай паспорт: характеристики будут собраны с этого компьютера.");
        private readonly CheckBox _private = new CheckBox { Content = "Скрыть имя компьютера", Margin = new Thickness(8) };
        private readonly CheckBox _portrait = new CheckBox { Content = "Вертикальная карточка · 1080 × 1920", Margin = new Thickness(8) };
        private readonly ComboBox _style = new ComboBox { Width = 150, Margin = new Thickness(8) };
        private PcPassport _passport;
        private PerformanceRecord _lastResult;
        private PerformanceRecord _lastBenchmarks;
        public PassportView()
        {
            var root = new StackPanel();
            var intro = new StackPanel(); intro.Children.Add(BrandVisuals.Badge("BURMALK  /  PC PASSPORT"));
            intro.Children.Add(new TextBlock { Text = "Твой ПК. В одной карточке.", FontSize = 32, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 9) });
            intro.Children.Add(ViewUi.Text("Настоящие характеристики, результаты и ранг. Сохрани для себя или поделись своей сборкой.")); root.Children.Add(intro);
            var tools = new WrapPanel { Margin = new Thickness(0, 20, 0, 15) };
            var create = ViewUi.Button("СОЗДАТЬ ПАСПОРТ ПК", Generate); create.SetResourceReference(BackgroundProperty, "Accent"); create.SetResourceReference(ForegroundProperty, "AccentInk"); tools.Children.Add(create);
            foreach (var name in new[] { "Pulse", "Aurora", "Graphite" }) _style.Items.Add(name);
            _style.SelectedItem = App.Store.Settings.PassportStyle;
            if (_style.SelectedIndex < 0) _style.SelectedIndex = 0;
            _private.IsChecked = App.Store.Settings.PassportHidePersonal;
            tools.Children.Add(_style); tools.Children.Add(_private); tools.Children.Add(_portrait); root.Children.Add(tools);
            root.Children.Add(_status);
            var frame = new Border { Child = _preview, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 12), ClipToBounds = true };
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderColor"); root.Children.Add(frame);
            var export = new WrapPanel();
            export.Children.Add(ViewUi.Button("СКАЧАТЬ PNG", () => Save(false)));
            export.Children.Add(ViewUi.Button("СКАЧАТЬ PDF", () => Save(true)));
            export.Children.Add(ViewUi.Button("КОПИРОВАТЬ", Copy)); root.Children.Add(export);
            root.Children.Add(ViewUi.Text("Серийные номера и уникальные идентификаторы не включаются. PDF содержит подробные характеристики; PNG — карточка с основными данными."));
            root.Children.Add(_details); Content = root;
            _style.SelectionChanged += (s, e) => ChangeOptions(); _private.Click += (s, e) => ChangeOptions();
            _portrait.Click += (s, e) => Render();
        }
        private void ChangeOptions()
        {
            App.Store.Settings.PassportStyle = _style.SelectedItem?.ToString() ?? "Pulse";
            App.Store.Settings.PassportHidePersonal = _private.IsChecked == true; App.Store.Save();
            if (_passport != null) Generate();
        }
        internal void Generate()
        {
            if (!App.Monitor.Hardware.ProbeCompleted) { _status.Text = "Характеристики ещё определяются. Подожди несколько секунд."; return; }
            try
            {
                _passport = PassportService.Create(_private.IsChecked == true, _style.SelectedItem?.ToString() ?? "Pulse");
                _lastResult = App.Store.Performance; _lastBenchmarks = App.Store.Benchmarks; Render(); _details.Children.Clear();
                foreach (var section in _passport.Sections) _details.Children.Add(ViewUi.Panel(section.Icon + "  " + section.Title, ViewUi.Text(string.Join("\n", section.Lines))));
                _status.Text = "Паспорт создан · " + _passport.CreatedLocal.ToString("dd.MM.yyyy HH:mm") + " · всё хранится локально";
            }
            catch (Exception ex) { _status.Text = "Не удалось создать паспорт: " + ex.Message; ErrorLog.Write("Passport: " + ex); }
        }
        private void Render() { if (_passport != null) _preview.Source = PassportExport.Card(_passport, _portrait.IsChecked == true); }
        private void Save(bool pdf)
        {
            if (_passport == null) Generate(); if (_passport == null) return;
            var dialog = new SaveFileDialog { FileName = "Burmalk-PC-Passport-" + DateTime.Now.ToString("yyyyMMdd"),
                Filter = pdf ? "PDF документ|*.pdf" : "PNG изображение|*.png", DefaultExt = pdf ? ".pdf" : ".png", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try { if (pdf) PassportExport.Pdf(_passport, dialog.FileName); else PassportExport.Png(_preview.Source as System.Windows.Media.Imaging.BitmapSource, dialog.FileName); _status.Text = "Сохранено: " + Path.GetFileName(dialog.FileName); }
            catch (Exception ex) { _status.Text = "Ошибка сохранения: " + ex.Message; ErrorLog.Write("Passport export: " + ex); }
        }
        private void Copy()
        {
            if (_passport == null) Generate(); if (_passport == null) return;
            try { Clipboard.SetImage((System.Windows.Media.Imaging.BitmapSource)_preview.Source); _status.Text = "Карточка скопирована. Вставь её через Ctrl + V."; }
            catch (Exception ex) { _status.Text = "Буфер обмена недоступен: " + ex.Message; }
        }
        public void Refresh() { if (_passport != null && (!ReferenceEquals(_lastResult, App.Store.Performance) || !ReferenceEquals(_lastBenchmarks, App.Store.Benchmarks))) Generate(); }
    }
}
